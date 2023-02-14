using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;


public class Mutation {
	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secretsecretsecret"));

	public static string? loginUser(string email, string password) {
		// find a user by email and password
		var user = OnTrackDBContext.ctx.Users
				.Where(u => u.Email == email && u.Password == password)
				.FirstOrDefault();
		if (user == null || user.Id == null)
			return null;

		// create a claim
		var claims = new List<Claim> {
			new Claim("id", user.Id.ToString()),
			new Claim("role", "Admin")
		};

		// sign it
		var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
		var token = new JwtSecurityToken(
			"issuer",
			"audience",
			claims,
			expires: DateTime.Now.AddDays(1),
			signingCredentials: signingCredentials
		);

		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	public static Guid? addUser(string email, string password) {
		// add the new user
		var user = new User { Id=Guid.NewGuid(), Email=email, Password=password, CreatedAt=DateTime.Now };
		OnTrackDBContext.ctx.Users.Add(user);
		// add the user's tracker immediately
		var tracker = new UserTracker { Id=Guid.NewGuid(), Owner=user, CreatedAt=DateTime.Now };
		OnTrackDBContext.ctx.UserTrackers.Add(tracker);
		OnTrackDBContext.ctx.SaveChanges();

		// return is pointless
		return user.Id;
	}

	[Authorize(Policy = "AdminPolicy")]
	public static Guid? createCampaign(IResolveFieldContext context, TrackingCampaignSubmission campaign) {
		Guid userId;
		if (context.User.Identity is ClaimsIdentity identity)
			userId = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		Console.WriteLine($"[+] searching users by userId: ${userId}");
		var user = OnTrackDBContext.ctx.Users.First(u => u.Id == userId);
		Console.WriteLine($"[+] searching user trackers by userId: ${userId}");
		var user_tracker = OnTrackDBContext.ctx.UserTrackers.First(t => t.Owner.Id == userId);

		Console.WriteLine($"[+] creating the new campaign: ${campaign.CampaignName}");
		var newCampaign = new TrackingCampaign();
		newCampaign.Id = Guid.NewGuid();
		newCampaign.ParentTracker = user_tracker;
		newCampaign.CreatedAt = DateTime.Now;
		newCampaign.Audience = new Random().Next(3, 100);

		newCampaign.Platform = campaign.Platform ?? newCampaign.Platform;
		newCampaign.CampaignName = campaign.CampaignName ?? newCampaign.CampaignName;
		newCampaign.CampaignBudget = campaign.CampaignBudget ?? newCampaign.CampaignBudget;
		newCampaign.ConversionValue = campaign.ConversionValue ?? newCampaign.ConversionValue;
		newCampaign.WebsiteDomain = campaign.WebsiteDomain ?? newCampaign.WebsiteDomain;
		newCampaign.CartPageURL = campaign.CartPageURL ?? newCampaign.CartPageURL;
		newCampaign.LandingPageURL = campaign.LandingPageURL ?? newCampaign.LandingPageURL;
		newCampaign.PrivacyPageURL = campaign.PrivacyPageURL ?? newCampaign.PrivacyPageURL;

		OnTrackDBContext.ctx.TrackingCampaigns.Add(newCampaign);
		OnTrackDBContext.ctx.SaveChanges();

		return newCampaign.Id;
	}

	[Authorize(Policy = "AdminPolicy")]
	public static Guid? updateCampaign(IResolveFieldContext context, string campaignId, TrackingCampaignSubmission campaign) {
		Console.WriteLine("updateCampaign started!");
		Guid userId;
		if (context.User.Identity is ClaimsIdentity identity)
			userId = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		var campaignGuid = Guid.Parse(campaignId);
		var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId).FirstOrDefault();
		if (existingCampaign == null)
			throw new Exception("campaign not found!");

		existingCampaign.Platform = campaign.Platform ?? existingCampaign.Platform;
		existingCampaign.CampaignName = campaign.CampaignName ?? existingCampaign.CampaignName;
		existingCampaign.CampaignBudget = campaign.CampaignBudget ?? existingCampaign.CampaignBudget;
		existingCampaign.ConversionValue = campaign.ConversionValue ?? existingCampaign.ConversionValue;
		existingCampaign.WebsiteDomain = campaign.WebsiteDomain ?? existingCampaign.WebsiteDomain;
		existingCampaign.CartPageURL = campaign.CartPageURL ?? existingCampaign.CartPageURL;
		existingCampaign.LandingPageURL = campaign.LandingPageURL ?? existingCampaign.LandingPageURL;
		existingCampaign.PrivacyPageURL = campaign.PrivacyPageURL ?? existingCampaign.PrivacyPageURL;
		OnTrackDBContext.ctx.SaveChanges();

		// Console.WriteLine("got id: " + id);
		// campaign.Id = Guid.NewGuid();
		// campaign.OwnerId = Guid.Parse(id);
		// campaign.CreatedAt = DateTime.Now;
		// campaign.Audience = 0;

		// Console.WriteLine("got campaign: " + campaign.ToString());
		// try {
		// 	OnTrackDBContext.ctx.TrackingCampaigns.Add(campaign);
		// 	Console.WriteLine("added campaign: " + campaign.ToString());
		// 	OnTrackDBContext.ctx.SaveChanges();
		// } catch (Exception e) {
		// 	Console.WriteLine("exception: " + e.ToString());
		// }
		// Console.WriteLine("saved campaign: " + campaign.ToString());

		// Console.WriteLine("returning id: " + campaign.Id);
		return existingCampaign.Id;
	}
}
