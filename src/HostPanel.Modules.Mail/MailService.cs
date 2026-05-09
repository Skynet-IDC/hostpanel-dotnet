using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Modules.Mail;

public class MailService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MailService> _logger;

    public MailService(AppDbContext db, ILogger<MailService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Mailbox> CreateMailboxAsync(int domainId, string localPart, string password, long quotaMb = 500, CancellationToken ct = default)
    {
        var domain = await _db.Domains
            .Include(d => d.Subscription).ThenInclude(s => s.ServicePlan)
            .Include(d => d.Mailboxes)
            .FirstOrDefaultAsync(d => d.Id == domainId, ct)
            ?? throw new InvalidOperationException("Domain not found");

        var currentCount = domain.Mailboxes.Count;
        if (currentCount >= domain.Subscription.ServicePlan.MaxEmailAccounts)
            throw new InvalidOperationException("Email account limit reached for this plan");

        var address = $"{localPart}@{domain.Name}";
        if (await _db.Mailboxes.AnyAsync(m => m.Address == address, ct))
            throw new InvalidOperationException("Email address already exists");

        var mailbox = new Mailbox
        {
            DomainId = domainId,
            Address = address,
            PasswordHash = HashPassword(password),
            QuotaMb = quotaMb
        };

        _db.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created mailbox {Address}", address);
        return mailbox;
    }

    public async Task DeleteMailboxAsync(int mailboxId, CancellationToken ct = default)
    {
        var mailbox = await _db.Mailboxes.FindAsync([mailboxId], ct)
            ?? throw new InvalidOperationException("Mailbox not found");

        _db.Mailboxes.Remove(mailbox);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted mailbox {Address}", mailbox.Address);
    }

    public async Task<MailAlias> CreateAliasAsync(int domainId, string source, string destination, CancellationToken ct = default)
    {
        var domain = await _db.Domains.FindAsync([domainId], ct)
            ?? throw new InvalidOperationException("Domain not found");

        var alias = new MailAlias
        {
            DomainId = domainId,
            Source = source.Contains('@') ? source : $"{source}@{domain.Name}",
            Destination = destination
        };

        _db.MailAliases.Add(alias);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created alias {Source} -> {Dest}", alias.Source, alias.Destination);
        return alias;
    }

    public async Task UpdatePasswordAsync(int mailboxId, string newPassword, CancellationToken ct = default)
    {
        var mailbox = await _db.Mailboxes.FindAsync([mailboxId], ct)
            ?? throw new InvalidOperationException("Mailbox not found");

        mailbox.PasswordHash = HashPassword(newPassword);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Generate Postfix main.cf configuration supporting multiple ports.
    /// Key ports: 25 (receive), 587 (submission/STARTTLS), 465 (SMTPS), 2525 (alternate submission).
    /// </summary>
    public string GeneratePostfixMainCf(string hostname, SmtpRelayConfig? relay)
    {
        var config = $"""
            # HostPanel Postfix Configuration
            myhostname = {hostname}
            mydomain = {hostname}
            myorigin = $mydomain
            mydestination = localhost

            # Virtual mailbox configuration
            virtual_mailbox_domains = proxy:mysql:/etc/postfix/mysql-virtual-domains.cf
            virtual_mailbox_maps = proxy:mysql:/etc/postfix/mysql-virtual-mailbox-maps.cf
            virtual_alias_maps = proxy:mysql:/etc/postfix/mysql-virtual-alias-maps.cf
            virtual_transport = lmtp:unix:private/dovecot-lmtp

            # TLS settings
            smtpd_use_tls = yes
            smtpd_tls_cert_file = /etc/ssl/certs/mail.pem
            smtpd_tls_key_file = /etc/ssl/private/mail.key
            smtpd_tls_security_level = may
            smtp_tls_security_level = may

            # SASL authentication
            smtpd_sasl_type = dovecot
            smtpd_sasl_path = private/auth
            smtpd_sasl_auth_enable = yes
            smtpd_recipient_restrictions = permit_sasl_authenticated, permit_mynetworks, reject_unauth_destination

            # Message size limit (50MB)
            message_size_limit = 52428800
            """;

        // Add SMTP relay if configured (bypasses port 25 blocks)
        if (relay is not null && !string.IsNullOrEmpty(relay.RelayHost))
        {
            config += $"""

                # SMTP Relay (bypasses port 25 blocks)
                relayhost = [{relay.RelayHost}]:{relay.RelayPort}
                smtp_sasl_auth_enable = yes
                smtp_sasl_password_maps = hash:/etc/postfix/sasl_passwd
                smtp_sasl_security_options = noanonymous
                smtp_tls_security_level = encrypt
                """;
        }

        return config;
    }

    /// <summary>
    /// Generate Postfix master.cf with multi-port support.
    /// Handles: port 25 (receive), 587 (submission), 465 (smtps), 2525 (alternate).
    /// </summary>
    public string GeneratePostfixMasterCf()
    {
        return """
            # HostPanel Postfix master.cf - Multi-port SMTP

            # Standard SMTP (port 25) - for receiving mail
            smtp      inet  n       -       y       -       -       smtpd

            # Submission (port 587) - client sending with STARTTLS
            submission inet n       -       y       -       -       smtpd
              -o syslog_name=postfix/submission
              -o smtpd_tls_security_level=encrypt
              -o smtpd_sasl_auth_enable=yes
              -o smtpd_tls_auth_only=yes
              -o smtpd_reject_unlisted_recipient=no
              -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
              -o milter_macro_daemon_name=ORIGINATING

            # SMTPS (port 465) - implicit TLS
            smtps     inet  n       -       y       -       -       smtpd
              -o syslog_name=postfix/smtps
              -o smtpd_tls_wrappermode=yes
              -o smtpd_sasl_auth_enable=yes
              -o smtpd_reject_unlisted_recipient=no
              -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
              -o milter_macro_daemon_name=ORIGINATING

            # Alternate submission (port 2525) - fallback if 587 is blocked
            2525      inet  n       -       y       -       -       smtpd
              -o syslog_name=postfix/alt-submission
              -o smtpd_tls_security_level=encrypt
              -o smtpd_sasl_auth_enable=yes
              -o smtpd_tls_auth_only=yes
              -o smtpd_reject_unlisted_recipient=no
              -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
              -o milter_macro_daemon_name=ORIGINATING

            # Dovecot LDA
            dovecot   unix  -       n       n       -       -       pipe
              flags=DRhu user=vmail:vmail argv=/usr/lib/dovecot/deliver -f ${sender} -d ${recipient}
            """;
    }

    /// <summary>
    /// Generate Dovecot config for IMAP/POP3.
    /// </summary>
    public string GenerateDovecotConf()
    {
        return """
            # HostPanel Dovecot Configuration
            protocols = imap pop3 lmtp

            listen = *, ::

            mail_location = maildir:/var/vmail/%d/%n/Maildir
            mail_uid = vmail
            mail_gid = vmail

            # Authentication
            auth_mechanisms = plain login

            passdb {
              driver = sql
              args = /etc/dovecot/dovecot-sql.conf.ext
            }

            userdb {
              driver = sql
              args = /etc/dovecot/dovecot-sql.conf.ext
            }

            # SSL
            ssl = required
            ssl_cert = </etc/ssl/certs/mail.pem
            ssl_key = </etc/ssl/private/mail.key
            ssl_min_protocol = TLSv1.2

            # IMAP settings
            protocol imap {
              mail_max_userip_connections = 20
            }

            # LMTP for Postfix delivery
            service lmtp {
              unix_listener /var/spool/postfix/private/dovecot-lmtp {
                group = postfix
                mode = 0600
                user = postfix
              }
            }

            # Auth service for Postfix SASL
            service auth {
              unix_listener /var/spool/postfix/private/auth {
                mode = 0660
                user = postfix
                group = postfix
              }
            }

            # Quotas
            plugin {
              quota = maildir:User quota
              quota_rule = *:storage=500M
              quota_grace = 10%%
            }
            """;
    }

    /// <summary>
    /// Generate Rspamd configuration for spam filtering.
    /// </summary>
    public string GenerateRspamdConfig()
    {
        return """
            # HostPanel Rspamd Configuration

            # Actions
            actions {
              reject = 15;
              add_header = 6;
              greylist = 4;
            }

            # DKIM signing
            dkim_signing {
              path = "/var/lib/rspamd/dkim/$domain.$selector.key";
              selector = "hostpanel";
              allow_username_mismatch = true;
            }

            # Redis for caching
            redis {
              servers = "127.0.0.1";
            }
            """;
    }

    private static string HashPassword(string password)
    {
        // SSHA512 for Dovecot compatibility
        using var sha = System.Security.Cryptography.SHA512.Create();
        var salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        var combined = System.Text.Encoding.UTF8.GetBytes(password).Concat(salt).ToArray();
        var hash = sha.ComputeHash(combined);
        return "{SSHA512}" + Convert.ToBase64String(hash.Concat(salt).ToArray());
    }
}
