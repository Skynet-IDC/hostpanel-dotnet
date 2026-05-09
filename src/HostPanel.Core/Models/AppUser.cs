using Microsoft.AspNetCore.Identity;
using HostPanel.Core.Enums;

namespace HostPanel.Core.Models;

public class AppUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public string? ParentUserId { get; set; }
    public AppUser? ParentUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Subscription> Subscriptions { get; set; } = [];
    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
