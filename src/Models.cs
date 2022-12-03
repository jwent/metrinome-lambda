using GraphQL;
using GraphQL.Authorization;



public class User {
	public Guid? Id { get; set; }
	public string? Email { get; set; }
	public string? Password { get; set; }
}

public class TrackingCampaign {
	public Guid Id { get; set; }
	public User? Owner { get; set; }

	public DateTime? CreatedAt { get; set; }
	public int? Audience { get; set; }

	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? TargetedCountries { get; set; }
	public string? CampaignBudget { get; set; }
	public string? CampaignDuration { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }
}


public class TrackingCampaignSubmission {
	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? TargetedCountries { get; set; }
	public string? CampaignBudget { get; set; }
	public string? CampaignDuration { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }
}



public class TrackerClick {
	public Guid? Id { get; set; }
	public TrackingCampaign? Campaign { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Ip { get; set; }
	public string? Useragent { get; set; }
	public string? Referer { get; set; }
}
