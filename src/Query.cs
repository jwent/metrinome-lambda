using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;

public class Query
{
    public static string hello() => "hello world!";
    // [Authorize(Policy = "CustomerPolicy")]
    // public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();

    [Authorize(Policy = "CustomerPolicy")]
    public static UserData getUserData(IResolveFieldContext context) {
        var userId = Util.GetCurrentUserId(context);
        var userdata = OnTrackDBContext.ctx.Users
            .Where(u => u.Id == userId)
            .Join(OnTrackDBContext.ctx.UserExtraProperties,
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
    public static string? trackerCode(IResolveFieldContext context) {
        var userId = Util.GetCurrentUserId(context);

        var user_tracker = OnTrackDBContext.ctx.UserTrackers.First(t => t.Owner.Id == userId);

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
    public static TrackingCampaign getCampaign(IResolveFieldContext context, string campaignId) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.FirstOrDefault(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId, null);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        return existingCampaign;
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Campaigns myCampaigns(IResolveFieldContext context, DateTime? createdAt) {
        var userId = Util.GetCurrentUserId(context);

        var campaigns = OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.ParentTracker.Owner.Id == userId).OrderByDescending(c => c.CreatedAt);
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
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick == true).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true && c.IsDesktop == true).Count()))
        .ToList();
        return new Campaigns(campaign_datas, count);
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Clicks myCampaignClicks(IResolveFieldContext context, string campaignId, DateTime? createdAt) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        var myClicks = OnTrackDBContext.ctx.TrackerClicks
                .Where(e => e.Campaign.Id == campaignGuid)
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    click => new { click.Id, PropertyKey = "ip_country" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (click, extraCountry) => new { click, extraCountry }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    combined => new { combined.click.Id, PropertyKey = "ip_region" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (combined, extraRegion) => new { combined.click, combined.extraCountry, extraRegion }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
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
    public static TrackingCampaignDetails myCampaignDetails(IResolveFieldContext context, string campaignId) {
        var userId = Util.GetCurrentUserId(context);

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        var campaignData = new TrackingCampaignData(existingCampaign,
                    OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id).Count(),
                    OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
                    OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.IsBotClick == true).Count(),
                    OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.Conversion == true).Count(),
                    OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == existingCampaign.Id && c.Conversion == true && c.IsDesktop == true).Count());

        var myClicks = OnTrackDBContext.ctx.TrackerClicks
                .Where(e => e.Campaign.Id == campaignGuid)
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    click => new { click.Id, PropertyKey = "ip_country" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (click, extraCountry) => new { click, extraCountry }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    combined => new { combined.click.Id, PropertyKey = "ip_region" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (combined, extraRegion) => new { combined.click, combined.extraCountry, extraRegion }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
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


        var myConversions = OnTrackDBContext.ctx.TrackerClicks
                .Where(e => e.Campaign.Id == campaignGuid && e.Conversion.Value)
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    click => new { click.Id, PropertyKey = "ip_country" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (click, extraCountry) => new { click, extraCountry }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    combined => new { combined.click.Id, PropertyKey = "ip_region" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (combined, extraRegion) => new { combined.click, combined.extraCountry, extraRegion }
                )
                .Join(OnTrackDBContext.ctx.TrackerClickExtraProperties,
                    combined => new { combined.click.Id, PropertyKey = "ip_city" },
                    extra => new { extra.ClickParent.Id, extra.PropertyKey },
                    (combined, extraCity) => new { combined.click, combined.extraCountry, combined.extraRegion, extraCity }
                );
        var topLocations = myConversions
        .Select(combined => new TrackerClickData(combined.click, combined.extraCountry, combined.extraRegion, combined.extraCity)).ToList()
        .GroupBy(p => p.City).Select(m => new Location { City = m.Key, Count = m.Count() }).OrderByDescending(s => s.Count).Take(5).ToList();

        return new TrackingCampaignDetails(campaignData, new Clicks(clicksList, count), new ChartDatas(topLocations));
    }
}
