using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;

public class Query {
	public static string hello() => "hello world!";
	[Authorize(Policy = "AdminPolicy")]
	public static List<User> users() => OnTrackDBContext.ctx.Users.ToList();
	[Authorize(Policy = "AdminPolicy")]
	public static string? trackerCode(IResolveFieldContext context) {
		string id;
		if (context.User.Identity is ClaimsIdentity identity)
			id = identity.FindFirst("id").Value;
		else
			throw new Exception("id claim missing");

		return @"<script type=""text/javascript"">!function(){let a=document.createElement(""script"");a.type=""text/javascript"";a.async=!0;a.src=""http://ontracktestdeployment.s3-website-us-east-1.amazonaws.com/landing?callback=skroCb&id="+id+@"&""+window.location.search.substring(1);let b=document.getElementsByTagName(""script"")[0];b.parentNode.insertBefore(a,b)}();</script>";
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
		var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.FirstOrDefault(e => e.Id == campaignGuid && e.Owner.Id == userId, null);
		if (existingCampaign == null)
			throw new Exception("campaign not found!");

		return existingCampaign;
	}

	[Authorize(Policy = "AdminPolicy")]
	public static List<TrackingCampaign> myCampaigns(IResolveFieldContext context) {
		Console.WriteLine("myCampaigns started!");
		Guid userId;
		if (context.User.Identity is ClaimsIdentity identity)
			userId = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		return OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.Owner.Id == userId).ToList();
	}
}
