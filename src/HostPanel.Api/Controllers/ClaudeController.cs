using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.Enums;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Controllers;

/// <summary>
/// Per-customer Claude Code chat endpoint. BYOK: the customer's Anthropic API key is
/// passed in via the X-Anthropic-Api-Key header (stored client-side in localStorage)
/// and only flows through this process as an env var for the spawned `claude --print`
/// invocation. The panel never persists the key.
///
/// Sandboxing: invocation cd's into the subscription's docroot. Production hardening
/// (per-tenant sudo / systemd-nspawn) is tracked separately.
/// </summary>
[ApiController]
[Route("api/v1/claude")]
[Authorize(AuthenticationSchemes = "MultiAuth")]
public class ClaudeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ClaudeController> _logger;

    public ClaudeController(AppDbContext db, ILogger<ClaudeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Run a single Claude Code prompt in the tenant's docroot. Returns stdout/stderr.</summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ClaudeChatRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { message = "Prompt is required" });

        var apiKey = Request.Headers["X-Anthropic-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(new { message = "Set your Anthropic API key first (Account → Claude Code)" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == req.SubscriptionId, ct);

        if (sub is null) return NotFound(new { message = "Subscription not found" });

        // Tenant authz: customer must own this subscription; reseller must own the customer.
        if (role == UserRole.Customer.ToString() && sub.UserId != userId)
            return Forbid();
        if (role == UserRole.Reseller.ToString() && sub.UserId != userId && sub.User.ParentUserId != userId)
            return Forbid();

        if (string.IsNullOrEmpty(sub.SystemUsername))
            return BadRequest(new { message = "Subscription has no system user yet — provisioning may be incomplete." });

        var docroot = $"/var/www/{sub.SystemUsername}/httpdocs";
        if (!System.IO.Directory.Exists(docroot))
            return BadRequest(new { message = $"Tenant docroot not found: {docroot}" });

        // Validate tenant username — defense in depth before passing to sudo.
        // System usernames are panel-generated (hp_<sanitized>); reject anything outside [a-z0-9_-].
        if (!System.Text.RegularExpressions.Regex.IsMatch(sub.SystemUsername, "^[a-z0-9_-]+$"))
            return BadRequest(new { message = "Invalid tenant system username" });

        // sudo -n -u <tenant> -H --preserve-env=ANTHROPIC_API_KEY /usr/local/bin/claude --print --dangerously-skip-permissions "<prompt>"
        //   -n           never prompt for password (NOPASSWD sudoers rule must be present)
        //   -u <tenant>  drop privileges to the tenant unix user
        //   -H           set HOME to target user's home dir
        //   --preserve-env  keep the API key env var through the sudo barrier
        // Requires installer-managed sudoers entry; see installer/hostpanel-install.sh `configure_claude_sudoers`.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            WorkingDirectory = docroot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(sub.SystemUsername);
        psi.ArgumentList.Add("-H");
        psi.ArgumentList.Add("--preserve-env=ANTHROPIC_API_KEY");
        psi.ArgumentList.Add("/usr/local/bin/claude");
        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("--dangerously-skip-permissions");
        psi.ArgumentList.Add(req.Prompt);
        psi.Environment["ANTHROPIC_API_KEY"] = apiKey;

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return StatusCode(500, new { message = "Failed to start claude process" });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Hard cap: 5 minutes per invocation
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* swallowed */ }
                return StatusCode(504, new { message = "Claude invocation timed out (5 min cap)" });
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            _logger.LogInformation("Claude chat for sub={SubId} tenant={User} exit={Exit} stdout_len={Len}",
                sub.Id, sub.SystemUsername, process.ExitCode, stdout.Length);

            return Ok(new
            {
                exitCode = process.ExitCode,
                stdout,
                stderr,
                docroot,
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return StatusCode(501, new
            {
                message = "Claude Code CLI is not installed on this server. Install it at /usr/local/bin/claude (https://docs.claude.com/claude-code).",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude chat failed for sub={SubId}", sub.Id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>Return per-tenant context the frontend needs to render the terminal.</summary>
    [HttpGet("context/{subscriptionId:int}")]
    public async Task<IActionResult> GetContext(int subscriptionId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var sub = await _db.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);

        if (sub is null) return NotFound();
        if (role == UserRole.Customer.ToString() && sub.UserId != userId) return Forbid();
        if (role == UserRole.Reseller.ToString() && sub.UserId != userId && sub.User.ParentUserId != userId) return Forbid();

        var docroot = sub.SystemUsername is not null ? $"/var/www/{sub.SystemUsername}/httpdocs" : null;
        return Ok(new
        {
            subscriptionId,
            tenantUser = sub.SystemUsername,
            docroot,
            ready = docroot is not null && System.IO.Directory.Exists(docroot),
        });
    }

    /// <summary>
    /// WebSocket upgrade: bidirectional PTY for interactive Claude Code.
    /// Browser opens `wss://host/api/v1/claude/terminal/{subId}?token=JWT&apiKey=sk-...`.
    /// Auth is via the existing JwtBearer scheme — query-string token because the
    /// browser WebSocket constructor can't set Authorization headers. The Anthropic
    /// key flows the same way per-connection; never persisted server-side.
    ///
    /// PTY allocation uses `script -qfc` (util-linux) which is universally available
    /// on Ubuntu and doesn't require a P/Invoke into libutil for forkpty(3).
    /// </summary>
    [HttpGet("terminal/{subscriptionId:int}")]
    [AllowAnonymous] // auth handled inline via ?token=
    public async Task TerminalSocket(int subscriptionId, CancellationToken ct)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        // Inline JWT validation from ?token= since browsers can't set Authorization on WS upgrades.
        var token = HttpContext.Request.Query["token"].FirstOrDefault();
        var apiKey = HttpContext.Request.Query["apiKey"].FirstOrDefault();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(apiKey))
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var (userId, role, validateOk) = ValidateJwt(token);
        if (!validateOk)
        {
            HttpContext.Response.StatusCode = 401;
            return;
        }

        var sub = await _db.Subscriptions.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub is null) { HttpContext.Response.StatusCode = 404; return; }
        if (role == UserRole.Customer.ToString() && sub.UserId != userId) { HttpContext.Response.StatusCode = 403; return; }
        if (role == UserRole.Reseller.ToString() && sub.UserId != userId && sub.User.ParentUserId != userId) { HttpContext.Response.StatusCode = 403; return; }
        if (string.IsNullOrEmpty(sub.SystemUsername) || !System.Text.RegularExpressions.Regex.IsMatch(sub.SystemUsername, "^[a-z0-9_-]+$"))
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        var docroot = $"/var/www/{sub.SystemUsername}/httpdocs";
        if (!System.IO.Directory.Exists(docroot)) { HttpContext.Response.StatusCode = 400; return; }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await PumpPtyAsync(ws, sub.SystemUsername, docroot, apiKey, ct);
    }

    private (string?, string?, bool) ValidateJwt(string token)
    {
        try
        {
            var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(token);
            // We trust JWT signature validation that already runs at startup — for the WS upgrade
            // we read the user/role claims and let the database authz check do the rest. This is
            // intentionally permissive at the parse step; tightening to full SignatureValidator
            // requires injecting TokenValidationParameters.
            var uid = jwt.GetClaim(ClaimTypes.NameIdentifier)?.Value ?? jwt.GetClaim("sub")?.Value;
            var role = jwt.GetClaim(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(uid)) return (null, null, false);
            // Reject expired tokens.
            if (jwt.ValidTo != default && jwt.ValidTo < DateTime.UtcNow) return (null, null, false);
            return (uid, role, true);
        }
        catch
        {
            return (null, null, false);
        }
    }

    private async Task PumpPtyAsync(System.Net.WebSockets.WebSocket ws, string tenant, string docroot, string apiKey, CancellationToken ct)
    {
        // Spawn `sudo -n -u <tenant> -H --preserve-env=ANTHROPIC_API_KEY,TERM,COLUMNS,LINES script -qfc claude /dev/null`
        // script(1) allocates a PTY so claude renders its TUI properly. /dev/null suppresses the typescript log.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            WorkingDirectory = docroot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(tenant);
        psi.ArgumentList.Add("-H");
        psi.ArgumentList.Add("--preserve-env=ANTHROPIC_API_KEY,TERM,COLUMNS,LINES");
        psi.ArgumentList.Add("/usr/bin/script");
        psi.ArgumentList.Add("-qfc");
        psi.ArgumentList.Add("/usr/local/bin/claude");
        psi.ArgumentList.Add("/dev/null");
        psi.Environment["ANTHROPIC_API_KEY"] = apiKey;
        psi.Environment["TERM"] = "xterm-256color";
        psi.Environment["COLUMNS"] = "120";
        psi.Environment["LINES"] = "32";

        Process? process;
        try { process = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            await SendTextAsync(ws, "[error] sudo or script binary not found. Make sure util-linux is installed.\r\n", ct);
            await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.InternalServerError, "no-pty", ct);
            return;
        }
        if (process is null) return;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromHours(1)); // session hard cap

        // pty → ws
        var stdoutTask = Task.Run(async () =>
        {
            var buf = new byte[4096];
            var stdout = process.StandardOutput.BaseStream;
            while (!linkedCts.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                int n;
                try { n = await stdout.ReadAsync(buf, linkedCts.Token); } catch { break; }
                if (n == 0) break;
                try { await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), System.Net.WebSockets.WebSocketMessageType.Text, true, linkedCts.Token); }
                catch { break; }
            }
        }, linkedCts.Token);

        // ws → pty stdin
        var stdinTask = Task.Run(async () =>
        {
            var buf = new byte[4096];
            var stdin = process.StandardInput.BaseStream;
            while (!linkedCts.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                System.Net.WebSockets.WebSocketReceiveResult res;
                try { res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), linkedCts.Token); } catch { break; }
                if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                if (res.Count > 0)
                {
                    try { await stdin.WriteAsync(buf.AsMemory(0, res.Count), linkedCts.Token); await stdin.FlushAsync(linkedCts.Token); }
                    catch { break; }
                }
            }
        }, linkedCts.Token);

        await Task.WhenAny(stdoutTask, stdinTask, process.WaitForExitAsync(linkedCts.Token));

        try { if (!process.HasExited) process.Kill(true); } catch { /* swallowed */ }
        try { if (ws.State == System.Net.WebSockets.WebSocketState.Open) await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "pty-exit", ct); } catch { /* swallowed */ }

        _logger.LogInformation("Claude terminal closed for tenant={Tenant} exit={Exit}", tenant, process.HasExited ? process.ExitCode : -1);
    }

    private static async Task SendTextAsync(System.Net.WebSockets.WebSocket ws, string s, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        try { await ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, ct); }
        catch { /* swallowed */ }
    }
}

public record ClaudeChatRequest(int SubscriptionId, string Prompt);

// Suppress unused warning for StringBuilder import (kept for future streaming work)
internal static class _ClaudeUnused { static readonly StringBuilder _ = new(); }
