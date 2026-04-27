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
				.ThenInclude(t => t.Organization)
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

	private static async Task CreateCveEventAsync(
		OnTrackDBContext onTrackDBContext,
		HttpRequest request,
		TrackerClick trackerClick,
		DateTime submittedAtUtc)
	{
		var organizationId = trackerClick.ParentTracker!.Organization.Id;
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
			IdempotencyKey = $"javascript-postback:{trackerClick.Id}",
			SubmittedAtUtc = submittedAtUtc,
			OriginalEventTimestampUtc = trackerClick.ConversionDate ?? submittedAtUtc,
			Status = "received",
			CountsTowardCve = false,
			RequestHash = ComputeRequestHash(request, trackerClick.Id),
			Source = "javascript_postback",
			CreatedAtUtc = submittedAtUtc,
			UpdatedAtUtc = submittedAtUtc,
		};

		if (originalEvent != null) {
			cveEvent.Status = "duplicate";
			cveEvent.DuplicateOfEventId = originalEvent.Id;
			cveEvent.RejectionReason = "Duplicate conversion received for tracker click.";
		}
		else if (contract == null) {
			cveEvent.Status = "rejected";
			cveEvent.RejectionReason = "No active CVE contract matched this conversion.";
		}
		else {
			var countedEvents = await onTrackDBContext.ConversionVerificationEvents.CountAsync(e =>
				e.ContractId == contract.Id &&
				e.CountsTowardCve);

			if (contract.CVEHardLimitEnabled && countedEvents >= contract.CommittedAnnualCVEs) {
				cveEvent.Status = "rejected";
				cveEvent.RejectionReason = "CVE hard limit reached for active contract.";
				if (contract.UpgradeRequiredTriggeredAt == null)
					contract.UpgradeRequiredTriggeredAt = submittedAtUtc;
			}
			else {
				cveEvent.Status = "counted";
				cveEvent.CountsTowardCve = true;
				cveEvent.CountedAtUtc = submittedAtUtc;

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

	private static async Task<OrganizationSite> FindOrCreateSiteAsync(
		OnTrackDBContext onTrackDBContext,
		TrackerClick trackerClick,
		DateTime submittedAtUtc)
	{
		var organizationId = trackerClick.ParentTracker!.Organization.Id;
		var host = ExtractHost(trackerClick.ClickUrl) ?? ExtractHost(trackerClick.Referer) ?? "unknown";
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
			TrackingId = trackerClick.ParentTracker.Id.ToString(),
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


