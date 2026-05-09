using HostPanel.Core.Enums;

namespace HostPanel.Core.DTOs;

public record CreateSubscriptionRequest(
    string UserId,
    int ServicePlanId,
    string MainDomain,
    BillingPeriod BillingPeriod = BillingPeriod.Monthly
);

public record SubscriptionDto(
    int Id,
    string UserId,
    string UserEmail,
    int ServicePlanId,
    string ServicePlanName,
    SubscriptionStatus Status,
    BillingPeriod BillingPeriod,
    string? SystemUsername,
    DateTime CreatedAt,
    List<DomainDto> Domains
);

public record CreateDomainRequest(
    int SubscriptionId,
    string Name,
    string PhpVersion = "8.3"
);

public record UpdateDomainRequest(
    string? PhpVersion,
    bool? SslEnabled
);

public record DomainDto(
    int Id,
    int SubscriptionId,
    string Name,
    string DocumentRoot,
    bool SslEnabled,
    string PhpVersion,
    bool IsMainDomain,
    DateTime CreatedAt
);
