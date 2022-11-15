using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;
using System.Security.Claims;


public class Mutation {
	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secretsecretsecret"));

	public static string? loginUser(string email, string password) {
		var user = OnTrackDBContext.ctx.Users
				.Where(u => u.Email == email && u.Password == password)
				.FirstOrDefault();

		if (user == null || user.Id == null)
			return null;

		var claims = new List<Claim> {
			new Claim("id", user.Id.ToString()),
			new Claim("role", "Admin")
		};

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
		var user = new User { Id=Guid.NewGuid(), Email=email, Password=password };
		OnTrackDBContext.ctx.Users.Add(user);
		OnTrackDBContext.ctx.SaveChanges();

		return user.Id;
	}

	[Authorize(Policy = "AdminPolicy")]
	public static Guid? createCampaign(IResolveFieldContext context, TrackingCampaignSubmission campaign) {
		string id;
		if (context.User.Identity is ClaimsIdentity identity)
			id = identity.FindFirst("id").Value;
		else
			throw new Exception("id claim missing");

		var newCampaign = new TrackingCampaign();
		newCampaign.Id = Guid.NewGuid();
		newCampaign.OwnerId = Guid.Parse(id);
		newCampaign.CreatedAt = DateTime.Now;
		newCampaign.Audience = new Random().Next(3, 100);

		newCampaign.Platform = campaign.Platform ?? newCampaign.Platform;
		newCampaign.CampaignName = campaign.CampaignName ?? newCampaign.CampaignName;
		newCampaign.TargetedCountries = campaign.TargetedCountries ?? newCampaign.TargetedCountries;
		newCampaign.CampaignBudget = campaign.CampaignBudget ?? newCampaign.CampaignBudget;
		newCampaign.CampaignDuration = campaign.CampaignDuration ?? newCampaign.CampaignDuration;
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
		Guid id;
		if (context.User.Identity is ClaimsIdentity identity)
			id = Guid.Parse(identity.FindFirst("id").Value);
		else
			throw new Exception("id claim missing");

		var campaignGuid = Guid.Parse(campaignId);
		var existingCampaign = OnTrackDBContext.ctx.TrackingCampaigns.Where(e => e.Id == campaignGuid && e.OwnerId == id).FirstOrDefault();
		if (existingCampaign == null)
			throw new Exception("campaign not found!");

		existingCampaign.Platform = campaign.Platform ?? existingCampaign.Platform;
		existingCampaign.CampaignName = campaign.CampaignName ?? existingCampaign.CampaignName;
		existingCampaign.TargetedCountries = campaign.TargetedCountries ?? existingCampaign.TargetedCountries;
		existingCampaign.CampaignBudget = campaign.CampaignBudget ?? existingCampaign.CampaignBudget;
		existingCampaign.CampaignDuration = campaign.CampaignDuration ?? existingCampaign.CampaignDuration;
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
