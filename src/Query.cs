using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;

public class Query
{
    public static string hello() => "hello world!";
    // [Authorize(Policy = "CustomerPolicy")]
    // public static List<User> users() => onTrackDBContext.Users.ToList();

    [Authorize(Policy = "CustomerPolicy")]
    public static UserData getUserData(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);
        var userdata = onTrackDBContext.Users
            .Where(u => u.Id == userId)
            .Join(onTrackDBContext.UserExtraProperties,
                user => new { user.Id, PropertyKey = "FullName" },
                extra => new { extra.Parent.Id, extra.PropertyKey },
                (user, extraFullName) => new { user, extraFullName }
            ).First();
        return new UserData {
            Email = userdata.user.Email,
            CreatedAt = userdata.user.CreatedAt,
            FullName = userdata.extraFullName.PropertyValue,
        };
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static string? trackerCode(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);

        var user_tracker = onTrackDBContext.UserTrackers.First(t => t.Owner.Id == userId);

        var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
        return Util.CompressJavascriptStub(@"<script type=""text/javascript"">
    (function(){
        const urlParams = new URLSearchParams(window.location.search);
        const cid=urlParams.get('cid');
        if(cid){
            const rpu = window.btoa(window.location.href);
            const rpr = window.btoa(document.referrer);
            (function(){
                fetch('" + endpoint + "?t=" + user_tracker.Id.ToString() + @"&r='+rpr+'&u='+rpu)
                    .then(r => r.json())
                    .then(d => sessionStorage.setItem('clid',d.clid))
                })();
        }
    })()
</script>");
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static PostbackCodes postbackCode(IResolveFieldContext context) {
        var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
        return new PostbackCodes {
            PagePostback = Util.CompressJavascriptStub(@"<script type=""text/javascript"">
    (function(){
        if(sessionStorage.getItem('clid')){
            fetch('" + endpoint + @"postback?clid='+sessionStorage.getItem('clid'),{mode:'no-cors'})
        }
    })()
</script>"),
            ButtonPostback = Util.CompressJavascriptStub(@"<script type=""text/javascript"">
    (function(){
        document.getElementById('{id}').addEventListener('click',function(){
            if(sessionStorage.getItem('clid')){
                fetch('" + endpoint + @"postback?clid='+sessionStorage.getItem('clid'),{mode:'no-cors'})
            }
        });
    })()
</script>"),
        };
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static TrackingCampaign getCampaign(IResolveFieldContext context, string campaignId, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = onTrackDBContext.TrackingCampaigns.FirstOrDefault(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId, null);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        return existingCampaign;
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Campaigns myCampaigns(IResolveFieldContext context, DateTime? createdAt, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);

        var campaigns = onTrackDBContext.TrackingCampaigns.Where(e => e.ParentTracker.Owner.Id == userId).OrderByDescending(c => c.CreatedAt);
        int count = campaigns.Count();
        List<TrackingCampaign> campaignList;
        if (createdAt.HasValue)
        {
            campaignList = campaigns.Where(e => e.CreatedAt < createdAt).Take(10).ToList();
        }
        else
        {
            campaignList = campaigns.Take(10).ToList();
        }
        var campaign_datas = campaignList.Select(e => new TrackingCampaignData(e,
                onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == e.Id).Count(),
                onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
                onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick == true).Count(),
                onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true).Count(),
                onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true && c.IsDesktop == true).Count()))
        .ToList();
        return new Campaigns(campaign_datas, count);
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Clicks myCampaignClicks(IResolveFieldContext context, string campaignId, DateTime? createdAt, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = onTrackDBContext.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId);
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
    public static TrackingCampaignDetails myCampaignDetails(IResolveFieldContext context, string campaignId, [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = onTrackDBContext.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        var campaignData = new TrackingCampaignData(existingCampaign,
                    onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id).Count(),
                    onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
                    onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.IsBotClick == true).Count(),
                    onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.Conversion == true).Count(),
                    onTrackDBContext.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.Conversion == true && c.IsDesktop == true).Count());

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

        List<TrackerClickData> clicksList = myClicks
                .Take(10)
                .Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity))
                .ToList();


        var myConversions = onTrackDBContext.TrackerClicks
                .Where(e => e.Campaign.Id == campaignGuid && e.Conversion.Value)
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
                );
        var topLocations = myConversions
        .Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity)).ToList()
        .GroupBy(p => p.City).Select(m => new Location { City = m.Key, Count = m.Count() }).OrderByDescending(s => s.Count).Take(5).ToList();

        return new TrackingCampaignDetails(campaignData, new Clicks(clicksList, count), new ChartDatas(topLocations));
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static GetCampaignStatsResponse myCampaignClickStats(IResolveFieldContext context, string campaignId, string? groupby="day", [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);
        var campaign = Util.GetCampaignById(userId, Guid.Parse(campaignId));

        // get the clicks by campaign
        var clicksQuery = onTrackDBContext.TrackerClicks
                .Where(e => e.Campaign.Id == campaign.Id);
        // groupby sub-query based on which group we need
        var subQuery =
                groupby == "day" ? clicksQuery.GroupBy(f => f.CreatedAt.Date.ToString()) :
                groupby == "hour" ? clicksQuery.GroupBy(f => f.CreatedAt.Date.ToString() + " " + f.CreatedAt.Hour) :
                throw new Exception("invalid groupby");

        // select the results
        var stats = subQuery
                .Select(g => new { datetime=g.Key, count=g.Count() })
                .ToList();
        // return formatted object
        return new GetCampaignStatsResponse {
            GroupedBy=groupby,
            Stats=stats.Select(s => new CampaignStatPoint { Position=s.datetime, Count=s.count }).ToList(),
        };
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static GetCampaignStatsResponse myCampaignConversionStats(IResolveFieldContext context, string campaignId, string? groupby="day", [FromServices] OnTrackDBContext onTrackDBContext) {
        var userId = Util.GetCurrentUserId(context);
        var campaign = Util.GetCampaignById(userId, Guid.Parse(campaignId));

        // get the clicks by campaign
        var conversionsQuery = onTrackDBContext.TrackerClicks
                .Where(e => e.Campaign.Id == campaign.Id && e.Conversion != null);
        // groupby sub-query based on which group we need
        var subQuery =
                groupby == "day" ? conversionsQuery.GroupBy(f => f.ConversionDate.Value.Date.ToString()) :
                groupby == "hour" ? conversionsQuery.GroupBy(f => f.ConversionDate.Value.Date.ToString() + " " + f.ConversionDate.Value.Hour) :
                throw new Exception("invalid groupby");

        // select the results
        var stats = subQuery
                .Select(g => new { datetime=g.Key, count=g.Count() })
                .ToList();
        // return formatted object
        return new GetCampaignStatsResponse {
            GroupedBy=groupby,
            Stats=stats.Select(s => new CampaignStatPoint { Position=s.datetime, Count=s.count }).ToList(),
        };
    }
}
