using System.Security.Cryptography;

namespace HostPanel.Agent.Services;

public class TokenAuthService
{
    private readonly string _validToken;
    private readonly ILogger<TokenAuthService> _logger;

    public TokenAuthService(IConfiguration config, ILogger<TokenAuthService> logger)
    {
        _logger = logger;
        var tokenPath = config["Agent:TokenPath"] ?? "/etc/hostpanel/agent.token";

        if (File.Exists(tokenPath))
        {
            _validToken = File.ReadAllText(tokenPath).Trim();
            _logger.LogInformation("Loaded agent token from {Path}", tokenPath);
        }
        else
        {
            // Generate and save a new token for development
            _validToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var dir = Path.GetDirectoryName(tokenPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                File.WriteAllText(tokenPath, _validToken);
                _logger.LogInformation("Generated new agent token at {Path}", tokenPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write token file to {Path}, using in-memory token", tokenPath);
            }
        }
    }

    public bool ValidateToken(string token) =>
        !string.IsNullOrEmpty(token) &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(token),
            System.Text.Encoding.UTF8.GetBytes(_validToken));

    public string GetToken() => _validToken;
}
