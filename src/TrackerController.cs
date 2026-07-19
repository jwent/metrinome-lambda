using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

public class TrackerController {
	public static TrackingCampaign GetCampaignById(OnTrackDBContext onTrackDBContext, Guid userTrackerId, Guid id) {
		Console.WriteLine($"[+] searching campaigns by campaignId: ${id}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns.FirstOrDefault(e => e.Id == id && e.ParentTracker.Id == userTrackerId);
		if (existingCampaign == null)
			throw new Exception("campaign not found!");
		return existingCampaign;
	}

	public static UserTracker GetUserTrackerByUser(OnTrackDBContext onTrackDBContext, Guid userId) {
		var user = onTrackDBContext.Users
			.Include(u => u.Organization)
			.First(u => u.Id == userId);
		return onTrackDBContext.UserTrackers.First(t => t.Organization.Id == user.Organization.Id);
	}

	public static async Task<Guid> RegisterClickAsync(
		OnTrackDBContext onTrackDBContext,
		HttpRequest request,
		string trackerId,
		string? encodedUrl,
		string? encodedReferer)
	{
		if (!Guid.TryParse(trackerId, out var userTrackerId))
			throw new BadHttpRequestException("Invalid tracker id.");

		var userTracker = await onTrackDBContext.UserTrackers
			.Include(t => t.Organization)
			.FirstOrDefaultAsync(t => t.Id == userTrackerId);
		if (userTracker == null)
			throw new BadHttpRequestException("Tracker not found.");
		if (!UserController.CanTrackCves(onTrackDBContext, userTracker.Organization.Id, DateTime.UtcNow))
			throw new BadHttpRequestException("Organization subscription is not active for click tracking.");

		var clickUrl = DecodeBase64OrRaw(encodedUrl);
		var referer = DecodeBase64OrRaw(encodedReferer);
		var userAgent = request.Headers.UserAgent.ToString();
		var campaignId = ExtractCampaignId(clickUrl);
		var campaign = campaignId == null
			? null
			: await onTrackDBContext.TrackingCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.ParentTracker.Id == userTrackerId);

		var click = new TrackerClick {
			Id = Guid.NewGuid(),
			ParentTracker = userTracker,
			Campaign = campaign,
			CreatedAt = DateTime.UtcNow,
			Ip = GetClientIp(request),
			ClickUrl = clickUrl,
			Useragent = userAgent,
			Referer = referer,
			IsBotClick = IsBotUserAgent(userAgent),
			Conversion = false,
			IsDesktop = IsDesktopUserAgent(userAgent),
		};

		onTrackDBContext.TrackerClicks.Add(click);
		foreach (var prop in BuildLocationProperties(click, request))
			onTrackDBContext.TrackerClickExtraProperties.Add(prop);

		await onTrackDBContext.SaveChangesAsync();
		return click.Id;
	}

	public static async Task<bool> RegisterPostbackAsync(OnTrackDBContext onTrackDBContext, HttpRequest request, string clickId) {
		if (!Guid.TryParse(clickId, out var trackerClickId))
			throw new BadHttpRequestException("Invalid click id.");

		var trackerClick = await onTrackDBContext.TrackerClicks
			.Include(c => c.ParentTracker)
				.ThenInclude(t => t!.Organization)
					.ThenInclude(o => o.SubscriptionPlan)
			.Include(c => c.Campaign)
			.FirstOrDefaultAsync(c => c.Id == trackerClickId);
		if (trackerClick == null || trackerClick.ParentTracker == null)
			return false;

		var submittedAtUtc = DateTime.UtcNow;
		if (trackerClick.Conversion != true) {
			trackerClick.Conversion = true;
			trackerClick.ConversionDate = submittedAtUtc;
			await onTrackDBContext.SaveChangesAsync();
		}

		try {
			await CreateCveEventAsync(onTrackDBContext, request, trackerClick, submittedAtUtc);
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01") {
			Console.WriteLine($"[CVE] CVE schema missing, skipping CVE event creation for click {trackerClick.Id}: {ex.MessageText}");
		}

		return true;
	}

	public static async Task<bool> RegisterUnmatchedPostbackAsync(
		OnTrackDBContext onTrackDBContext,
		HttpRequest request,
		string? trackerId,
		string? encodedUrl,
		string? encodedReferer)
	{
		if (!Guid.TryParse(trackerId, out var userTrackerId))
			throw new BadHttpRequestException("Invalid tracker id.");

		var userTracker = await onTrackDBContext.UserTrackers
			.Include(t => t.Organization)
				.ThenInclude(o => o.SubscriptionPlan)
			.FirstOrDefaultAsync(t => t.Id == userTrackerId);
		if (userTracker == null)
			return false;

		var submittedAtUtc = DateTime.UtcNow;
		try {
			await CreateUnmatchedCveEventAsync(
				onTrackDBContext,
				request,
				userTracker,
				DecodeBase64OrRaw(encodedUrl),
				DecodeBase64OrRaw(encodedReferer),
				submittedAtUtc);
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01") {
			Console.WriteLine($"[CVE] CVE schema missing, skipping unmatched CVE event creation for tracker {userTracker.Id}: {ex.MessageText}");
		}

		return true;
	}

	private static async Task CreateCveEventAsync(
		OnTrackDBContext onTrackDBContext,
		HttpRequest request,
		TrackerClick trackerClick,
		DateTime submittedAtUtc)
	{
		var organization = trackerClick.ParentTracker!.Organization;
		var organizationId = organization.Id;
		var site = await FindOrCreateSiteAsync(onTrackDBContext, trackerClick, submittedAtUtc);
		var contract = await onTrackDBContext.OrganizationCveContracts
			.Where(c =>
				c.OrganizationId == organizationId &&
				c.ContractStartDate <= submittedAtUtc &&
				c.ContractEndDate >= submittedAtUtc)
			.OrderByDescending(c => c.ContractStartDate)
			.FirstOrDefaultAsync();
		var originalEvent = await onTrackDBContext.ConversionVerificationEvents
			.Where(e => e.TrackerClickId == trackerClick.Id)
			.OrderBy(e => e.SubmittedAtUtc)
			.FirstOrDefaultAsync();

		var cveEvent = new ConversionVerificationEvent {
			Id = Guid.NewGuid(),
			OrganizationId = organizationId,
			SiteId = site.Id,
			ContractId = contract?.Id,
			TrackerId = trackerClick.ParentTracker.Id,
			TrackingCampaignId = trackerClick.Campaign?.Id,
			TrackerClickId = trackerClick.Id,
			ExternalSubmissionId = trackerClick.Id.ToString(),
			ExternalConversionId = trackerClick.Id.ToString(),
			IdempotencyKey = $"javascript-postback:{trackerClick.Id}:{Guid.NewGuid():N}",
			SubmittedAtUtc = submittedAtUtc,
			OriginalEventTimestampUtc = trackerClick.ConversionDate ?? submittedAtUtc,
			Status = trackerClick.IsBotClick == true ? "Flagged" : "Verified",
			CountsTowardCve = true,
			CountedAtUtc = submittedAtUtc,
			RequestHash = ComputeRequestHash(request, trackerClick.Id),
			Source = "javascript_postback",
			CreatedAtUtc = submittedAtUtc,
			UpdatedAtUtc = submittedAtUtc,
		};

		if (originalEvent != null) {
			cveEvent.Status = "Duplicate";
			cveEvent.DuplicateOfEventId = originalEvent.Id;
		}
		else if (!UserController.CanTrackCves(onTrackDBContext, organizationId, submittedAtUtc)) {
			RejectCveEvent(cveEvent, "Organization subscription is not active for CVE tracking.");
		}
		else if (contract == null) {
		}
		else {
			var countedEvents = await onTrackDBContext.ConversionVerificationEvents.CountAsync(e =>
				e.ContractId == contract.Id &&
				e.CountsTowardCve);

			if (contract.CVEHardLimitEnabled && countedEvents >= contract.CommittedAnnualCVEs) {
				RejectCveEvent(cveEvent, "CVE hard limit reached for active contract.");
				if (contract.UpgradeRequiredTriggeredAt == null)
					contract.UpgradeRequiredTriggeredAt = submittedAtUtc;
			}
			else {
				var newCount = countedEvents + 1;
				if (contract.CommittedAnnualCVEs > 0) {
					var usageRatio = (decimal)newCount / contract.CommittedAnnualCVEs;
					if (contract.CVEWarning75SentAt == null && usageRatio >= 0.75m)
						contract.CVEWarning75SentAt = submittedAtUtc;
					if (contract.CVEWarning90SentAt == null && usageRatio >= 0.90m)
						contract.CVEWarning90SentAt = submittedAtUtc;
				}
			}
		}

		onTrackDBContext.ConversionVerificationEvents.Add(cveEvent);
		await onTrackDBContext.SaveChangesAsync();
	}

	private static async Task CreateUnmatchedCveEventAsync(
		OnTrackDBContext onTrackDBContext,
		HttpRequest request,
		UserTracker userTracker,
		string postbackUrl,
		string referer,
		DateTime submittedAtUtc)
	{
		var organization = userTracker.Organization;
		var organizationId = organization.Id;
		var site = await FindOrCreateSiteAsync(onTrackDBContext, organizationId, userTracker.Id, postbackUrl, referer, submittedAtUtc);
		var contract = await onTrackDBContext.OrganizationCveContracts
			.Where(c =>
				c.OrganizationId == organizationId &&
				c.ContractStartDate <= submittedAtUtc &&
				c.ContractEndDate >= submittedAtUtc)
			.OrderByDescending(c => c.ContractStartDate)
			.FirstOrDefaultAsync();

		var eventId = Guid.NewGuid();
		var cveEvent = new ConversionVerificationEvent {
			Id = eventId,
			OrganizationId = organizationId,
			SiteId = site.Id,
			ContractId = contract?.Id,
			TrackerId = userTracker.Id,
			TrackingCampaignId = null,
			TrackerClickId = null,
			ExternalSubmissionId = eventId.ToString(),
			ExternalConversionId = eventId.ToString(),
			IdempotencyKey = $"javascript-postback-unmatched:{userTracker.Id}:{Guid.NewGuid():N}",
			SubmittedAtUtc = submittedAtUtc,
			OriginalEventTimestampUtc = submittedAtUtc,
			Status = "Unmatched",
			CountsTowardCve = true,
			CountedAtUtc = submittedAtUtc,
			RequestHash = ComputeRequestHash(request, eventId),
			Source = "javascript_postback_unmatched",
			CreatedAtUtc = submittedAtUtc,
			UpdatedAtUtc = submittedAtUtc,
		};

		if (!UserController.CanTrackCves(onTrackDBContext, organizationId, submittedAtUtc)) {
			RejectCveEvent(cveEvent, "Organization subscription is not active for CVE tracking.");
		}
		else if (contract == null) {
		}
		else {
			var countedEvents = await onTrackDBContext.ConversionVerificationEvents.CountAsync(e =>
				e.ContractId == contract.Id &&
				e.CountsTowardCve);

			if (contract.CVEHardLimitEnabled && countedEvents >= contract.CommittedAnnualCVEs) {
				RejectCveEvent(cveEvent, "CVE hard limit reached for active contract.");
				if (contract.UpgradeRequiredTriggeredAt == null)
					contract.UpgradeRequiredTriggeredAt = submittedAtUtc;
			}
			else {
				var newCount = countedEvents + 1;
				if (contract.CommittedAnnualCVEs > 0) {
					var usageRatio = (decimal)newCount / contract.CommittedAnnualCVEs;
					if (contract.CVEWarning75SentAt == null && usageRatio >= 0.75m)
						contract.CVEWarning75SentAt = submittedAtUtc;
					if (contract.CVEWarning90SentAt == null && usageRatio >= 0.90m)
						contract.CVEWarning90SentAt = submittedAtUtc;
				}
			}
		}

		onTrackDBContext.ConversionVerificationEvents.Add(cveEvent);
		await onTrackDBContext.SaveChangesAsync();
	}

	private static void RejectCveEvent(ConversionVerificationEvent cveEvent, string rejectionReason)
	{
		cveEvent.Status = "Rejected";
		cveEvent.CountsTowardCve = false;
		cveEvent.CountedAtUtc = null;
		cveEvent.RejectionReason = rejectionReason;
	}

	private static async Task<OrganizationSite> FindOrCreateSiteAsync(
		OnTrackDBContext onTrackDBContext,
		TrackerClick trackerClick,
		DateTime submittedAtUtc)
	{
		var organizationId = trackerClick.ParentTracker!.Organization.Id;
		return await FindOrCreateSiteAsync(
			onTrackDBContext,
			organizationId,
			trackerClick.ParentTracker.Id,
			trackerClick.ClickUrl,
			trackerClick.Referer,
			submittedAtUtc);
	}

	private static async Task<OrganizationSite> FindOrCreateSiteAsync(
		OnTrackDBContext onTrackDBContext,
		Guid organizationId,
		Guid trackerId,
		string? url,
		string? referer,
		DateTime submittedAtUtc)
	{
		var host = ExtractHost(url) ?? ExtractHost(referer) ?? "unknown";
		var normalizedHost = NormalizeHost(host);

		var sites = await onTrackDBContext.OrganizationSites
			.Where(s => s.OrganizationId == organizationId)
			.ToListAsync();

		var matchingSite = sites.FirstOrDefault(site => DomainMatches(normalizedHost, NormalizeHost(site.Domain)));
		if (matchingSite != null)
			return matchingSite;

		var newSite = new OrganizationSite {
			Id = Guid.NewGuid(),
			OrganizationId = organizationId,
			SiteName = host,
			Domain = host,
			TrackingId = trackerId.ToString(),
			IsActive = true,
			CreatedAt = submittedAtUtc,
			UpdatedAt = submittedAtUtc,
		};

		onTrackDBContext.OrganizationSites.Add(newSite);
		await onTrackDBContext.SaveChangesAsync();
		return newSite;
	}

	private static IEnumerable<TrackerClickExtraProperty> BuildLocationProperties(TrackerClick click, HttpRequest request)
	{
		var country = request.Headers["CF-IPCountry"].FirstOrDefault()
			?? request.Headers["X-Country-Code"].FirstOrDefault()
			?? "Unknown";
		var region = request.Headers["X-Region"].FirstOrDefault() ?? "Unknown";
		var city = request.Headers["X-City"].FirstOrDefault() ?? "Unknown";

		return new[] {
			new TrackerClickExtraProperty {
				Id = Guid.NewGuid(),
				ClickParent = click,
				PropertyKey = "ip_country",
				PropertyValue = country,
			},
			new TrackerClickExtraProperty {
				Id = Guid.NewGuid(),
				ClickParent = click,
				PropertyKey = "ip_region",
				PropertyValue = region,
			},
			new TrackerClickExtraProperty {
				Id = Guid.NewGuid(),
				ClickParent = click,
				PropertyKey = "ip_city",
				PropertyValue = city,
			},
		};
	}

	private static Guid? ExtractCampaignId(string? clickUrl)
	{
		if (string.IsNullOrWhiteSpace(clickUrl) || !Uri.TryCreate(clickUrl, UriKind.Absolute, out var uri))
			return null;

		var query = QueryHelpers.ParseQuery(uri.Query);
		if (!query.TryGetValue("cid", out var campaignIdValue))
			return null;

		return Guid.TryParse(campaignIdValue.ToString(), out var campaignId) ? campaignId : null;
	}

	private static string DecodeBase64OrRaw(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		try {
			return Encoding.UTF8.GetString(Convert.FromBase64String(value));
		}
		catch (FormatException) {
			return value;
		}
	}

	private static string GetClientIp(HttpRequest request)
	{
		var forwardedFor = request.Headers["X-Forwarded-For"].FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(forwardedFor))
			return forwardedFor.Split(',')[0].Trim();

		return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
	}

	private static bool IsBotUserAgent(string? userAgent)
	{
		if (string.IsNullOrWhiteSpace(userAgent))
			return false;

		var normalized = userAgent.ToLowerInvariant();
		return normalized.Contains("bot") ||
			normalized.Contains("spider") ||
			normalized.Contains("crawl") ||
			normalized.Contains("slurp") ||
			normalized.Contains("headless");
	}

	private static bool IsDesktopUserAgent(string? userAgent)
	{
		if (string.IsNullOrWhiteSpace(userAgent))
			return true;

		var normalized = userAgent.ToLowerInvariant();
		return !(normalized.Contains("android") ||
			normalized.Contains("iphone") ||
			normalized.Contains("ipad") ||
			normalized.Contains("mobile"));
	}

	private static string? ExtractHost(string? url)
	{
		if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
			return null;

		return uri.Host;
	}

	private static string NormalizeHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host))
			return string.Empty;

		var normalized = host.Trim().Trim('.').ToLowerInvariant();
		return normalized.StartsWith("www.") ? normalized.Substring(4) : normalized;
	}

	private static bool DomainMatches(string host, string domain)
	{
		if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(domain))
			return false;

		return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
	}

	private static string ComputeRequestHash(HttpRequest request, Guid trackerClickId)
	{
		var payload = string.Join("|", new[] {
			trackerClickId.ToString(),
			GetClientIp(request),
			request.Headers.UserAgent.ToString(),
			request.Headers.Origin.ToString(),
		});
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
	}
}
