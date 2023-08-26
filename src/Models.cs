using GraphQL;
using GraphQL.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


[Index(nameof(Email), IsUnique=true)]
public class User {

	public Guid Id { get; set; }
	public UserOrganization Organization { get; set; }
	public string Email { get; set; }
	public string Password { get; set; }
	public DateTime CreatedAt { get; set; }
	public string ResetPasswordToken { get; set; }
	public string UserState { get; set; }

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


