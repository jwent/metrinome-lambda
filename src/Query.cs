using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;

public class Query {
	public static string hello() => "hello world!";
	[Authorize(Policy = "AdminPolicy")]
	public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();
	[Authorize(Policy = "AdminPolicy")]
	public static string? trackerCode(IResolveFieldContext context) {
		Guid userId;
		if (context.User.Identity is ClaimsIdentity identity)
			userId = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		var user_tracker = OnTrackDBContext.ctx.UserTrackers.First(t => t.Owner.Id == userId);

		// return @"<script type=""text/javascript"">!function(){let a=document.createElement(""script"");a.type=""text/javascript"";a.async=!0;a.src=""http:// ontracktestdeployment.s3-website-us-east-1.amazonaws.com/landing?callback=skroCb&id="+id+@"&""+window.location.search.substring(1);let b=document.getElementsByTagName(""script"")[0];b.parentNode.insertBefore(a,b)}();</script>";
		return @"<script type=""text/javascript"">(function(){ fetch('http://localhost:4000/?t=" + user_tracker.Id.ToString() + @"&r='+document.referrer+'&u='+window.location.href).then(j => j.json()).then(console.log)})();</script>";
	}

	[Authorize(Policy = "AdminPolicy")]
	public static TrackingCampaign getCampaign(IResolveFieldContext context, string campaignId) {
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

	[Authorize(Policy = "AdminPolicy")]
	public static List<TrackingCampaignData> myCampaigns(IResolveFieldContext context) {
		Guid userId;
		if (context.User.Identity is ClaimsIdentity identity)
			userId = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		var campaign_datas = OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.ParentTracker.Owner.Id == userId).ToList()
			.Select(e => new TrackingCampaignData(e,
					OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id).Count(),
					OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick != true).GroupBy(c => c.Ip).Count(),
					OnTrackDBContext.ctx.TrackerClicks.Where(c => c.Campaign.Id == e.Id && c.IsBotClick == true).Count()))
			.ToList();
		return campaign_datas;
	}
}
