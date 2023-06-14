using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;

public class Mutation {
	public static SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Util.ValueOrDie(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY"))));

	public static LoginUserResponse loginUser([FromServices] OnTrackDBContext onTrackDBContext, string email, string password) {
		// find a user by email and password
		var user = onTrackDBContext.Users
				.Where(u => u.Email == email && u.UserState == "Active")
				.FirstOrDefault();

		// verify that we found a user
		if (user == null)
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

	public static SuccessResponse forgotPassword([FromServices] OnTrackDBContext onTrackDBContext, string email) {
		// find a user by email
		var user = onTrackDBContext.Users
				.Where(u => u.UserState == "Active" && u.Email == email)
				.FirstOrDefault();
		if (user == null) {
			Console.WriteLine("[!] user not found");
			return new SuccessResponse { Success=true };
		}


		// generate a random token so that they can reset their password
		var randomResetToken = Util.GetSecureRandomString(64); // 256 bits of security
		user.ResetPasswordToken = randomResetToken;
		// save the property
		onTrackDBContext.SaveChanges();


		// TODO: email the user about their forgotten password

		return new SuccessResponse { Success=true };
	}

	public static AddUserResponse addUser([FromServices] OnTrackDBContext onTrackDBContext, string fullname, string email, string password) {
		// find a user by email
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

		// create both the user, their organization, and the role between them
		var newUser = new User {
				Id=Guid.NewGuid(),
				Email=email,
				Password=passwordHash,
				CreatedAt=DateTime.Now,
				ResetPasswordToken="",
				UserState="Active",
			};
		var newOrganization = new UserOrganization {
				Id=Guid.NewGuid(),
				OwnerId=newUser.Id,
				CreatedAt=DateTime.Now,
			};
		newUser.Organization = newOrganization;
		var newRole = new UserOrganizationalRoleAssociation {
				Id=Guid.NewGuid(),
				OrganizationUser=newUser,
				Organization=newOrganization,
				RoleName="Owner",
			};


		try {
			// add the new user
			onTrackDBContext.Users.Add(newUser);
			// add the new organization
			onTrackDBContext.UserOrganizations.Add(newOrganization);
			onTrackDBContext.UserOrganizationalRoleAssociations.Add(newRole);
			// try to commit the user
			onTrackDBContext.SaveChanges();

		} catch (Microsoft.EntityFrameworkCore.DbUpdateException e) {
			Console.WriteLine("duplicate user key error:" + e.ToString());
			onTrackDBContext.Users.Remove(newUser);
			onTrackDBContext.UserOrganizations.Remove(newOrganization);
			onTrackDBContext.UserOrganizationalRoleAssociations.Remove(newRole);
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// add the user's tracker immediately
		var tracker = new UserTracker { Id=Guid.NewGuid(), Organization=newOrganization, CreatedAt=DateTime.Now };
		onTrackDBContext.UserTrackers.Add(tracker);

		// add a meta property for the user's fullname
		onTrackDBContext.UserExtraProperties.Add(new UserExtraProperty {
			Id = Guid.NewGuid(),
			Parent = newUser,
			PropertyKey = "FullName",
			PropertyValue = fullname,
		});
		onTrackDBContext.SaveChanges();

		// return is pointless
		return new AddUserResponse { Id=newUser.Id };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static async Task<AddUserResponse> addUserToOrganization(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string email) {
		// get the current user and their organization
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Include(u => u.Organization)
			.First(u => u.Id == userId);

		// check user AuthZ
		if (!UserController.CanUserDo(onTrackDBContext, user.Id, user.Organization.Id, UserController.INVITE_ORGANIZATIONAL_USER_PERMISSION))
			return new AddUserResponse { Error="Forbidden." };

		// find a user by email
		var possibleUser = onTrackDBContext.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();
		if (possibleUser != null) {
			Console.WriteLine("duplicate user!");
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		// check email parameter settings
		if (email.Length > 128)
			return new AddUserResponse { Error="Invalid or duplicate email." };

		// generate a random token so that they can register
		var randomResetToken = Util.GetSecureRandomString(64); // 256 bits of security
		// create both the user and their organization
		var newUser = new User {
				Id=Guid.NewGuid(),
				CreatedAt=DateTime.Now,
				Email=email,
				Password="",
				ResetPasswordToken=randomResetToken,
				Organization=user.Organization,
				UserState="Invited",
			};
		var newRole = new UserOrganizationalRoleAssociation {
				Id=Guid.NewGuid(),
				OrganizationUser=newUser,
				Organization=user.Organization,
				RoleName="Viewer",
			};

		try {
			// add the new user
			onTrackDBContext.Users.Add(newUser);
			// add the new organization
			onTrackDBContext.UserOrganizationalRoleAssociations.Add(newRole);
			// try to commit the user
			onTrackDBContext.SaveChanges();

		} catch (Microsoft.EntityFrameworkCore.DbUpdateException e) {
			Console.WriteLine("duplicate user key error:" + e.ToString());
			onTrackDBContext.Users.Remove(newUser);
			onTrackDBContext.UserOrganizationalRoleAssociations.Remove(newRole);
			return new AddUserResponse { Error="Invalid or duplicate email." };
		}

		await EmailController.SendEmail(email, "You've been invited to an On Track Analytics organization",
				$"Please follow <a href='{Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")}SignUpOrgUser?resetkey={randomResetToken}'>this link</a> to register your account and join the organization.");

		// response is only for success
		return new AddUserResponse { Id=newUser.Id };
	}

	public static AddUserResponse registerNewOrganizationalUser(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string resetToken, string fullname, string password) {
		
		// find a user by email
		var newUser = onTrackDBContext.Users
				.Where(u => u.UserState == "Invited" && u.ResetPasswordToken == resetToken)
				.FirstOrDefault();
		if (newUser == null) {
			Console.WriteLine("[!] new user not found");
			return new AddUserResponse { Error="Invalid or missing user." };
		}

		// assert stuff
		if (fullname.Length > 128)
			return new AddUserResponse { Error="Invalid fullname." };
		if (password.Length < 8)
			return new AddUserResponse { Error="Password too short." };

		// salt and hash the new password
		var passwordHash = Util.SaltAndHash(password);

		// set properties
		newUser.Password = passwordHash;
		newUser.UserState = "Active";
		newUser.ResetPasswordToken = ""; // clear the reset token to prevent reuse

		// add a meta property for the user's fullname
		onTrackDBContext.UserExtraProperties.Add(new UserExtraProperty {
			Id = Guid.NewGuid(),
			Parent = newUser,
			PropertyKey = "FullName",
			PropertyValue = fullname,
		});
		// save the properties and meta prop
		onTrackDBContext.SaveChanges();

		// response is only for success
		return new AddUserResponse { Id=newUser.Id };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static Guid? createCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, TrackingCampaignSubmission campaign) {
		var userId = UserController.GetCurrentUserId(context);

		Console.WriteLine($"[+] searching user trackers by userId: ${userId}");
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

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
