using HostPanel.Core.DTOs;

namespace HostPanel.Agent.Services;

/// <summary>
/// Provisions postfix + dovecot for a single virtual domain.
/// Called after any mailbox/alias create/delete so postfix file maps stay in sync
/// with the HostPanel SQL Server database.
///
/// Uses file-based maps (not MySQL) because the HostPanel main DB is SQL Server
/// and postfix cannot proxy-query it directly.
/// </summary>
public class MailProvisionHandler : IAgentMethodHandler
{
    public string Method => AgentMethods.MailProvision;

    private readonly ILogger<MailProvisionHandler> _logger;
    public MailProvisionHandler(ILogger<MailProvisionHandler> logger) => _logger = logger;

    public async Task<AgentResult> HandleAsync(Dictionary<string, object?> parameters, CancellationToken ct)
    {
        var domain = GetStr(parameters, "domain");
        var hostname = GetStr(parameters, "hostname") ?? $"mail.{domain}";
        var mailboxes = GetList(parameters, "mailboxes");   // [{address, passwordHash, quotaMb}]
        var aliases = GetList(parameters, "aliases");       // [{source, destination}]

        if (string.IsNullOrEmpty(domain))
            return Fail("domain is required");

        _logger.LogInformation("Provisioning mail for {Domain} ({Boxes} boxes, {Aliases} aliases)",
            domain, mailboxes.Count, aliases.Count);

        // 1. Ensure vmail user + base directory exist
        await ProcessRunner.RunAsync("/usr/sbin/groupadd", ["-g", "5000", "vmail"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/useradd",
            ["-g", "vmail", "-u", "5000", "-d", "/var/vmail", "-m", "-s", "/usr/sbin/nologin", "vmail"], ct);
        Directory.CreateDirectory("/var/vmail");
        await ProcessRunner.RunAsync("/bin/chown", ["vmail:vmail", "/var/vmail"], ct);

        // 2. Create Maildir spool directories for each mailbox
        foreach (var box in mailboxes)
        {
            var addr = box.TryGetValue("address", out var a) ? a?.ToString() ?? "" : "";
            var local = addr.Contains('@') ? addr.Split('@')[0] : addr;
            var spoolPath = $"/var/vmail/{domain}/{local}/Maildir";
            Directory.CreateDirectory(spoolPath);
            await ProcessRunner.RunAsync("/bin/chown", ["-R", "vmail:vmail", $"/var/vmail/{domain}"], ct);
        }

        // 3. Write postfix virtual_domains (one domain per line)
        await File.WriteAllTextAsync("/etc/postfix/virtual_domains", domain + "\n", ct);

        // 4. Write postfix vmailbox map (address → mailbox path relative to virtual_mailbox_base)
        var vmailbox = mailboxes
            .Select(b => {
                var addr = b.TryGetValue("address", out var a) ? a?.ToString() ?? "" : "";
                var local = addr.Contains('@') ? addr.Split('@')[0] : addr;
                return $"{addr}   {domain}/{local}/Maildir/";
            });
        await File.WriteAllTextAsync("/etc/postfix/vmailbox", string.Join("\n", vmailbox) + "\n", ct);

        // 5. Write postfix virtual_alias map
        var aliasLines = aliases
            .Select(al => {
                var src = al.TryGetValue("source", out var s) ? s?.ToString() ?? "" : "";
                var dst = al.TryGetValue("destination", out var d) ? d?.ToString() ?? "" : "";
                return $"{src}   {dst}";
            });
        await File.WriteAllTextAsync("/etc/postfix/virtual_alias", string.Join("\n", aliasLines) + "\n", ct);

        // 6. postmap the hash maps
        await ProcessRunner.RunAsync("/usr/sbin/postmap", ["/etc/postfix/vmailbox"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postmap", ["/etc/postfix/virtual_alias"], ct);

        // 7. Apply postfix virtual_mailbox settings (idempotent postconf calls)
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", $"myhostname = {hostname}"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "mydomain = " + domain], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "myorigin = " + domain], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "mydestination = localhost"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_mailbox_domains = /etc/postfix/virtual_domains"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_mailbox_base = /var/vmail"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_mailbox_maps = hash:/etc/postfix/vmailbox"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_alias_maps = hash:/etc/postfix/virtual_alias"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_transport = lmtp:unix:private/dovecot-lmtp"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_minimum_uid = 100"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_uid_maps = static:5000"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "virtual_gid_maps = static:5000"], ct);

        // 8. Write dovecot passwd-file for auth
        const string usersFile = "/etc/dovecot/users";
        var dovecotLines = mailboxes.Select(b => {
            var addr = b.TryGetValue("address", out var a) ? a?.ToString() ?? "" : "";
            var hash = b.TryGetValue("passwordHash", out var h) ? h?.ToString() ?? "" : "";
            var local = addr.Contains('@') ? addr.Split('@')[0] : addr;
            // Format: user@domain:{SSHA512}hash:5000:5000::/var/vmail/domain/local::
            return $"{addr}:{hash}:5000:5000::/var/vmail/{domain}/{local}::";
        });
        await File.WriteAllTextAsync(usersFile, string.Join("\n", dovecotLines) + "\n", ct);
        await ProcessRunner.RunAsync("/bin/chown", ["root:dovecot", usersFile], ct);
        await ProcessRunner.RunAsync("/bin/chmod", ["0640", usersFile], ct);

        // 9. Write dovecot auth-passwdfile config
        const string dovecotAuthConf = "/etc/dovecot/conf.d/auth-passwdfile.conf.ext";
        await File.WriteAllTextAsync(dovecotAuthConf, """
passdb {
  driver = passwd-file
  args = scheme=SSHA512 username_format=%u /etc/dovecot/users
}
userdb {
  driver = passwd-file
  args = username_format=%u /etc/dovecot/users
  default_fields = uid=vmail gid=vmail home=/var/vmail/%d/%n
}
""", ct);

        // 10. Ensure dovecot mail_location is set for Maildir
        await ProcessRunner.RunAsync("/bin/bash",
            ["-c", "grep -q 'mail_location' /etc/dovecot/conf.d/10-mail.conf && " +
             "sed -i 's|^mail_location.*|mail_location = maildir:/var/vmail/%d/%n/Maildir|' /etc/dovecot/conf.d/10-mail.conf || " +
             "echo 'mail_location = maildir:/var/vmail/%d/%n/Maildir' >> /etc/dovecot/conf.d/10-mail.conf"], ct);

        // 11. Enable passwd-file auth, disable SQL auth in dovecot
        await ProcessRunner.RunAsync("/bin/bash",
            ["-c", "sed -i 's|^!include auth-sql|#!include auth-sql|' /etc/dovecot/conf.d/10-auth.conf 2>/dev/null; " +
             // Uncomment the passwdfile include (it exists but is commented by default)
             "sed -i 's|^#!include auth-passwdfile.conf.ext|!include auth-passwdfile.conf.ext|' /etc/dovecot/conf.d/10-auth.conf; " +
             // Add login mechanism alongside plain
             "sed -i 's|auth_mechanisms = plain$|auth_mechanisms = plain login|' /etc/dovecot/conf.d/10-auth.conf"], ct);

        // 12. Configure Postfix SASL via Dovecot socket
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "smtpd_sasl_type = dovecot"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "smtpd_sasl_path = private/auth"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e", "smtpd_sasl_auth_enable = yes"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postconf", ["-e",
            "smtpd_recipient_restrictions = permit_sasl_authenticated, permit_mynetworks, reject_unauth_destination"], ct);

        // 13. Enable dovecot-lmtp socket for Postfix (patch 10-master.conf once)
        await ProcessRunner.RunAsync("/bin/bash", ["-c", @"
python3 - <<'PYEOF'
with open('/etc/dovecot/conf.d/10-master.conf') as f: c = f.read()
old = 'service lmtp {\n  unix_listener lmtp {\n    #mode = 0666\n  }'
new = 'service lmtp {\n  unix_listener /var/spool/postfix/private/dovecot-lmtp {\n    mode = 0600\n    user = postfix\n    group = postfix\n  }'
if old in c:
    c = c.replace(old, new)
    with open('/etc/dovecot/conf.d/10-master.conf', 'w') as f: f.write(c)
PYEOF"], ct);

        // 14. Enable postfix auth socket in dovecot (patch 10-master.conf once)
        await ProcessRunner.RunAsync("/bin/bash", ["-c",
            "python3 -c \"\nimport re\nwith open('/etc/dovecot/conf.d/10-master.conf') as f: c=f.read()\n" +
            "c=c.replace('unix_listener /var/spool/postfix/private/auth {\\n  #  mode = 0666\\n  #}',\n" +
            "'unix_listener /var/spool/postfix/private/auth {\\n    mode = 0660\\n    user = postfix\\n    group = postfix\\n  }')\n" +
            "open('/etc/dovecot/conf.d/10-master.conf','w').write(c)\n\""], ct);

        // 15. Restart dovecot (needs restart not reload to create new sockets), reload postfix
        await ProcessRunner.RunAsync("/usr/bin/systemctl", ["restart", "dovecot"], ct);
        await ProcessRunner.RunAsync("/usr/sbin/postfix", ["reload"], ct);

        _logger.LogInformation("Mail provisioned for {Domain}", domain);

        return new AgentResult
        {
            Success = true,
            Data = new() { ["domain"] = domain, ["mailboxes"] = mailboxes.Count, ["aliases"] = aliases.Count }
        };
    }

    private static string GetStr(Dictionary<string, object?> p, string key)
        => p.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static List<Dictionary<string, object?>> GetList(Dictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return [];
        if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            return je.EnumerateArray()
                .Select(e => e.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.ToString()))
                .ToList();
        return [];
    }

    private static AgentResult Fail(string msg) =>
        new() { Success = false, Data = new() { ["error"] = msg } };
}
