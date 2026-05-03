using GraphQL;
using GraphQL.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


[Index(nameof(Email), IsUnique=true)]
public class User {

	public Guid Id { get; set; }
	public UserOrganization Organization { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; }
	public string Password { get; set; }
	public DateTime CreatedAt { get; set; }
	public string ResetPasswordToken { get; set; }
	public string UserState { get; set; }
    public string? MagicLink { get; set; }

    [InverseProperty("Parent")]
	public List<UserExtraProperty> ExtraProperties { get; set; }
	[InverseProperty("OrganizationUser")]
	public List<UserOrganizationalRoleAssociation> UserRoles { get; set; }
}

public class UserExtraProperty {
	public Guid Id { get; set; }
	public User Parent { get; set; }
	public string PropertyKey { get; set; }
	public string PropertyValue { get; set; }
}

public class UserOrganizationalRoleAssociation {
	public Guid Id { get; set; }
	public User OrganizationUser { get; set; }
	public UserOrganization Organization { get; set; }
	public string RoleName { get; set; }
}

public class UserOrganization {
	public Guid Id { get; set; }
	public Guid CreatorId { get; set; }
	public DateTime CreatedAt { get; set; }
	public OrganizationalSubscriptionPlan? SubscriptionPlan { get; set; }
    public DateTime? SubscriptionTrialStartDate { get; set; }

    [InverseProperty("Organization")]
	public List<User> Users { get; set; }
	[InverseProperty("Organization")]
	public List<UserTracker> OrganizationalTrackers { get; set; }
}

public class OrganizationalSubscriptionPlan {
	public Guid Id { get; set; }
	public string PlanKey { get; set; }
	public string PlanName { get; set; }

	public int UsersLimitPerPlan { get; set; }
	public int CampaignsLimitPerPlan { get; set; }
	public bool CanUseInsightAnalytics { get; set; }
	public bool IsFreePlan { get; set; }
}

public class OrganizationCveContract {
	public Guid Id { get; set; }
	public Guid OrganizationId { get; set; }
	public string TierName { get; set; }
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
	public string SiteName { get; set; }
	public string Domain { get; set; }
	public string TrackingId { get; set; }
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
	public string Status { get; set; }
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
	public UserOrganization Organization { get; set; }
	public DateTime CreatedAt { get; set; }

	[InverseProperty("ParentTracker")]
	public List<TrackingCampaign> Campaigns { get; set; }
}

public class TrackingCampaign {
	public Guid Id { get; set; }
	public UserTracker ParentTracker { get; set; }

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
	public List<TrackerClick> Clicks { get; set; }
	[InverseProperty("Parent")]
	public List<TrackingCampaignExtraProperty> ExtraProperties { get; set; }
}

public class TrackingCampaignExtraProperty {
	public Guid Id { get; set; }
	public TrackingCampaign Parent { get; set; }
	public string PropertyKey { get; set; }
	public string PropertyValue { get; set; }
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
	public List<TrackerClickExtraProperty> ExtraProperties { get; set; }
}

public class TrackerClickExtraProperty {
	public Guid Id { get; set; }
	public TrackerClick ClickParent { get; set; }
	public string PropertyKey { get; set; }
	public string PropertyValue { get; set; }
}
