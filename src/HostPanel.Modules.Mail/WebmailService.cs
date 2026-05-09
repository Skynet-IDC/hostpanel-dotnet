using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HostPanel.Modules.Mail;

/// <summary>
/// Manages webmail integration (Roundcube or Snappymail).
/// Generates Nginx reverse proxy configs for the webmail app per-domain.
/// </summary>
public class WebmailService
{
    private readonly string _webmailType;
    private readonly string _webmailUrl;
    private readonly ILogger<WebmailService> _logger;

    public WebmailService(IConfiguration config, ILogger<WebmailService> logger)
    {
        _logger = logger;
        _webmailType = config["Mail:Webmail:Type"] ?? "roundcube";   // "roundcube" or "snappymail"
        _webmailUrl = config["Mail:Webmail:InternalUrl"] ?? "http://127.0.0.1:9080";
    }

    /// <summary>
    /// Generate Nginx location block for webmail access at /webmail on the domain.
    /// </summary>
    public string GenerateNginxWebmailLocation(string domain)
    {
        return $$"""
                # Webmail ({{_webmailType}}) for {{domain}}
                location /webmail/ {
                    proxy_pass {{_webmailUrl}}/;
                    proxy_set_header Host $host;
                    proxy_set_header X-Real-IP $remote_addr;
                    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                    proxy_set_header X-Forwarded-Proto $scheme;

                    # Pre-fill domain for login
                    proxy_set_header X-Webmail-Domain {{domain}};
                }
            """;
    }

    /// <summary>
    /// Generate Roundcube config.inc.php for this HostPanel installation.
    /// </summary>
    public string GenerateRoundcubeConfig(string imapHost, string smtpHost)
    {
        var desKey = Guid.NewGuid().ToString("N");
        return $$"""
            <?php
            // HostPanel Roundcube Configuration
            $config['db_dsnw'] = 'sqlite:////var/lib/roundcube/db/roundcube.db';
            $config['imap_host'] = 'ssl://{{imapHost}}:993';
            $config['smtp_host'] = 'tls://{{smtpHost}}:587';
            $config['smtp_user'] = '%u';
            $config['smtp_pass'] = '%p';
            $config['product_name'] = 'HostPanel Webmail';
            $config['des_key'] = '{{desKey}}';
            $config['plugins'] = ['archive', 'zipdownload', 'managesieve'];
            $config['skin'] = 'elastic';
            $config['language'] = 'en_US';
            $config['spellcheck_engine'] = 'pspell';
            ?>
            """;
    }

    /// <summary>
    /// Generate Snappymail config for this HostPanel installation.
    /// </summary>
    public string GenerateSnappymailDomainConfig(string domain)
    {
        return $$"""
            [IMAP]
            host = "127.0.0.1"
            port = 993
            secure = "SSL"

            [SMTP]
            host = "127.0.0.1"
            port = 587
            secure = "STARTTLS"

            [General]
            domain = "{{domain}}"
            """;
    }

    public string GetWebmailType() => _webmailType;
    public string GetWebmailUrl() => _webmailUrl;
}
