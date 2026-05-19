public class UserData {
	public string? Email { get; set; }
	public DateTime? CreatedAt { get; set; }
	public string? UserState { get; set; }
	public bool? Admin { get; set; }
	public string? MagicLink { get; set; }
	
	public string? FullName { get; set; }
	public List<string>? UserRoles { get; set; }
}


public class OrganizationData {
	public DateTime? CreatedAt { get; set; }
	public List<UserData>? Users { get; set; }
	public OrganizationalSubscriptionPlan? SubscriptionPlan { get; set; }
}

public class TrackingCampaignSubmission {
	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? CampaignBudget { get; set; }
	public string? ConversionValue { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }

	public string? CampaignType { get; set; }
	public string? PrimaryCampaignObjective { get; set; }
}

// public class TrackingCampaignSubmissionExtras {
//     public string? CampaignType { get; set; }
//     public string? PrimaryCampaignObjective { get; set; }
// }


public class TrackingCampaignData {
    public TrackingCampaignData(TrackingCampaign campaign, int Clicks, int UniqueClicks, int BotClicks, int Conversions, int DesktopClicks, TrackingCampaignExtraProperty ExtraCampaignType, int? Count = null)
    {
        this.Id = campaign.Id;
        this.CreatedAt = campaign.CreatedAt;
        this.Platform = campaign.Platform;
        this.CampaignName = campaign.CampaignName;
        this.CampaignBudget = campaign.CampaignBudget;
        this.ConversionValue = campaign.ConversionValue;
        this.WebsiteDomain = campaign.WebsiteDomain;
        this.CartPageURL = campaign.CartPageURL;
        this.LandingPageURL = campaign.LandingPageURL;
        this.PrivacyPageURL = campaign.PrivacyPageURL;
        this.Clicks = Clicks;
        this.UniqueClicks = UniqueClicks;
        this.BotClicks = BotClicks;
        this.Conversions = Conversions;
        this.DesktopClicks = DesktopClicks;
        this.Count = Count;
        this.CampaignType = ExtraCampaignType.PropertyValue;
    }

	public Guid Id { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? CampaignBudget { get; set; }
	public string? ConversionValue { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }

	public int? Clicks { get; set; }
	public int? UniqueClicks { get; set; }
	public int? BotClicks { get; set; }
	public int? Conversions { get; set; }
	public int? DesktopClicks { get; set; }
    public int? Count { get; set; }
    public string? CampaignType { get; set; }
}

public class TrackerClickData {
	public TrackerClickData(TrackerClick click, TrackerClickExtraProperty extraCountry, TrackerClickExtraProperty extraRegion, TrackerClickExtraProperty extraCity) {
		this.Id = click.Id;
		// this.ParentTracker = click.ParentTracker;
		// this.Campaign = click.Campaign;
		this.CreatedAt = click.CreatedAt;
		this.Ip = click.Ip;
		this.ClickUrl = click.ClickUrl;
		this.Useragent = click.Useragent;
		this.Referer = click.Referer;
		this.IsBotClick = click.IsBotClick;
		this.Conversion=click.Conversion;
		this.Country = extraCountry.PropertyValue;
		this.Region = extraRegion.PropertyValue;
		this.City = extraCity.PropertyValue;
	}

	public Guid? Id { get; set; }
	// public UserTracker? ParentTracker { get; set; }
	// public TrackingCampaign? Campaign { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string? Ip { get; set; }
	public string? ClickUrl { get; set; }
	public string? Useragent { get; set; }
	public string? Referer { get; set; }

	public bool? IsBotClick { get; set; }
	public bool? Conversion { get; set; }

	public string? Country { get; set; }
	public string? Region { get; set; }
	public string? City { get; set; }
}



public class TrackingCampaignDetails {
	public TrackingCampaignDetails(TrackingCampaignData trackingCampaignData, Clicks clicks, ChartDatas chartDatas) {
		this.TrackingCampaignData=trackingCampaignData;
		this.Clicks=clicks;
		this.ChartDatas=chartDatas;
	}
	public TrackingCampaignData TrackingCampaignData { get; set; }
	public Clicks Clicks { get; set; }
	public ChartDatas ChartDatas { get; set; }
}
public class Campaigns
{
	public Campaigns(List<TrackingCampaignData> campaignDatas, int campaignCount)
	{
		this.CampaignDatas = campaignDatas;
		this.CampaignCount = campaignCount;
	}
	public List<TrackingCampaignData> CampaignDatas { get; set; }
	public int CampaignCount { get; set; }
}
public class Clicks
{
	public Clicks(List<TrackerClickData> clickDatas, int clickCount)
	{
		this.ClickDatas = clickDatas;
		this.ClickCount = clickCount;
	}
	public List<TrackerClickData> ClickDatas { get; set; }
	public int ClickCount { get; set; }
}
public class Location
{
	public string? City { get; set; }
	public string? Region { get; set; }
	public int ClickCount { get; set; }
	public int ConversionCount { get; set; }
}
public class ChartDatas
{
	public ChartDatas(List<Location> topLocations)
	{
		this.TopLocations=topLocations;
	}
	public List<Location> TopLocations { get; set; }
}

public class GetCampaignClickStatsResponse {
	public string? GroupedBy { get; set; }
	public List<CampaignClickStatPoint>? Stats { get; set; }
}
public class CampaignClickStatPoint {
	public string Position { get; set; }
	public int ClickCount { get; set; }
}
public class GetCampaignConversionStatsResponse
{
    public string? GroupedBy { get; set; }
    public List<CampaignConversionStatPoint>? Stats { get; set; }
}
public class CampaignConversionStatPoint
{
    public string Position { get; set; }
    public int ConversionCount { get; set; }
}
public class AddUserResponse {
	public Guid? Id { get; set; }
    public string? MagicLinkUrl { get; set; }
    public string? Error { get; set; }
}
public class SuccessResponse {
	public bool? Success { get; set; }
}

public class SuccessOrErrorResponse {
	public bool? Success { get; set; }
	public string? Error { get; set; }
}

public class CheckoutResponse
{
    public bool? Success { get; set; }
    public string? Url { get; set; }
    public string? Error { get; set; }
}

public class PostbackCodes
{
    public string? PagePostback { get; set; }
    public string? ButtonPostback { get; set; }
}

public class CreatePaymentIntentResponse
{
	public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ClientSecret { get; set; }
}

public class LoginUserResponse {
	public string? BearerToken { get; set; }
	public string? Error { get; set; }
}

public class CheckMagicLinkResult
{
    public bool HasValidMagicLink { get; set; }
    public bool IsExpired { get; set; }
    public string? Error { get; set; }
	public string? BearerToken { get; set; }
}

public class TrackerInsightsResponse {
	public string? PropertyType { get; set; }
	public string? GroupedBy { get; set; }
	public List<StatPoint>? Stats { get; set; }
}
public class StatPoint {
	public string? Position { get; set; }
	public int Count { get; set; }
}
public class CurrentSubscriptionResponse
{
    public bool? Success { get; set; }  // aligns with other responses like SuccessResponse
    public string? Status { get; set; } // e.g. "active", "canceled", "none"
    public string? PlanName { get; set; }
    public string? PlanKey { get; set; }
    public string? Error { get; set; }
	public string? CustomerName { get; set; }
}

public class AccountSummaryResponse
{
	public bool? Success { get; set; }
	public Guid? OrganizationId { get; set; }
	public string? OrganizationName { get; set; }
	public DateTime? OrganizationCreatedAt { get; set; }
	public List<UserData>? Users { get; set; }
	public OrganizationalSubscriptionPlan? SubscriptionPlan { get; set; }
	public string? SubscriptionStatus { get; set; }
	public int UserCount { get; set; }
	public int CampaignCount { get; set; }
	public int TotalCves { get; set; }
	public int CountedCves { get; set; }
	public int VerifiedCves { get; set; }
	public int CurrentPeriodCountedCves { get; set; }
	public int CurrentPeriodVerifiedCves { get; set; }
	public int CurrentPeriodProcessedCves { get; set; }
	public int? CurrentPeriodCveLimit { get; set; }
	public string? ContractTierName { get; set; }
	public int? CommittedAnnualCves { get; set; }
	public int? RemainingCommittedCves { get; set; }
	public long RatePerCveCents { get; set; }
	public long AnnualMinimumFeeCents { get; set; }
	public long AnnualContractValueCents { get; set; }
	public long CurrentPlanCostCents { get; set; }
	public long UsageCostCents { get; set; }
	public long UsageValueCents { get; set; }
	public string Currency { get; set; } = "usd";
	public string? CostCalculation { get; set; }
	public string? Error { get; set; }
}

public class AdminCveData
{
	public Guid Id { get; set; }
	public DateTime SubmittedAtUtc { get; set; }
	public DateTime? OriginalEventTimestampUtc { get; set; }
	public string? Status { get; set; }
	public bool CountsTowardCve { get; set; }
	public DateTime? CountedAtUtc { get; set; }
	public string? RejectionReason { get; set; }
	public string? Source { get; set; }
	public string? ExternalSubmissionId { get; set; }
	public string? ExternalConversionId { get; set; }
	public string? SiteName { get; set; }
	public string? Domain { get; set; }
	public string? TrackingId { get; set; }
	public string? ContractTierName { get; set; }
	public string? CampaignName { get; set; }
	public Guid? TrackingCampaignId { get; set; }
	public Guid? TrackerId { get; set; }
	public Guid? TrackerClickId { get; set; }
	public Guid? DuplicateOfEventId { get; set; }
}
