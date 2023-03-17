using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;
using System.Text.Json;

public class Mutation {
	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY")));

	public static string? loginUser(string email, string password) {
		// find a user by email and password
		var user = OnTrackDBContext.ctx.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();

		// verify that we found a user
		if (user == null || user.Id == null)
            return JsonSerializer.Serialize(new { status = "Error", message = "Invalid email or password." });
		// verify password
		if (!Util.VerifyHash(password, user.Password))
			return JsonSerializer.Serialize(new { status = "Error", message = "Invalid email or password." });

		// create a claim
		var claims = new List<Claim> {
			new Claim("id", user.Id.ToString()),
			new Claim("role", "Customer")
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
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
		return JsonSerializer.Serialize(new { status = "OK", message = tokenString });
	}

	public static AddUserResponse addUser(string email, string password) {
		// find a user by email and password
		var possibleUser = OnTrackDBContext.ctx.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();
		if (possibleUser != null) {
			Console.WriteLine("duplicate user!");
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// assert stuff
		if (email.Length > 128)
			return new AddUserResponse { Error="Invalid or duplicate email." };

		// salt and hash the new password
		var passwordHash = Util.SaltAndHash(password);

		Console.WriteLine("email");

		var user = new User { Id=Guid.NewGuid(), Email=email, Password=passwordHash, CreatedAt=DateTime.Now };
		try {
			// add the new user
			OnTrackDBContext.ctx.Users.Add(user);
			// try to commit the user
			OnTrackDBContext.ctx.SaveChanges();

		} catch (Microsoft.EntityFrameworkCore.DbUpdateException e) {
			Console.WriteLine("duplicate user key error");
			OnTrackDBContext.ctx.Users.Remove(user);
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// add the user's tracker immediately
		var tracker = new UserTracker { Id=Guid.NewGuid(), Owner=user, CreatedAt=DateTime.Now };
		OnTrackDBContext.ctx.UserTrackers.Add(tracker);
		OnTrackDBContext.ctx.SaveChanges();

		// return is pointless
		return new AddUserResponse { Id=user.Id };
	}

	[Authorize(Policy = "CustomerPolicy")]
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

	[Authorize(Policy = "CustomerPolicy")]
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
