using System.Security.Cryptography;

namespace HostPanel.Infrastructure.Services;

// Shared TOTP / Base32 helpers. Extracted from TwoFactorController so AuthController
// can enforce 2FA at login (UI-BUG-014 fix). Behavior preserved: HMAC-SHA1, 30s step,
// ±1 window for clock skew. Recovery codes use plain string match against the JSON
// array stored on TotpToken.RecoveryCodes.
public static class TotpHelper
{
    public static bool ValidateTotp(string secret, string code)
    {
        var secretBytes = Base32Decode(secret);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        for (var i = -1; i <= 1; i++)
        {
            var step = timeStep + i;
            var stepBytes = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);
            var paddedStep = new byte[8];
            Array.Copy(stepBytes, 0, paddedStep, 8 - stepBytes.Length, stepBytes.Length);

            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(paddedStep);
            var offset = hash[^1] & 0x0f;
            var otp = ((hash[offset] & 0x7f) << 24 |
                       (hash[offset + 1] & 0xff) << 16 |
                       (hash[offset + 2] & 0xff) << 8 |
                       (hash[offset + 3] & 0xff)) % 1000000;

            if (otp.ToString("D6") == code)
                return true;
        }
        return false;
    }

    public static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        return result.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        var result = new byte[input.Length * 5 / 8];
        int buffer = 0, bitsLeft = 0, idx = 0;
        foreach (var c in input)
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                result[idx++] = (byte)((buffer >> (bitsLeft - 8)) & 0xff);
                bitsLeft -= 8;
            }
        }
        return result;
    }
}
