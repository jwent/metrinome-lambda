using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;

public class Mutation {
	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY")));


    public static LoginUserResponse loginUser([FromServices] OnTrackDBContext onTrackDBContext, string email, string password) {
		// find a user by email and password
		var user = onTrackDBContext.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();

		// verify that we found a user
		if (user == null || user.Id == null)
            return new LoginUserResponse { Error="Invalid email or password." };
		// verify password
		if (!Util.VerifyHash(password, user.Password))
			return new LoginUserResponse { Error="Invalid email or password." };

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
		// create token
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // return token
		return new LoginUserResponse { BearerToken=tokenString };
	}

	public static AddUserResponse addUser([FromServices] OnTrackDBContext onTrackDBContext, string fullname, string email, string password) {
		// find a user by email and password
		var possibleUser = onTrackDBContext.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();
		if (possibleUser != null) {
			Console.WriteLine("duplicate user!");
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// assert stuff
		if (fullname.Length > 128)
			return new AddUserResponse { Error="Invalid fullname." };
		if (email.Length > 128)
			return new AddUserResponse { Error="Invalid or duplicate email." };
		if (password.Length < 8)
			return new AddUserResponse { Error="Password too short." };

		// salt and hash the new password
		var passwordHash = Util.SaltAndHash(password);

		// create both the user and their organization
		var user = new User { Id=Guid.NewGuid(), Email=email, Password=passwordHash, CreatedAt=DateTime.Now, UserOrganizationalRole="Owner" };
		var organization = new UserOrganization { Id=Guid.NewGuid(), OwnerId=user.Id, CreatedAt=DateTime.Now };
		user.Organization = organization;

		try {
			// add the new user
			onTrackDBContext.Users.Add(user);
			// add the new organization
			onTrackDBContext.UserOrganizations.Add(organization);
			// try to commit the user
			onTrackDBContext.SaveChanges();

		} catch (Microsoft.EntityFrameworkCore.DbUpdateException e) {
			Console.WriteLine("duplicate user key error:" + e.ToString());
			onTrackDBContext.Users.Remove(user);
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// add the user's tracker immediately
		var tracker = new UserTracker { Id=Guid.NewGuid(), Organization=organization, CreatedAt=DateTime.Now };
		onTrackDBContext.UserTrackers.Add(tracker);

		// add a meta property for the user's fullname
		onTrackDBContext.UserExtraProperties.Add(new UserExtraProperty {
			Id = Guid.NewGuid(),
			Parent = user,
			PropertyKey = "FullName",
			PropertyValue = fullname,
		});
		onTrackDBContext.SaveChanges();

		// return is pointless
		return new AddUserResponse { Id=user.Id };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static Guid? createCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, TrackingCampaignSubmission campaign) {
		var userId = Util.GetCurrentUserId(context);

		Console.WriteLine($"[+] searching user trackers by userId: ${userId}");
		var userTracker = Util.GetUserTrackerByUser(onTrackDBContext, userId);

		Console.WriteLine($"[+] creating the new campaign: ${campaign.CampaignName}");
		var newCampaign = new TrackingCampaign();
		newCampaign.Id = Guid.NewGuid();
		newCampaign.ParentTracker = userTracker;
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

		onTrackDBContext.TrackingCampaigns.Add(newCampaign);
		onTrackDBContext.SaveChanges();

		var CampaignTypeProperty = new TrackingCampaignExtraProperty {
			Id = Guid.NewGuid(),
			Parent = newCampaign,
			PropertyKey = "CampaignType",
			PropertyValue = campaign.CampaignType ?? "Google Ads Search Campaign",
		};
		var PrimaryCampaignObjectiveProperty = new TrackingCampaignExtraProperty {
			Id = Guid.NewGuid(),
			Parent = newCampaign,
			PropertyKey = "PrimaryCampaignObjective",
			PropertyValue = campaign.PrimaryCampaignObjective ?? "Sales",
		};

		onTrackDBContext.TrackingCampaignExtraProperties.Add(CampaignTypeProperty);
		onTrackDBContext.TrackingCampaignExtraProperties.Add(PrimaryCampaignObjectiveProperty);
		onTrackDBContext.SaveChanges();

		return newCampaign.Id;
	}

	// [Authorize(Policy = "CustomerPolicy")]
	// public static Guid? updateCampaign(IResolveFieldContext context, string campaignId, TrackingCampaignSubmission campaign) {
	// 	Console.WriteLine("updateCampaign started!");
	// 	Guid userId;
	// 	if (context.User.Identity is ClaimsIdentity identity)
	// 		userId = Guid.Parse(identity.FindFirst("id").Value);
	// 	else
	// 		throw new Exception("id claim missing");

	// 	var campaignGuid = Guid.Parse(campaignId);
	// 	var existingCampaign = OnTrackDBContext.TrackingCampaigns.Where(e => e.Id == campaignGuid && e.ParentTracker.Owner.Id == userId).FirstOrDefault();
	// 	if (existingCampaign == null)
	// 		throw new Exception("campaign not found!");

	// 	existingCampaign.Platform = campaign.Platform ?? existingCampaign.Platform;
	// 	existingCampaign.CampaignName = campaign.CampaignName ?? existingCampaign.CampaignName;
	// 	existingCampaign.CampaignBudget = campaign.CampaignBudget ?? existingCampaign.CampaignBudget;
	// 	existingCampaign.ConversionValue = campaign.ConversionValue ?? existingCampaign.ConversionValue;
	// 	existingCampaign.WebsiteDomain = campaign.WebsiteDomain ?? existingCampaign.WebsiteDomain;
	// 	existingCampaign.CartPageURL = campaign.CartPageURL ?? existingCampaign.CartPageURL;
	// 	existingCampaign.LandingPageURL = campaign.LandingPageURL ?? existingCampaign.LandingPageURL;
	// 	existingCampaign.PrivacyPageURL = campaign.PrivacyPageURL ?? existingCampaign.PrivacyPageURL;
	// 	OnTrackDBContext.SaveChanges();

	// 	// Console.WriteLine("got id: " + id);
	// 	// campaign.Id = Guid.NewGuid();
	// 	// campaign.OwnerId = Guid.Parse(id);
	// 	// campaign.CreatedAt = DateTime.Now;
	// 	// campaign.Audience = 0;

	// 	// Console.WriteLine("got campaign: " + campaign.ToString());
	// 	// try {
	// 	// 	OnTrackDBContext.TrackingCampaigns.Add(campaign);
	// 	// 	Console.WriteLine("added campaign: " + campaign.ToString());
	// 	// 	OnTrackDBContext.SaveChanges();
	// 	// } catch (Exception e) {
	// 	// 	Console.WriteLine("exception: " + e.ToString());
	// 	// }
	// 	// Console.WriteLine("saved campaign: " + campaign.ToString());

	// 	// Console.WriteLine("returning id: " + campaign.Id);
	// 	return existingCampaign.Id;
	// }
}
