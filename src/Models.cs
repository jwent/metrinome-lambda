using GraphQL;
using GraphQL.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


[Index(nameof(Email), IsUnique=true)]
public class User {
	public Guid? Id { get; set; }
	public string? Email { get; set; }
	public string? Password { get; set; }
	public DateTime? CreatedAt { get; set; }
}

public class UserTracker {
	public Guid Id { get; set; }
	public User? Owner { get; set; }
	public DateTime? CreatedAt { get; set; }
}

public class TrackingCampaign {
	public Guid Id { get; set; }
	public UserTracker? ParentTracker { get; set; }

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


public class TrackingCampaignData {
	public TrackingCampaignData(TrackingCampaign campaign, int Clicks, int UniqueClicks, int BotClicks) {
		this.Id = campaign.Id;
		this.CreatedAt = campaign.CreatedAt;
		this.Platform = campaign.Platform;
		this.CampaignName = campaign.CampaignName;
		this.TargetedCountries = campaign.TargetedCountries;
		this.CampaignBudget = campaign.CampaignBudget;
		this.CampaignDuration = campaign.CampaignDuration;
		this.WebsiteDomain = campaign.WebsiteDomain;
		this.CartPageURL = campaign.CartPageURL;
		this.LandingPageURL = campaign.LandingPageURL;
		this.PrivacyPageURL = campaign.PrivacyPageURL;
		this.Clicks = Clicks;
		this.UniqueClicks = UniqueClicks;
		this.BotClicks = BotClicks;
	}

	public Guid Id { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? TargetedCountries { get; set; }
	public string? CampaignBudget { get; set; }
	public string? CampaignDuration { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }

	public int? Clicks { get; set; }
	public int? UniqueClicks { get; set; }
	public int? BotClicks { get; set; }
}

public class TrackerClickData {
	public TrackerClickData(TrackerClick click) {
		this.Id = click.Id;
		this.ParentTracker = click.ParentTracker;
		this.Campaign = click.Campaign;
		this.CreatedAt = click.CreatedAt;
		this.Ip = click.Ip;
		this.ClickUrl = click.ClickUrl;
		this.Useragent = click.Useragent;
		this.Referer = click.Referer;
		this.IsBotClick = click.IsBotClick;
	}

	public Guid? Id { get; set; }
	public UserTracker? ParentTracker { get; set; }
	public TrackingCampaign? Campaign { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Ip { get; set; }
	public string? ClickUrl { get; set; }
	public string? Useragent { get; set; }
	public string? Referer { get; set; }

	public bool? IsBotClick { get; set; }
}



public class TrackerClick {
	public Guid? Id { get; set; }
	public UserTracker? ParentTracker { get; set; }
	public TrackingCampaign? Campaign { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Ip { get; set; }
	public string? ClickUrl { get; set; }
	public string? Useragent { get; set; }
	public string? Referer { get; set; }

	public bool? IsBotClick { get; set; }
}
public class TrackerClickExtraProperty {
	public Guid? Id { get; set; }
	public TrackerClick? ClickParent { get; set; }
	public string? PropertyKey { get; set; }
	public string? PropertyValue { get; set; }
}
