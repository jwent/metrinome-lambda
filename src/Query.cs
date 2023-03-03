using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;
using System.Text.Json;

public class Query
{
    public static string hello() => "hello world!";
    [Authorize(Policy = "CustomerPolicy")]
    public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();
    [Authorize(Policy = "CustomerPolicy")]
    public static string? trackerCode(IResolveFieldContext context)
    {
        Guid userId;
        if (context.User.Identity is ClaimsIdentity identity)
            userId = Guid.Parse(identity.FindFirst("id").Value);
        else
            throw new Exception("id claim missing");

        var user_tracker = OnTrackDBContext.ctx.UserTrackers.First(t => t.Owner.Id == userId);

        // return @"<script type=""text/javascript"">!function(){let a=document.createElement(""script"");a.type=""text/javascript"";a.async=!0;a.src=""http:// ontracktestdeployment.s3-website-us-east-1.amazonaws.com/landing?callback=skroCb&id="+id+@"&""+window.location.search.substring(1);let b=document.getElementsByTagName(""script"")[0];b.parentNode.insertBefore(a,b)}();</script>";
        var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
        return @"<script type=""text/javascript"">const urlParams = new URLSearchParams(window.location.search);const cid=urlParams.get('cid');if(cid){const rpu = window.btoa(window.location.href);const rpr = window.btoa(document.referrer);(function(){fetch('" + endpoint + "?t=" + user_tracker.Id.ToString() + @"&r='+rpr+'&u='+rpu).then((response) => response.json()).then((data) => sessionStorage.setItem('clid',data.clid))})();}</script>";
    }
    [Authorize(Policy = "CustomerPolicy")]
    public static string? postbackCode(IResolveFieldContext context)
    {
        var endpoint = Environment.GetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL");
        var postbackCode = new {
            page = @"<script type=""text/javascript"">(function(){if(sessionStorage.getItem('clid')){fetch('" + endpoint + "postback?clid='+sessionStorage.getItem('clid'),{mode:'no-cors'})}})();</script>",
            button = @"<script type=""text/javascript"">function postClick(){if(sessionStorage.getItem('clid')){fetch('" + endpoint + "postback?clid='+sessionStorage.getItem('clid'),{mode:'no-cors'})}}; const confirmBtn=document.getElementById('{id}'); confirmBtn.addEventListener('click', postClick);</script>",
        };
        return JsonSerializer.Serialize(postbackCode);
    }
    [Authorize(Policy = "CustomerPolicy")]
    public static TrackingCampaign getCampaign(IResolveFieldContext context, string campaignId)
    {
        Guid userId;
        if (context.User.Identity is ClaimsIdentity identity)
            userId = Guid.Parse(identity.FindFirst("id").Value);
        else
            throw new Exception("id claim missing");

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.FirstOrDefault(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId, null);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        return existingCampaign;
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Campaigns myCampaigns(IResolveFieldContext context, DateTime? createdAt)
    {
        Guid userId;
        if (context.User.Identity is ClaimsIdentity identity)
            userId = Guid.Parse(identity.FindFirst("id").Value);
        else
            throw new Exception("id claim missing");

        var campaigns = OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.ParentTracker.Owner.Id == userId).OrderByDescending(c => c.CreatedAt);
        int count = campaigns.Count();
        List<TrackingCampaign> campaignList;
        if (createdAt.HasValue) {
            campaignList = campaigns.Where(e => e.CreatedAt < createdAt).Take(10).ToList();
        } else {
            campaignList = campaigns.Take(10).ToList();
        }
        var campaign_datas = campaignList.Select(e => new TrackingCampaignData(e,
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick == true).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true).Count(),
                OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.Conversion == true && c.IsDesktop == true).Count()))
        .ToList();
        return new Campaigns(campaign_datas,count);
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static Clicks myCampaignClicks(IResolveFieldContext context, string campaignId, DateTime? createdAt)
    {
        Guid userId;
        if (context.User.Identity is ClaimsIdentity identity)
            userId = Guid.Parse(identity.FindFirst("id").Value);
        else
            throw new Exception("id claim missing");

        var campaignGuid = Guid.Parse(campaignId);
        Console.WriteLine($"[+] searching campaigns by campaignId: ${campaignId}");
        var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.First(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId);
        if (existingCampaign == null)
            throw new Exception("campaign not found!");

        var myClicks = OnTrackDBContext.ctx.TrackerClicks.Where(e => e.Campaign.Id == campaignGuid).OrderByDescending(c => c.CreatedAt);
        int count = myClicks.Count();

        List<TrackerClickData> clicksList;
        if (createdAt.HasValue) {
            clicksList = myClicks.Where(e => e.CreatedAt < createdAt).Take(10).Select(click => new TrackerClickData(click))
            .ToList();
        } else {
            clicksList = myClicks.Take(10).Select(click => new TrackerClickData(click))
            .ToList();
        }
        
        return new Clicks(clicksList,count);
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static TrackingCampaignDetails myCampaignDetails(IResolveFieldContext context, string campaignId)
    {
        Guid userId;
        if (context.User.Identity is ClaimsIdentity identity)
            userId = Guid.Parse(identity.FindFirst("id").Value);
        else
            throw new Exception("id claim missing");

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
        
        var myClicks = OnTrackDBContext.ctx.TrackerClicks.Where(e => e.Campaign.Id == campaignGuid).OrderByDescending(c => c.CreatedAt);
        int count = myClicks.Count();
        List<TrackerClickData> clicksList = myClicks.Take(10).Select(click => new TrackerClickData(click)).ToList();
        return new TrackingCampaignDetails(campaignData, new Clicks(clicksList,count));
    }
}
