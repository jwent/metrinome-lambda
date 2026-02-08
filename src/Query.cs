using GraphQL;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Diagnostics;
using System.Security.Claims;

public class Query
{
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
			SubscriptionPlan=org.SubscriptionPlan,
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static UserData getUserData(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		Debugger.Break();
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
	public static string? trackerCode(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		var userId = UserController.GetCurrentUserId(context);

		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
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
				fetch('" + endpoint + "?t=" + userTracker.Id.ToString() + @"&r='+rpr+'&u='+rpu)
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
		var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
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
					fetch('https://ping.ontrackanalytics.com/postback?clid=' + encodeURIComponent(clid), { mode: 'no-cors' });
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
							fetch('" + endpoint + @"postback?clid='+clid,{mode:'no-cors'})
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
				onTrackDBContext.TrackerClicks.Where(c => c.Campaign != null && c.Campaign.Id == e.campaign.Id && c.Conversion == true && c.IsDesktop == true).Count(),
				e.extraCampaignType))
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
				.Where(e => e.Campaign.Id == campaignGuid)
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

		var campaignGuid = Guid.Parse(campaignId);
		Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns.Join(onTrackDBContext.TrackingCampaignExtraProperties,
					campaign => new { campaign.Id, PropertyKey = "CampaignType" },
					extra => new { extra.Parent.Id, extra.PropertyKey },
					(campaign, extraCampaignType) => new { campaign, extraCampaignType }
				).First(e => e.campaign.Id == campaignGuid && e.campaign.ParentTracker.Organization.Id == organizationId);

		if (existingCampaign == null)
			throw new Exception("campaign not found!");

		var campaignData = new TrackingCampaignData(existingCampaign.campaign,
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.campaign.Id).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.campaign.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.campaign.Id && c.IsBotClick == true).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.campaign.Id && c.Conversion == true).Count(),
					onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.campaign.Id && c.Conversion == true && c.IsDesktop == true).Count(),
					existingCampaign.extraCampaignType);

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

		var myClicksData= myClicks
		.Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity)).ToList();

		var topLocationsByClicks = myClicksData
		.GroupBy(p => new { p.City, p.Region }).Select(m => new Location { City = m.Key.City, Region = m.Key.Region, ClickCount = m.Count() }).OrderByDescending(s => s.ClickCount).Take(10).ToList();
		var conversionCountByCity = myClicksData.Where(t => t.Conversion == true).GroupBy(p => new { p.City, p.Region }).Select(m => new Location { City = m.Key.City, Region = m.Key.Region, ConversionCount = m.Count() }).OrderByDescending(s => s.ConversionCount).ToList();
		var topLocations = topLocationsByClicks.GroupJoin(conversionCountByCity, click => click.City, conversion => conversion.City,
	(click, locations) => new Location { City = click.City, Region = click.Region, ClickCount = click.ClickCount, ConversionCount = locations.FirstOrDefault() != null ? locations.FirstOrDefault().ConversionCount : 0 }).ToList();
	
		return new TrackingCampaignDetails(campaignData, new Clicks(clicksList, count), new ChartDatas(topLocations));
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static GetCampaignClickStatsResponse myCampaignClickStats(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId, string? groupby = "day") {
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);
		var campaign = TrackerController.GetCampaignById(onTrackDBContext, userTracker.Id, Guid.Parse(campaignId));

		// get the clicks by campaign
		var clicksQuery = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign.Id == campaign.Id);
		// groupby sub-query based on which group we need
		var subQuery =
				groupby == "day" ? clicksQuery.GroupBy(f => new { Date = f.CreatedAt.Date, Hour = 0 }) :
				groupby == "hour" ? clicksQuery.Where(t => t.CreatedAt > DateTime.Now.AddHours(-24)).GroupBy(f => new { Date = f.CreatedAt.Date, Hour = f.CreatedAt.Hour }) :
				throw new Exception("invalid groupby");

		// select the results
		var stats = subQuery
				.Select(g => new { datetime = groupby == "day" ? g.Key.Date.ToString() : g.Key.Date.AddHours(g.Key.Hour).ToString(), count = g.Count() })
				.ToList();
		// return formatted object
		return new GetCampaignClickStatsResponse
		{
			GroupedBy = groupby,
			Stats = stats.Select(s => new CampaignClickStatPoint { Position = s.datetime, ClickCount = s.count }).ToList(),
		};
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static GetCampaignConversionStatsResponse myCampaignConversionStats(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId, string? groupby = "day") {
		var userId = UserController.GetCurrentUserId(context);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);
		var campaign = TrackerController.GetCampaignById(onTrackDBContext, userTracker.Id, Guid.Parse(campaignId));

		// get the clicks by campaign
		var conversionsQuery = onTrackDBContext.TrackerClicks
				.Where(e => e.Campaign.Id == campaign.Id && e.Conversion != null);

		// groupby sub-query based on which group we need
		var subQuery =
				groupby == "day" ? conversionsQuery.GroupBy(f => new { Date = f.ConversionDate.Value.Date, Hour = 0 }) :
				groupby == "hour" ? conversionsQuery.Where(t => t.ConversionDate.Value > DateTime.Now.AddHours(-24)).GroupBy(f => new { Date = f.ConversionDate.Value.Date, Hour = f.ConversionDate.Value.Hour }) :
				throw new Exception("invalid groupby");

		// select the results
		var stats = subQuery
				.Select(g => new { datetime = groupby == "day" ? g.Key.Date.ToString() : g.Key.Date.AddHours(g.Key.Hour).ToString(), count = g.Count() })
				.ToList();
		// return formatted object
		return new GetCampaignConversionStatsResponse
		{
			GroupedBy = groupby,
			Stats = stats.Select(s => new CampaignConversionStatPoint { Position = s.datetime, ConversionCount = s.count }).ToList(),
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
		var organization = UserController.GetCurrentOrganization(context, onTrackDBContext);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		// check if the organization subscription plan allows for insight analytics
                if (organization.SubscriptionPlan == null || !organization.SubscriptionPlan.CanUseInsightAnalytics) {
                        Console.WriteLine($"[+] organization does not have insight analytics in their subscription plan!");
                        throw new Exception("unauthorized");
                }

		// select where we want to get stuff from
		var query =
				propertytype == "click" ? onTrackDBContext.TrackerClicks.Where(c => c.ParentTracker.Id == userTracker.Id) :
				propertytype == "conversion" ? onTrackDBContext.TrackerClicks.Where(c => c.ParentTracker.Id == userTracker.Id && c.Conversion != null) :
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
						click => new { click.Campaign.Id, PropertyKey = "CampaignType" },
						extra => new { extra.Parent.Id, extra.PropertyKey },
						(click, extraCampaignType) => new { click, extraCampaignType }
					).GroupBy(c => c.extraCampaignType.PropertyValue)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				groupby == "platform" ? query
					.GroupBy(c => c.Campaign.Platform)
					.Select(g => new StatPoint { Position = g.Key, Count = g.Count() }) :
				// since clicks and conversions have different date properties, we have to get more specific
				groupby == "day_of_the_week" && propertytype == "click" ? query
					.GroupBy(c => c.CreatedAt.DayOfWeek)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "day_of_the_week" && propertytype == "conversion" ? query
					.GroupBy(c => ((DateTime)c.ConversionDate).DayOfWeek)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "hour_of_the_day" && propertytype == "click" ? query
					.GroupBy(c => c.CreatedAt.Hour)
					.Select(g => new StatPoint { Position = g.Key.ToString(), Count = g.Count() }) :
				groupby == "hour_of_the_day" && propertytype == "conversion" ? query
					.GroupBy(c => ((DateTime)c.ConversionDate).Hour)
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
		var organization = UserController.GetCurrentOrganization(context, onTrackDBContext);
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		// check if the organization subscription plan allows for insight analytics
                if (organization.SubscriptionPlan == null || !organization.SubscriptionPlan.CanUseInsightAnalytics) {
                        Console.WriteLine($"[+] organization does not have insight analytics in their subscription plan!");
                        throw new Exception("unauthorized");
                }

		// select where we want to get stuff from
		var query =
				propertytype == "roi" ? onTrackDBContext.TrackerClicks
					.Where(c => c.ParentTracker.Id == userTracker.Id && c.Conversion != null)
					.GroupBy(c => c.Campaign.Id)
					.Select(g => new { Campaign=onTrackDBContext.TrackingCampaigns.First(t => t.Id == g.Key), Count=g.Count() })
					.ToList() :
				throw new Exception("invalid propertytype argument:" + propertytype);

		// group our stuff by the group value
		var results =
				groupby == "roi" ? query
					.Select(g => new StatPoint { Position=g.Campaign.CampaignName, Count=(int)(g.Count * float.Parse(g.Campaign.ConversionValue)) } )
					.ToList() :
				groupby == "ad_type" ? query
					.GroupBy(c => onTrackDBContext.TrackingCampaignExtraProperties.First(p => p.Parent == c.Campaign && p.PropertyKey == "CampaignType").PropertyValue)
					.Select(g2 => new StatPoint { Position=g2.Key, Count=g2.ToList().Sum(g => (int)(g.Count * float.Parse(g.Campaign.ConversionValue))) } )
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
    public static async Task<CurrentSubscriptionResponse> getCurrentSubscription(
        IResolveFieldContext context,
        [FromServices] OnTrackDBContext onTrackDBContext,
        [FromServices] SubscriptionService subscriptionService,
        [FromServices] CustomerService customerService)
    {
        try
        {
            var userId = UserController.GetCurrentUserId(context);

            var user = onTrackDBContext.Users
                .Include(u => u.Organization)
                .FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return new CurrentSubscriptionResponse
                {
                    Success = false,
                    Error = "User not found."
                };
            }

            var userEmail = user.Email;
            
			if (string.IsNullOrEmpty(userEmail))
                return new CurrentSubscriptionResponse { Status = "no email" };

            // If no customer exists yet, user cannot have any subscriptions
            if (string.IsNullOrWhiteSpace(user.StripeCustomerId))
            {
                return new CurrentSubscriptionResponse
                {
                    Success = true,
                    PlanKey = null,
                    Status = "none"
                };
            }

            // 🔹 Retrieve active subscription
            var subs = await subscriptionService.ListAsync(new SubscriptionListOptions
            {
                Customer = user.StripeCustomerId,
                Limit = 1,
                Status = "all"
            });

            var subscription = subs.Data.FirstOrDefault();
            if (subscription == null)
                return new CurrentSubscriptionResponse { Status = "none" };

            var planKey = subscription.Metadata.ContainsKey("planKey")
                ? subscription.Metadata["planKey"]
                : "unknown";

            string customerName = "unknown";
            if (!string.IsNullOrEmpty(user.StripeCustomerId))
            {
                var customer = await customerService.GetAsync(user.StripeCustomerId);
                if (customer != null && !string.IsNullOrEmpty(customer.Name))
                    customerName = customer.Name;
            }

            return new CurrentSubscriptionResponse
            {
                PlanKey = planKey,
                Status = subscription.Status ?? "unknown",
                CustomerName = customerName
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] getCurrentSubscription error: {ex.Message}");
			return new CurrentSubscriptionResponse
			{
				PlanKey = null,
				Status = "error",
				CustomerName = null
			};
        }
    }
}
