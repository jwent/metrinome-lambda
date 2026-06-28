using GraphQL;
using GraphQL.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


[Index(nameof(Email), IsUnique=true)]
public class User {

	public Guid Id { get; set; }
	public UserOrganization Organization { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public string ResetPasswordToken { get; set; } = string.Empty;
	public string UserState { get; set; } = string.Empty;
    public string? MagicLink { get; set; }

    [InverseProperty("Parent")]
	public List<UserExtraProperty> ExtraProperties { get; set; } = new();
	[InverseProperty("OrganizationUser")]
	public List<UserOrganizationalRoleAssociation> UserRoles { get; set; } = new();
}

public class UserExtraProperty {
	public Guid Id { get; set; }
	public User Parent { get; set; } = null!;
	public string PropertyKey { get; set; } = string.Empty;
	public string PropertyValue { get; set; } = string.Empty;
}

public class UserOrganizationalRoleAssociation {
	public Guid Id { get; set; }
	public User OrganizationUser { get; set; } = null!;
	public UserOrganization Organization { get; set; } = null!;
	public string RoleName { get; set; } = string.Empty;
}

public class UserOrganization {
	public Guid Id { get; set; }
	public Guid CreatorId { get; set; }
	public DateTime CreatedAt { get; set; }
	public OrganizationalSubscriptionPlan? SubscriptionPlan { get; set; }
    public DateTime? SubscriptionTrialStartDate { get; set; }

    [InverseProperty("Organization")]
	public List<User> Users { get; set; } = new();
	[InverseProperty("Organization")]
	public List<UserTracker> OrganizationalTrackers { get; set; } = new();
}

public class OrganizationalSubscriptionPlan {
	public Guid Id { get; set; }
	public string PlanKey { get; set; } = string.Empty;
	public string PlanName { get; set; } = string.Empty;

	public int UsersLimitPerPlan { get; set; }
	public int CampaignsLimitPerPlan { get; set; }
	public bool CanUseInsightAnalytics { get; set; }
	public bool IsFreePlan { get; set; }
}

public class OrganizationCveContract {
	public Guid Id { get; set; }
	public Guid OrganizationId { get; set; }
	public string TierName { get; set; } = string.Empty;
	public int CommittedAnnualCVEs { get; set; }
	public DateTime ContractStartDate { get; set; }
	public DateTime ContractEndDate { get; set; }
	public bool CVEHardLimitEnabled { get; set; }
	public DateTime? CVEWarning75SentAt { get; set; }
	public DateTime? CVEWarning90SentAt { get; set; }
	public DateTime? UpgradeRequiredTriggeredAt { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}

public class OrganizationSite {
	public Guid Id { get; set; }
	public Guid OrganizationId { get; set; }
	public string SiteName { get; set; } = string.Empty;
	public string Domain { get; set; } = string.Empty;
	public string TrackingId { get; set; } = string.Empty;
	public bool IsActive { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}

public class ConversionVerificationEvent {
	public Guid Id { get; set; }
	public Guid OrganizationId { get; set; }
	public Guid SiteId { get; set; }
	public Guid? ContractId { get; set; }
	public Guid? TrackerId { get; set; }
	public Guid? TrackingCampaignId { get; set; }
	public Guid? TrackerClickId { get; set; }
	public string? ExternalSubmissionId { get; set; }
	public string? ExternalConversionId { get; set; }
	public string? IdempotencyKey { get; set; }
	public DateTime SubmittedAtUtc { get; set; }
	public DateTime? OriginalEventTimestampUtc { get; set; }
	public string Status { get; set; } = string.Empty;
	public bool CountsTowardCve { get; set; }
	public DateTime? CountedAtUtc { get; set; }
	public Guid? DuplicateOfEventId { get; set; }
	public string? RejectionReason { get; set; }
	public string? RequestHash { get; set; }
	public string? Source { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime UpdatedAtUtc { get; set; }
}

public class UserTracker {
	public Guid Id { get; set; }
	public UserOrganization Organization { get; set; } = null!;
	public DateTime CreatedAt { get; set; }

	[InverseProperty("ParentTracker")]
	public List<TrackingCampaign> Campaigns { get; set; } = new();
}

public class TrackingCampaign {
	public Guid Id { get; set; }
	public UserTracker ParentTracker { get; set; } = null!;

	public DateTime CreatedAt { get; set; }
	public int? Audience { get; set; }

	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? CampaignBudget { get; set; }
	public string? ConversionValue { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }

	[InverseProperty("Campaign")]
	public List<TrackerClick> Clicks { get; set; } = new();
	[InverseProperty("Parent")]
	public List<TrackingCampaignExtraProperty> ExtraProperties { get; set; } = new();
}

public class TrackingCampaignExtraProperty {
	public Guid Id { get; set; }
	public TrackingCampaign Parent { get; set; } = null!;
	public string PropertyKey { get; set; } = string.Empty;
	public string PropertyValue { get; set; } = string.Empty;
}

public class TrackerClick {
	public Guid Id { get; set; }
	public UserTracker? ParentTracker { get; set; }
	public TrackingCampaign? Campaign { get; set; }
	public DateTime CreatedAt { get; set; }
	public string? Ip { get; set; }
	public string? ClickUrl { get; set; }
	public string? Useragent { get; set; }
	public string? Referer { get; set; }

	public bool? IsBotClick { get; set; }
	public DateTime? ConversionDate { get; set; }
	public bool? Conversion { get; set; }
	public bool? IsDesktop { get; set; }

	[InverseProperty("ClickParent")]
	public List<TrackerClickExtraProperty> ExtraProperties { get; set; } = new();
}

public class TrackerClickExtraProperty {
	public Guid Id { get; set; }
	public TrackerClick ClickParent { get; set; } = null!;
	public string PropertyKey { get; set; } = string.Empty;
	public string PropertyValue { get; set; } = string.Empty;
}
