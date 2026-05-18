using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>
/// Wires a Let's Encrypt cert (fullchain.pem + privkey.pem) into Postfix and Dovecot
/// so SMTP/IMAP clients see a valid cert instead of the snakeoil self-signed one.
/// Called automatically by SslService after issuing a mail.{domain} cert.
/// Can also be triggered manually from the panel SSL section.
/// </summary>
public class MailConfigureSslHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MailConfigureSsl;
    private readonly ILogger<MailConfigureSslHandler> _logger;
    public MailConfigureSslHandler(ILogger<MailConfigureSslHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var certPath = GetStr(parameters, "certPath");
        var keyPath = GetStr(parameters, "keyPath");
        var domain = GetStr(parameters, "domain");

        if (string.IsNullOrEmpty(certPath) || string.IsNullOrEmpty(keyPath))
            return Fail("certPath and keyPath are required");

        if (!File.Exists(certPath) || !File.Exists(keyPath))
            return Fail($"Cert or key file not found: {certPath} / {keyPath}");

        _logger.LogInformation("Configuring mail TLS for {Domain} with cert {Cert}", domain, certPath);

        // Postfix: set TLS cert/key paths
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", $"smtpd_tls_cert_file = {certPath}"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", $"smtpd_tls_key_file = {keyPath}"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "smtpd_tls_security_level = may"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "smtp_tls_security_level = may"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postfix", ["reload"], ct);

        // Dovecot: write ssl cert/key to 10-ssl.conf
        const string dovecotSslConf = "/etc/dovecot/conf.d/10-ssl.conf";
        var sslConf = $"""
ssl = yes
ssl_cert = <{certPath}
ssl_key = <{keyPath}
ssl_min_protocol = TLSv1.2
ssl_prefer_server_ciphers = yes
""";
        await File.WriteAllTextAsync(dovecotSslConf, sslConf, ct);
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["reload", "dovecot"], ct);

        _logger.LogInformation("Mail TLS configured for {Domain}", domain);
        return new AgentResult
        {
            Success = true,
            Data = new() { ["certPath"] = certPath, ["domain"] = domain }
        };
    }

    private static string GetStr(Dictionary<string, object?> p, string k)
        => p.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

    private static AgentResult Fail(string msg) =>
        new() { Success = false, Data = new() { ["error"] = msg } };
}
