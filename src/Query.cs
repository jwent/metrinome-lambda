using GraphQL;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Security.Claims;

public class Query
{
	private static float ParseCampaignConversionValue(string? conversionValue)
	{
		return float.TryParse(conversionValue, out var parsedValue) ? parsedValue : 0;
	}

	private static int CountDuplicateConversions(OnTrackDBContext onTrackDBContext, Guid campaignId)
	{
		try {
			return onTrackDBContext.ConversionVerificationEvents.Count(e =>
				e.TrackingCampaignId == campaignId &&
				e.Status == "Duplicate");
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01") {
			return 0;
		}
	}

	public static string DatabaseInfo([FromServices] OnTrackDBContext dbContext)
	{
		// Returns current database name from EF Core
		var conn = dbContext.Database.GetDbConnection();
		using var cmd = dbContext.Database.GetDbConnection().CreateCommand();
		cmd.CommandText = @"
			SELECT table_schema || '.' || table_name 
			FROM information_schema.tables
			WHERE table_schema = 'public'
			ORDER BY table_name;
		";

		dbContext.Database.OpenConnection();
		using var reader = cmd.ExecuteReader();
		var results = new List<string>();
		while (reader.Read())
		{
			results.Add(reader.GetString(0));
		}
		dbContext.Database.CloseConnection();

		//return string.Join("\n", results);
    	return $"Database: {conn.Database}, ConnectionString: {conn.ConnectionString}";	   
    }

	[Authorize(Policy = "CustomerPolicy")]
        public static OrganizationData getOrganization(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
                var org = UserController.GetCurrentOrganization(context, onTrackDBContext);
                var userdatalist = org.Users
                        .Select(user => new UserData {
				Email=user.Email,
				CreatedAt=user.CreatedAt,
				FullName=user.ExtraProperties.FirstOrDefault(prop => prop.PropertyKey == "FullName")?.PropertyValue,
				UserRoles=user.UserRoles.Select(r => r.RoleName).ToList(),
				UserState=user.UserState,
			}).ToList();
		return new OrganizationData {
			CreatedAt=org.CreatedAt,
			Users=userdatalist,
			SubscriptionPlan=null,
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static UserData getUserData(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Where(u => u.Id == userId)
			.Include(u => u.ExtraProperties)
			.Include(u => u.UserRoles)
			.First();

		return new UserData {
			Email=user.Email,
			CreatedAt=user.CreatedAt,
			FullName=user.ExtraProperties.FirstOrDefault(prop => prop.PropertyKey == "FullName")?.PropertyValue,
			UserRoles=user.UserRoles.Select(r => r.RoleName).ToList(),
			Admin = string.Equals(user.UserState, "Admin", StringComparison.OrdinalIgnoreCase),
            MagicLink = user.MagicLink
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static List<AdminCveData> adminCves(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		var userId = UserController.GetCurrentUserId(context);
		var currentUser = onTrackDBContext.Users
			.AsNoTracking()
			.Where(u => u.Id == userId)
			.Select(u => new { u.OrganizationId })
			.First();

		List<AdminCveData> cves;
		try {
			cves = (
				from cve in onTrackDBContext.ConversionVerificationEvents.AsNoTracking()
				where cve.OrganizationId == currentUser.OrganizationId
				join site in onTrackDBContext.OrganizationSites.AsNoTracking()
					on cve.SiteId equals site.Id into siteGroup
				from site in siteGroup.DefaultIfEmpty()
				join contract in onTrackDBContext.OrganizationCveContracts.AsNoTracking()
					on cve.ContractId equals contract.Id into contractGroup
				from contract in contractGroup.DefaultIfEmpty()
				join campaign in onTrackDBContext.TrackingCampaigns.AsNoTracking()
					on cve.TrackingCampaignId equals campaign.Id into campaignGroup
				from campaign in campaignGroup.DefaultIfEmpty()
				orderby cve.SubmittedAtUtc descending
				select new AdminCveData {
					Id = cve.Id,
					SubmittedAtUtc = cve.SubmittedAtUtc,
					OriginalEventTimestampUtc = cve.OriginalEventTimestampUtc,
					Status = cve.Status,
					CountsTowardCve = cve.CountsTowardCve,
					CountedAtUtc = cve.CountedAtUtc,
					RejectionReason = cve.RejectionReason,
					Source = cve.Source,
					ExternalSubmissionId = cve.ExternalSubmissionId,
					ExternalConversionId = cve.ExternalConversionId,
					SiteName = site != null ? site.SiteName : null,
					Domain = site != null ? site.Domain : null,
					TrackingId = site != null ? site.TrackingId : null,
					ContractTierName = contract != null ? contract.TierName : null,
					CampaignName = campaign != null ? campaign.CampaignName : null,
					TrackingCampaignId = cve.TrackingCampaignId,
					TrackerId = cve.TrackerId,
					TrackerClickId = cve.TrackerClickId,
					DuplicateOfEventId = cve.DuplicateOfEventId,
				}
			).ToList();
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01") {
			throw new ExecutionError(
				"CVE schema is missing in the active database. Run scripts/cve-schema-migration.sql against the database configured by ONTRACK_DATABASE_CONNECT_STRING."
			);
		}

		return cves;
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static List<AdminCveData> myCves(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		return adminCves(context, onTrackDBContext);
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static AccountSummaryResponse accountSummary(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		try {
			var organization = UserController.GetCurrentOrganization(context, onTrackDBContext);
			var now = DateTime.UtcNow;
			var organizationId = organization.Id;
			var ownerUser = organization.Users.FirstOrDefault(user => user.Id == organization.CreatorId)
				?? organization.Users.FirstOrDefault();
			var ownerName = ownerUser?.ExtraProperties.FirstOrDefault(prop => prop.PropertyKey == "FullName")?.PropertyValue;
			var organizationName = !string.IsNullOrWhiteSpace(ownerName)
				? $"{ownerName} Organization"
				: !string.IsNullOrWhiteSpace(ownerUser?.Email)
					? ownerUser.Email
					: organizationId.ToString();

			var users = organization.Users
				.Select(user => new UserData {
					Email = user.Email,
					CreatedAt = user.CreatedAt,
					FullName = user.ExtraProperties.FirstOrDefault(prop => prop.PropertyKey == "FullName")?.PropertyValue,
					UserRoles = user.UserRoles.Select(role => role.RoleName).ToList(),
					UserState = user.UserState,
				})
				.ToList();

			var campaignCount = onTrackDBContext.TrackingCampaigns.Count(c => c.ParentTracker.Organization.Id == organizationId);
			var countedCves = onTrackDBContext.ConversionVerificationEvents.Count(cve => cve.OrganizationId == organizationId && cve.CountsTowardCve);
			var totalCves = onTrackDBContext.ConversionVerificationEvents.Count(cve => cve.OrganizationId == organizationId);
			var verifiedCves = onTrackDBContext.ConversionVerificationEvents.Count(cve =>
				cve.OrganizationId == organizationId &&
				cve.Status == "Verified");

			var activeContract = onTrackDBContext.OrganizationCveContracts
				.Where(contract =>
					contract.OrganizationId == organizationId &&
					contract.ContractStartDate <= now &&
					contract.ContractEndDate >= now)
				.OrderByDescending(contract => contract.ContractStartDate)
				.FirstOrDefault();
			var pricing = CveContractPricingCatalog.Resolve(activeContract);
			var subscriptionStatus = UserController.GetSubscriptionStatus(activeContract, now);

			var currentPeriodCountedCves = activeContract == null
				? countedCves
				: onTrackDBContext.ConversionVerificationEvents.Count(cve =>
					cve.OrganizationId == organizationId &&
					cve.CountsTowardCve &&
					cve.SubmittedAtUtc >= activeContract.ContractStartDate &&
					cve.SubmittedAtUtc <= activeContract.ContractEndDate);
			var currentPeriodVerifiedCves = activeContract == null
				? verifiedCves
				: onTrackDBContext.ConversionVerificationEvents.Count(cve =>
					cve.OrganizationId == organizationId &&
					cve.Status == "Verified" &&
					cve.SubmittedAtUtc >= activeContract.ContractStartDate &&
					cve.SubmittedAtUtc <= activeContract.ContractEndDate);
			var currentPeriodProcessedCves = activeContract == null
				? countedCves
				: onTrackDBContext.ConversionVerificationEvents.Count(cve =>
					cve.OrganizationId == organizationId &&
					cve.CountsTowardCve &&
					cve.SubmittedAtUtc >= activeContract.ContractStartDate &&
					cve.SubmittedAtUtc <= activeContract.ContractEndDate);

			long annualContractValueCents = pricing?.AnnualContractValueCents ?? 0;
			long usageValueCents = pricing == null ? 0 : currentPeriodProcessedCves * pricing.RatePerCveCents;
			long usageCostCents = usageValueCents;
			string currency = pricing?.Currency ?? "usd";
			string? costCalculation;
			if (activeContract != null && pricing != null) {
				costCalculation = $"Usage value is based on all processed CVEs in the active contract window ({currentPeriodProcessedCves}/{pricing.CommittedAnnualCves}) at {pricing.RatePerCveCents} cents per CVE. Annual contract value is the greater of the annual minimum fee or committed CVEs multiplied by the contract rate.";
			}
			else if (activeContract != null) {
				costCalculation = "An active CVE contract exists, but pricing could not be derived from its tier or committed annual CVE capacity.";
			}
			else {
				costCalculation = "No active CVE capacity contract is configured for this organization.";
			}

			return new AccountSummaryResponse {
				Success = true,
				OrganizationId = organizationId,
				OrganizationName = organizationName,
				OrganizationCreatedAt = organization.CreatedAt,
				Users = users,
				PlanName = pricing?.TierName ?? activeContract?.TierName,
				SubscriptionPlan = null,
				SubscriptionStatus = subscriptionStatus,
				UserCount = users.Count,
				CampaignCount = campaignCount,
				TotalCves = totalCves,
				CountedCves = countedCves,
				VerifiedCves = verifiedCves,
				CurrentPeriodCountedCves = currentPeriodCountedCves,
				CurrentPeriodVerifiedCves = currentPeriodVerifiedCves,
				CurrentPeriodProcessedCves = currentPeriodProcessedCves,
				CurrentPeriodCveLimit = activeContract?.CommittedAnnualCVEs,
				ContractTierName = pricing?.TierName ?? activeContract?.TierName,
				ContractStartDate = activeContract?.ContractStartDate,
				CommittedAnnualCves = activeContract?.CommittedAnnualCVEs,
				RemainingCommittedCves = activeContract == null
					? null
					: Math.Max(activeContract.CommittedAnnualCVEs - currentPeriodProcessedCves, 0),
				RatePerCveCents = pricing?.RatePerCveCents ?? 0,
				AnnualMinimumFeeCents = pricing?.AnnualMinimumFeeCents ?? 0,
				AnnualContractValueCents = annualContractValueCents,
				CurrentPlanCostCents = annualContractValueCents,
				UsageCostCents = usageCostCents,
				UsageValueCents = usageValueCents,
				Currency = currency,
				CostCalculation = costCalculation,
			};
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01") {
			return new AccountSummaryResponse {
				Success = false,
				Error = "CVE schema is missing in the active database. Run scripts/cve-schema-migration.sql against the database configured by ONTRACK_DATABASE_CONNECT_STRING."
			};
		}
		catch (Exception ex) {
			Console.WriteLine($"[!] accountSummary error: {ex.Message}");
			return new AccountSummaryResponse {
				Success = false,
				Error = ex.Message
			};
		}
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static string? trackerCode(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		var userId = UserController.GetCurrentUserId(context);

		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		var endpoint = (Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL") ?? string.Empty).TrimEnd('/');
		var clickEndpoint = $"{endpoint}/click";
		return 
			Util.CompressJavascriptStub(@"<script src=""" + Environment.GetEnvironmentVariable("ONTRACK_SITE_URL") + @"cdn/psl.min.js""></script>") + "\n"
			+ Util.CompressJavascriptStub(@"<script type=""text/javascript"">
	(function(){
		var urlParams = new URLSearchParams(window.location.search);
		var cid = urlParams.get('cid');
		if(cid){
			var rpu = window.btoa(window.location.href);
			var rpr = window.btoa(document.referrer);
			(function(){
				fetch('" + clickEndpoint + "?t=" + userTracker.Id.ToString() + @"&r='+rpr+'&u='+rpu)
				.then(function(r) { return r.json(); })
				.then(function(d) { 
					var cookieName = 'ontrack-clid';
					var cookieValue = d.clid;
					var myDate = new Date();
					var parsed = psl.parse(window.location.hostname);
					myDate.setDate(myDate.getDate() + 7);
					document.cookie = cookieName +'=' + cookieValue + ';expires=' + myDate + ';domain=.' + parsed.domain + ';path=/';
				});
			})();
		}
	})();
</script>");
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static PostbackCodes postbackCode(IResolveFieldContext context) {
		var endpoint = (Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL") ?? string.Empty).TrimEnd('/');
		var postbackEndpoint = $"{endpoint}/postback";
		return new PostbackCodes
		{
			PagePostback = Util.CompressJavascriptStub(@"<script type=""text/javascript"">
				(function () {
					var match = document.cookie.match('(^|;)\\s*ontrack-clid\\s*=\\s*([^;]+)');
					var clid = match ? match.pop() : '';
					if (!clid) {
						console.warn('Metrinome postback: no valid clid found');
						return;
					}
					fetch('" + postbackEndpoint + @"?clid=' + encodeURIComponent(clid), { mode: 'no-cors' });
					console.log('Metrinome postback fired:', clid);
				}
				)();
				</script>"),
			ButtonPostback = Util.CompressJavascriptStub(@"<script type=""text/javascript"">
				(function(){
					document.getElementById('{id}').addEventListener('click',
					function(){
						var cookies = document.cookie.match('(^|;)\\s*ontrack-clid\\s*=\\s*([0-9a-zA-Z\\-]+)');
						var clid = cookies ? cookies.pop() : '';
				
						if(clid){
							fetch('" + postbackEndpoint + @"?clid='+clid,{mode:'no-cors'})
						}
					});
				})()</script>"),
		};
	}

	// [Authorize(Policy = "CustomerPolicy")]
	// public static TrackingCampaign getCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId) {
	// 	var userId = UserController.GetCurrentUserId(context);
	// 	var organizationId = UserController.GetCurrentOrganizationId(context, onTrackDBContext);

	// 	var campaignGuid = Guid.Parse(campaignId);
	// 	Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
	// 	var existingCampaign = onTrackDBContext.TrackingCampaigns.FirstOrDefault(e => e.Id == campaignGuid && e.ParentTracker.Organization.Id == organizationId, null);
	// 	if (existingCampaign == null)
	// 		throw new Exception("campaign not found!");

	// 	return existingCampaign;
	// }

	[Authorize(Policy = "CustomerPolicy")]
	public static Campaigns myCampaigns(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, DateTime? createdAt, int length) {
		var userId = UserController.GetCurrentUserId(context);
		var organizationId = UserController.GetCurrentOrganizationId(context, onTrackDBContext);

		IOrderedQueryable<TrackingCampaign> campaigns = (IOrderedQueryable<TrackingCampaign>)onTrackDBContext.TrackingCampaigns.Where(e => e.ParentTracker.Organization.Id == organizationId);
		int count = campaigns.Count();
		if (createdAt.HasValue)
			campaigns = campaigns.Where(e => e.CreatedAt > createdAt).OrderBy(c => c.CreatedAt);
		else
			campaigns = campaigns.OrderBy(c => c.CreatedAt);
			
		if (length > 0)
			campaigns = (IOrderedQueryable<TrackingCampaign>)campaigns.Take(length);
		
		var campaignList=campaigns.Join(onTrackDBContext.TrackingCampaignExtraProperties,
					campaign => new { campaign.Id, PropertyKey = "CampaignType" },
					extra => new { extra.Parent.Id, extra.PropertyKey },
					(campaign, extraCampaignType) => new { campaign, extraCampaignType }
				).ToList();

		var campaign_datas = campaignList.Select(e => new TrackingCampaignData(e.campaign,
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id).Count(),
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id && c.IsBotClick == true).Count(),
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id && c.Conversion == true).Count(),
				CountDuplicateConversions(onTrackDBContext, e.campaign.Id),
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id && c.Conversion == true && c.IsDesktop == true).Count(),
				e.extraCampaignType.PropertyValue))
		.ToList();
		return new Campaigns(campaign_datas, count);
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static Clicks myCampaignClicks(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId, DateTime? createdAt) {
		var userId = UserController.GetCurrentUserId(context);
		var organizationId = UserController.GetCurrentOrganizationId(context, onTrackDBContext);

		var campaignGuid = Guid.Parse(campaignId);
		Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Organization.Id == organizationId);
		if (existingCampaign == null)
			throw new Exception("campaign not found!");

		var myClicks = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign != null && e.Campaign.Id == campaignGuid)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					click => new { click.Id, PropertyKey = "ip_country" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(click, extraCountry) => new { click, extraCountry }
				)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					combined => new { combined.click.Id, PropertyKey = "ip_region" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(combined, extraRegion) => new { combined.click, combined.extraCountry, extraRegion }
				)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					combined => new { combined.click.Id, PropertyKey = "ip_city" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(combined, extraCity) => new { combined.click, combined.extraCountry, combined.extraRegion, extraCity }
				)
				.OrderByDescending(combined => combined.click.CreatedAt);
		int count = myClicks.Count();

		List<TrackerClickData> clicksList;
		if (createdAt.HasValue)
		{
			clicksList = myClicks.Where(e => e.click.CreatedAt < createdAt)
					.Take(10)
					.Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity))
					.ToList();
		}
		else
		{
			clicksList = myClicks
					.Take(10)
					.Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity))
					.ToList();
		}

		return new Clicks(clicksList, count);
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static TrackingCampaignDetails myCampaignDetails(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId) {
		var userId = UserController.GetCurrentUserId(context);
		var organizationId = UserController.GetCurrentOrganizationId(context, onTrackDBContext);

		if (!Guid.TryParse(campaignId, out var campaignGuid))
			return EmptyCampaignDetails();

		Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns
				.FirstOrDefault(e => e.Id == campaignGuid && e.ParentTracker.Organization.Id == organizationId);

		if (existingCampaign == null)
			return EmptyCampaignDetails();

		var campaignType = onTrackDBContext.TrackingCampaignExtraProperties
				.Where(extra => extra.Parent.Id == existingCampaign.Id && extra.PropertyKey == "CampaignType")
				.Select(extra => extra.PropertyValue)
				.FirstOrDefault() ?? "Unknown";

		var campaignData = new TrackingCampaignData(existingCampaign,
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == existingCampaign.Id).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == existingCampaign.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == existingCampaign.Id && c.IsBotClick == true).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == existingCampaign.Id && c.Conversion == true).Count(),
					CountDuplicateConversions(onTrackDBContext, existingCampaign.Id),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == existingCampaign.Id && c.Conversion == true && c.IsDesktop == true).Count(),
					campaignType);

		var myClicks = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign != null && e.Campaign.Id == campaignGuid)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					click => new { click.Id, PropertyKey = "ip_country" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(click, extraCountry) => new { click, extraCountry }
				)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					combined => new { combined.click.Id, PropertyKey = "ip_region" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(combined, extraRegion) => new { combined.click, combined.extraCountry, extraRegion }
				)
				.Join(onTrackDBContext.TrackerClickExtraProperties,
					combined => new { combined.click.Id, PropertyKey = "ip_city" },
					extra => new { extra.ClickParent.Id, extra.PropertyKey },
					(combined, extraCity) => new { combined.click, combined.extraCountry, combined.extraRegion, extraCity }
				)
				.OrderByDescending(combined => combined.click.CreatedAt);
		int count = myClicks.Count();

		List<TrackerClickData> clicksList = myClicks
				.Take(10)
				.Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity))
				.ToList();

		// Keep this projection as IQueryable so PostgreSQL performs the grouping and
		// aggregation. Only the top ten aggregate rows are materialized in Lambda.
		var myClicksData = myClicks.Select(combined => new
		{
			City = combined.extraCity.PropertyValue,
			Region = combined.extraRegion.PropertyValue,
			Conversion = combined.click.Conversion,
		});

		var topLocations = myClicksData
			.GroupBy(click => new { click.City, click.Region })
			.Select(group => new Location
			{
				City = group.Key.City,
				Region = group.Key.Region,
				ClickCount = group.Count(),
				ConversionCount = group.Count(click => click.Conversion == true),
			})
			.OrderByDescending(location => location.ClickCount)
			.Take(10)
			.ToList();
	
		return new TrackingCampaignDetails(campaignData, new Clicks(clicksList, count), new ChartDatas(topLocations));
	}

	private static TrackingCampaignDetails EmptyCampaignDetails() {
		var emptyCampaign = new TrackingCampaign
		{
			Id = Guid.Empty,
			CreatedAt = DateTime.UtcNow,
		};
		return new TrackingCampaignDetails(
			new TrackingCampaignData(emptyCampaign, 0, 0, 0, 0, 0, 0, "Unknown"),
			new Clicks(new List<TrackerClickData>(), 0),
			new ChartDatas(new List<Location>()));
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static GetCampaignClickStatsResponse myCampaignClickStats(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId, string? groupby = "day") {
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);
		if (!Guid.TryParse(campaignId, out var parsedCampaignId))
			return new GetCampaignClickStatsResponse { GroupedBy = groupby, Stats = new List<CampaignClickStatPoint>() };

		var campaign = onTrackDBContext.TrackingCampaigns
			.FirstOrDefault(e => e.Id == parsedCampaignId && e.ParentTracker.Id == userTracker.Id);
		if (campaign == null)
			return new GetCampaignClickStatsResponse { GroupedBy = groupby, Stats = new List<CampaignClickStatPoint>() };

		// get the clicks by campaign
		var clicksQuery = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign != null && e.Campaign.Id == campaign.Id);

		List<CampaignClickStatPoint> stats;
		if (groupby == "day") {
			stats = clicksQuery
				.GroupBy(f => new { Date = f.CreatedAt.Date, Hour = 0 })
				.Select(g => new { g.Key.Date, g.Key.Hour, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignClickStatPoint { Position = s.Date.ToString("yyyy-MM-dd"), ClickCount = s.Count })
				.ToList();
		} else if (groupby == "hour") {
			stats = clicksQuery
				.Where(t => t.CreatedAt > DateTime.Now.AddHours(-24))
				.GroupBy(f => new { Date = f.CreatedAt.Date, Hour = f.CreatedAt.Hour })
				.Select(g => new { g.Key.Date, g.Key.Hour, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignClickStatPoint { Position = s.Date.AddHours(s.Hour).ToString("yyyy-MM-ddTHH:00:00"), ClickCount = s.Count })
				.ToList();
		} else if (groupby == "month") {
			stats = clicksQuery
				.GroupBy(f => new { f.CreatedAt.Year, f.CreatedAt.Month })
				.Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignClickStatPoint { Position = new DateTime(s.Year, s.Month, 1).ToString("yyyy-MM-dd"), ClickCount = s.Count })
				.ToList();
		} else {
			throw new Exception("invalid groupby");
		}

		// return formatted object
		return new GetCampaignClickStatsResponse
		{
			GroupedBy = groupby,
			Stats = stats,
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static GetCampaignConversionStatsResponse myCampaignConversionStats(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId, string? groupby = "day") {
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);
		if (!Guid.TryParse(campaignId, out var parsedCampaignId))
			return new GetCampaignConversionStatsResponse { GroupedBy = groupby, Stats = new List<CampaignConversionStatPoint>() };

		var campaign = onTrackDBContext.TrackingCampaigns
			.FirstOrDefault(e => e.Id == parsedCampaignId && e.ParentTracker.Id == userTracker.Id);
		if (campaign == null)
			return new GetCampaignConversionStatsResponse { GroupedBy = groupby, Stats = new List<CampaignConversionStatPoint>() };

		// get the clicks by campaign
		var conversionsQuery = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign != null && e.Campaign.Id == campaign.Id && e.Conversion != null && e.ConversionDate.HasValue);

		List<CampaignConversionStatPoint> stats;
		if (groupby == "day") {
			stats = conversionsQuery
				.GroupBy(f => new { Date = f.ConversionDate!.Value.Date, Hour = 0 })
				.Select(g => new { g.Key.Date, g.Key.Hour, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignConversionStatPoint { Position = s.Date.ToString("yyyy-MM-dd"), ConversionCount = s.Count })
				.ToList();
		} else if (groupby == "hour") {
			stats = conversionsQuery
				.Where(t => t.ConversionDate!.Value > DateTime.Now.AddHours(-24))
				.GroupBy(f => new { Date = f.ConversionDate!.Value.Date, Hour = f.ConversionDate!.Value.Hour })
				.Select(g => new { g.Key.Date, g.Key.Hour, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignConversionStatPoint { Position = s.Date.AddHours(s.Hour).ToString("yyyy-MM-ddTHH:00:00"), ConversionCount = s.Count })
				.ToList();
		} else if (groupby == "month") {
			stats = conversionsQuery
				.GroupBy(f => new { f.ConversionDate!.Value.Year, f.ConversionDate!.Value.Month })
				.Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
				.ToList()
				.Select(s => new CampaignConversionStatPoint { Position = new DateTime(s.Year, s.Month, 1).ToString("yyyy-MM-dd"), ConversionCount = s.Count })
				.ToList();
		} else {
			throw new Exception("invalid groupby");
		}

		// return formatted object
		return new GetCampaignConversionStatsResponse
		{
			GroupedBy = groupby,
			Stats = stats,
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static TrackerInsightsResponse trackerClickInsights(
			IResolveFieldContext context,
			[FromServices] OnTrackDBContext onTrackDBContext,
			string propertytype,
			string groupby) {

		// get user properties for reference
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		// select where we want to get stuff from
		var query =
				propertytype == "click" ? onTrackDBContext.TrackerClicks.Where(c => c.ParentTracker != null && c.ParentTracker.Id == userTracker.Id) :
				propertytype == "conversion" ? onTrackDBContext.TrackerClicks.Where(c => c.ParentTracker != null && c.ParentTracker.Id == userTracker.Id && c.Conversion != null) :
				throw new Exception("invalid propertytype argument:" + propertytype);

		// group our stuff by the group value
		var groupedQuery =
				groupby == "country" ? query
					.Join(onTrackDBContext.TrackerClickExtraProperties,
						click => new { click.Id, PropertyKey = "ip_country" },
						extra => new { extra.ClickParent.Id, extra.PropertyKey },
						(click, extraCountry) => new { click, extraCountry }
					).GroupBy(c => c.extraCountry.PropertyValue)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "region" ? query
					.Join(onTrackDBContext.TrackerClickExtraProperties,
						click => new { click.Id, PropertyKey = "ip_region" },
						extra => new { extra.ClickParent.Id, extra.PropertyKey },
						(click, extraRegion) => new { click, extraRegion }
					).GroupBy(c => c.extraRegion.PropertyValue)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "city" ? query
					.Join(onTrackDBContext.TrackerClickExtraProperties,
						click => new { click.Id, PropertyKey = "ip_city" },
						extra => new { extra.ClickParent.Id, extra.PropertyKey },
						(click, extraCity) => new { click, extraCity }
					).GroupBy(c => c.extraCity.PropertyValue)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "referer" ? query
					.GroupBy(c => c.Referer)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "device" ? query
					.GroupBy(c => c.Useragent)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "ad_type" ? query
					.Join(onTrackDBContext.TrackingCampaignExtraProperties,
						click => new { click.Campaign!.Id, PropertyKey = "CampaignType" },
						extra => new { extra.Parent.Id, extra.PropertyKey },
						(click, extraCampaignType) => new { click, extraCampaignType }
					).GroupBy(c => c.extraCampaignType.PropertyValue)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "platform" ? query
					.GroupBy(c => c.Campaign!.Platform)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				// since clicks and conversions have different date properties, we have to get more specific
				groupby == "day_of_the_week" && propertytype == "click" ? query
					.GroupBy(c => c.CreatedAt.DayOfWeek)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "day_of_the_week" && propertytype == "conversion" ? query
					.GroupBy(c => c.ConversionDate!.Value.DayOfWeek)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "hour_of_the_day" && propertytype == "click" ? query
					.GroupBy(c => c.CreatedAt.Hour)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "hour_of_the_day" && propertytype == "conversion" ? query
					.GroupBy(c => c.ConversionDate!.Value.Hour)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				throw new Exception("invalid groupby argument:" + groupby);

		// sort, take, and execute the query
		var results = groupedQuery
			.OrderByDescending(s => s.Count)
			.Take(50)
			.ToList();

		// return the values with some context
		return new TrackerInsightsResponse {
			PropertyType=propertytype,
			GroupedBy=groupby,
			Stats=results,
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static TrackerInsightsResponse trackerCampaignInsights(
			IResolveFieldContext context,
			[FromServices] OnTrackDBContext onTrackDBContext,
			string propertytype,
			string groupby) {

		// get user properties for reference
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		// select where we want to get stuff from
		var query =
				propertytype == "roi" ? onTrackDBContext.TrackerClicks
					.Where(c => c.ParentTracker != null && c.ParentTracker.Id == userTracker.Id && c.Campaign != null && c.Conversion != null)
					.GroupBy(c => c.Campaign!.Id)
					.Select(g => new { Campaign=onTrackDBContext.TrackingCampaigns.First(t => t.Id == g.Key), Count=g.Count() })
					.ToList() :
				throw new Exception("invalid propertytype argument:" + propertytype);

		// group our stuff by the group value
		var results =
				groupby == "roi" ? query
					.Select(g => new StatPoint { Position=g.Campaign.CampaignName, Count=(int)(g.Count * ParseCampaignConversionValue(g.Campaign.ConversionValue)) } )
					.ToList() :
				groupby == "ad_type" ? query
					.GroupBy(c => onTrackDBContext.TrackingCampaignExtraProperties.First(p => p.Parent == c.Campaign && p.PropertyKey == "CampaignType").PropertyValue)
					.Select(g2 => new StatPoint { Position=g2.Key, Count=g2.ToList().Sum(g => (int)(g.Count * ParseCampaignConversionValue(g.Campaign.ConversionValue))) } )
					.ToList() :
				throw new Exception("invalid groupby argument:" + groupby);

		// return the values with some context
		return new TrackerInsightsResponse {
			PropertyType=propertytype,
			GroupedBy=groupby,
			Stats=results,
		};
	}

    [Authorize(Policy = "CustomerPolicy")]
    public static Task<CurrentSubscriptionResponse> getCurrentSubscription(
        IResolveFieldContext context,
        [FromServices] OnTrackDBContext onTrackDBContext)
    {
        try
        {
            var userId = UserController.GetCurrentUserId(context);

            var user = onTrackDBContext.Users
                .Include(u => u.Organization)
                .Include(u => u.ExtraProperties)
                .FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return Task.FromResult(new CurrentSubscriptionResponse
                {
                    Success = false,
                    Error = "User not found."
                });
            }

            var now = DateTime.UtcNow;
            var activeContract = UserController.GetActiveCveContract(onTrackDBContext, user.Organization.Id, now);
            var pricing = CveContractPricingCatalog.Resolve(activeContract);
            var fullName = user.ExtraProperties.FirstOrDefault(p => p.PropertyKey == "FullName")?.PropertyValue;

            return Task.FromResult(new CurrentSubscriptionResponse
            {
                Success = true,
                PlanKey = activeContract?.TierName?.ToLowerInvariant(),
                PlanName = pricing?.TierName ?? activeContract?.TierName,
                Status = UserController.GetSubscriptionStatus(activeContract, now),
                CustomerName = !string.IsNullOrWhiteSpace(fullName) ? fullName : user.Email
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] getCurrentSubscription error: {ex.Message}");
			return Task.FromResult(new CurrentSubscriptionResponse
			{
				Success = false,
				PlanKey = null,
				Status = "error",
				CustomerName = null,
				Error = ex.Message
			});
        }
    }
}
