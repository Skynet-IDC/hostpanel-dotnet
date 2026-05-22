using HostPanel.Core.Enums;

namespace HostPanel.Core.DTOs;

public record LoginRequest(string Email, string Password, string? Totp = null);

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? CompanyName,
    UserRole Role = UserRole.Customer
);

public record AuthResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User
);

public record UserDto(
    string Id,
    string Email,
    string FirstName,
    string LastName,
    string? CompanyName,
    UserRole Role,
    bool IsActive
);

public record RefreshTokenRequest(string RefreshToken);
