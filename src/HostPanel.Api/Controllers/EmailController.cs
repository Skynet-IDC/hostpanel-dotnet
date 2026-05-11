using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.Mail;
using HostPanel.Api.Middleware;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class EmailController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MailService _mailService;
    private readonly TenantAuthorizationService _auth;

    public EmailController(AppDbContext db, MailService mailService, TenantAuthorizationService auth)
    {
        _db = db;
        _mailService = mailService;
        _auth = auth;
    }

    // --- Mailboxes ---

    [HttpGet("domains/{domainId}/mailboxes")]
    public async Task<IActionResult> ListMailboxes(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var mailboxes = await _db.Mailboxes
            .Where(m => m.DomainId == domainId)
            .Select(m => new { m.Id, m.Address, m.QuotaMb, m.IsActive, m.CreatedAt })
            .ToListAsync();
        return Ok(mailboxes);
    }

    [HttpPost("domains/{domainId}/mailboxes")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> CreateMailbox(int domainId, [FromBody] CreateMailboxRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        try
        {
            var mailbox = await _mailService.CreateMailboxAsync(domainId, request.LocalPart, request.Password, request.QuotaMb);
            return CreatedAtAction(nameof(ListMailboxes), new { domainId }, new
            {
                mailbox.Id, mailbox.Address, mailbox.QuotaMb, mailbox.IsActive
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("mailboxes/{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> DeleteMailbox(int id)
    {
        if (!await _auth.CanAccessMailboxAsync(User, id)) return Forbid();

        try
        {
            await _mailService.DeleteMailboxAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPut("mailboxes/{id}/password")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordRequest request)
    {
        if (!await _auth.CanAccessMailboxAsync(User, id)) return Forbid();

        try
        {
            await _mailService.UpdatePasswordAsync(id, request.NewPassword);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    // --- Aliases ---

    [HttpGet("domains/{domainId}/aliases")]
    public async Task<IActionResult> ListAliases(int domainId)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var aliases = await _db.MailAliases
            .Where(a => a.DomainId == domainId)
            .Select(a => new { a.Id, a.Source, a.Destination, a.CreatedAt })
            .ToListAsync();
        return Ok(aliases);
    }

    [HttpPost("domains/{domainId}/aliases")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> CreateAlias(int domainId, [FromBody] CreateAliasRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        try
        {
            var alias = await _mailService.CreateAliasAsync(domainId, request.Source, request.Destination);
            return CreatedAtAction(nameof(ListAliases), new { domainId }, new
            {
                alias.Id, alias.Source, alias.Destination
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("aliases/{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> DeleteAlias(int id)
    {
        var alias = await _db.MailAliases.FindAsync(id);
        if (alias is null) return NotFound();

        if (!await _auth.CanAccessDomainAsync(User, alias.DomainId)) return Forbid();

        _db.MailAliases.Remove(alias);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Catch-All ---

    [HttpPut("domains/{domainId}/catchall")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<IActionResult> SetCatchAll(int domainId, [FromBody] CatchAllRequest request)
    {
        if (!await _auth.CanAccessDomainAsync(User, domainId)) return Forbid();

        var domain = await _db.Domains.FindAsync(domainId);
        if (domain is null) return NotFound();

        // Remove existing catch-all alias if any
        var existing = await _db.MailAliases
            .FirstOrDefaultAsync(a => a.DomainId == domainId && a.Source == $"@{domain.Name}");

        if (string.IsNullOrEmpty(request.ForwardTo))
        {
            if (existing is not null) _db.MailAliases.Remove(existing);
        }
        else
        {
            if (existing is not null)
            {
                existing.Destination = request.ForwardTo;
            }
            else
            {
                _db.MailAliases.Add(new Core.Models.MailAlias
                {
                    DomainId = domainId,
                    Source = $"@{domain.Name}",
                    Destination = request.ForwardTo
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { catchAll = request.ForwardTo ?? "disabled" });
    }

    // --- SMTP Relay Configuration ---

    [HttpGet("mail/relay")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> GetRelay()
    {
        var relay = await _db.Set<Core.Models.SmtpRelayConfig>().FirstOrDefaultAsync(r => r.IsGlobal);
        if (relay is null)
            return Ok(new { configured = false });

        return Ok(new
        {
            configured = true,
            relay.RelayHost,
            relay.RelayPort,
            relay.RelayUsername,
            relay.UseTls
        });
    }

    [HttpPut("mail/relay")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> SetRelay([FromBody] SetRelayRequest request)
    {
        var relay = await _db.Set<Core.Models.SmtpRelayConfig>().FirstOrDefaultAsync(r => r.IsGlobal);

        if (relay is null)
        {
            relay = new Core.Models.SmtpRelayConfig { IsGlobal = true };
            _db.Set<Core.Models.SmtpRelayConfig>().Add(relay);
        }

        relay.RelayHost = request.RelayHost;
        relay.RelayPort = request.RelayPort;
        relay.RelayUsername = request.RelayUsername;
        relay.RelayPassword = request.RelayPassword;
        relay.UseTls = request.UseTls;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // --- Port Configuration Info ---

    [HttpGet("mail/ports")]
    public IActionResult GetSupportedPorts()
    {
        return Ok(new
        {
            smtp = new { port = 25, description = "Standard SMTP (receiving)", protocol = "STARTTLS" },
            submission = new { port = 587, description = "Submission (recommended for sending)", protocol = "STARTTLS" },
            smtps = new { port = 465, description = "SMTPS (implicit TLS)", protocol = "TLS" },
            altSubmission = new { port = 2525, description = "Alternate submission (fallback if 587 blocked)", protocol = "STARTTLS" },
            imap = new { port = 993, description = "IMAPS", protocol = "TLS" },
            pop3 = new { port = 995, description = "POP3S", protocol = "TLS" }
        });
    }
}

public record CreateMailboxRequest(string LocalPart, string Password, long QuotaMb = 500);
public record ChangePasswordRequest(string NewPassword);
public record CreateAliasRequest(string Source, string Destination);
public record SetRelayRequest(string RelayHost, int RelayPort = 587, string? RelayUsername = null, string? RelayPassword = null, bool UseTls = true);
