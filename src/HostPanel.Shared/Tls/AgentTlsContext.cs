using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace HostPanel.Shared.Tls;

// Cross-host agent mTLS context. Both the listener side (TcpListenerWorker) and the
// caller side (RemoteServerAgent) load the same identity (own cert + key) and the same
// trust store (directory of peer certs). The shape mirrors what SslStream needs on
// each side so callers don't have to assemble it themselves.
//
// Trust model: drop-file. The installer generates a self-signed cert per host
// (server.crt + server.key under /etc/hostpanel/tls/). To enable mTLS, the operator
// drops the OTHER host's server.crt into /etc/hostpanel/tls/trusted-cas.d/ on each
// side, then flips Agent:Tcp:Tls:Enabled=true on both. Bearer token in Agent:Token is
// still validated after the TLS handshake — defense in depth, not replacement.
public sealed class AgentTlsContext
{
    public bool Enabled { get; }
    public X509Certificate2? Identity { get; }
    public X509Certificate2Collection TrustedRoots { get; }

    private AgentTlsContext(bool enabled, X509Certificate2? identity, X509Certificate2Collection roots)
    {
        Enabled = enabled;
        Identity = identity;
        TrustedRoots = roots;
    }

    // Disabled context — TLS off. TcpListenerWorker / RemoteServerAgent treat the
    // socket as raw NetworkStream and skip the handshake entirely.
    public static AgentTlsContext Disabled() => new(false, null, new X509Certificate2Collection());

    // Load identity + trust store from disk. Throws if enabled=true and any path is
    // missing — fail loud at startup rather than crash on first connection.
    public static AgentTlsContext Load(bool enabled, string? certPath, string? keyPath, string? trustedRootsDir)
    {
        if (!enabled)
            return Disabled();

        if (string.IsNullOrWhiteSpace(certPath) || !File.Exists(certPath))
            throw new InvalidOperationException($"Agent:Tcp:Tls:Enabled=true but certificate not found at '{certPath}'");
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            throw new InvalidOperationException($"Agent:Tcp:Tls:Enabled=true but private key not found at '{keyPath}'");

        // .NET 10 binds the PEM private key to the cert directly on Linux. Windows
        // would need a PFX round-trip; not needed because agents run on Ubuntu.
        var identity = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        var roots = new X509Certificate2Collection();
        if (!string.IsNullOrWhiteSpace(trustedRootsDir) && Directory.Exists(trustedRootsDir))
        {
            foreach (var file in Directory.EnumerateFiles(trustedRootsDir, "*.crt", SearchOption.TopDirectoryOnly))
            {
                try { roots.Add(X509CertificateLoader.LoadCertificateFromFile(file)); }
                catch (Exception ex) { throw new InvalidOperationException($"Failed to load trusted root '{file}': {ex.Message}", ex); }
            }
        }

        if (roots.Count == 0)
            throw new InvalidOperationException($"Agent:Tcp:Tls:Enabled=true but no trusted-root certs found under '{trustedRootsDir}'. mTLS handshake would reject every peer.");

        return new AgentTlsContext(true, identity, roots);
    }

    // Callback for SslStream (BOTH server and client side): accept a peer cert iff its
    // thumbprint OR issuer matches one in our drop-file trust store. Hostname mismatches
    // are tolerated because peers are addressed by IP in the HA mesh and the certs we
    // generate carry SANs for the LAN IP, not a DNS name. We deliberately do NOT consult
    // the OS root store — mTLS here is a closed mesh.
    public RemoteCertificateValidationCallback BuildValidationCallback() =>
        (sender, presented, chain, errors) =>
        {
            if (presented is null) return false;
            using var leaf = new X509Certificate2(presented);

            foreach (var root in TrustedRoots)
            {
                if (leaf.Thumbprint.Equals(root.Thumbprint, StringComparison.OrdinalIgnoreCase)) return true;
                if (leaf.Issuer.Equals(root.Subject, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        };
}
