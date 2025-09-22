using Microsoft.EntityFrameworkCore;
using System.Text;
using GraphQL;
using GraphQL.Authorization;

public class Mutation {
        private const string StandardMonthlyPlanKey = "monthly_plan_299";
        private const string PremiumMonthlyPlanKey = "monthly_plan_499";
	public static LoginUserResponse loginUser([FromServices] OnTrackDBContext onTrackDBContext, string email, string password) {
		// find a user by email and password
		var user = onTrackDBContext.Users
				.Where(u => u.Email == email)
				.FirstOrDefault();
		// verify that we found a user
		if (user == null)
			return new LoginUserResponse { Error="Invalid email or password." };
		// if they haven't verified email yet, tell them
		if (user.UserState == "Invited")
			return new LoginUserResponse { Error="Please verify your email before logging in." };
		// verify password
		if (!Util.VerifyHash(password, user.Password))
			return new LoginUserResponse { Error="Invalid email or password." };
		// if they don't have an active account, no entry
		if (user.UserState != "Active")
			return new LoginUserResponse { Error="Account disabled." };

		// return token
		return new LoginUserResponse { BearerToken=Util.SignAuthToken(user) };
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

	[Authorize(Policy = "CustomerPolicy")]
	public static SuccessOrErrorResponse updateUserFullName(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string fullname) {
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Where(u => u.Id == userId)
			.Include(u => u.ExtraProperties)
			.First();

		// assert stuff
		if (fullname.Length > 128)
			return new SuccessOrErrorResponse { Success=false, Error="Invalid fullname." };

		// update the property
		var prop = user.ExtraProperties.FirstOrDefault(prop => prop.PropertyKey == "FullName");
		prop.PropertyValue = fullname;
		// save the property
		onTrackDBContext.SaveChanges();

		// return success
		return new SuccessOrErrorResponse { Success=true };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static SuccessOrErrorResponse updateUserPassword(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string password) {
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Where(u => u.Id == userId)
			.First();

		// assert stuff
		var passwordError = UserController.ValidatePasswordCreation(password);
		if (passwordError != null)
			return new SuccessOrErrorResponse { Success=false, Error=passwordError };

		// salt and hash the new password
		var passwordHash = Util.SaltAndHash(password);
		// put into the user and save
		user.Password = passwordHash;
		onTrackDBContext.SaveChanges();

		// return success
		return new SuccessOrErrorResponse { Success=true };
	}

        private static List<string> SubscriptionPlanOptions = new List<string> {
                StandardMonthlyPlanKey,
                PremiumMonthlyPlanKey,
        };

	[Authorize(Policy = "CustomerPolicy")]
        public static Task<CheckoutResponse> requestCheckout(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string plan) {
                var userId = UserController.GetCurrentUserId(context);
                var user = onTrackDBContext.Users
                        .Include(u => u.Organization.SubscriptionPlan)
                        .First(u => u.Id == userId);

                if (string.IsNullOrWhiteSpace(plan))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="missing plan" });

                plan = plan.Trim();

                // check that the plan is valid
                if (!SubscriptionPlanOptions.Contains(plan))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="invalid plan" });

                OrganizationalSubscriptionPlan? selectedPlan;
                try {
                        selectedPlan = UserController.GetSubscriptionPlanByKey(onTrackDBContext, plan);
                } catch (InvalidOperationException) {
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="plan unavailable" });
                }

                user.Organization.SubscriptionPlan = selectedPlan;
                onTrackDBContext.SaveChanges();

                var paymentFormUrl = Environment.GetEnvironmentVariable("ONTRACK_STRIPE_PAYMENT_FORM_URL");
                if (string.IsNullOrWhiteSpace(paymentFormUrl)) {
                        var siteUrl = Environment.GetEnvironmentVariable("ONTRACK_SITE_URL");
                        if (!string.IsNullOrWhiteSpace(siteUrl))
                                paymentFormUrl = siteUrl.TrimEnd('/') + "/stripe-payment";
                }

                string? redirectUrl = null;
                if (!string.IsNullOrWhiteSpace(paymentFormUrl)) {
                        var separator = paymentFormUrl.Contains('?') ? '&' : '?';
                        redirectUrl = $"{paymentFormUrl}{separator}plan={plan}";
                }

                return Task.FromResult(new CheckoutResponse {
                        Success=true,
                        Url=redirectUrl,
                        Error=null,
                        SelectedPlanKey=selectedPlan?.PlanKey,
                });
        }

	[Authorize(Policy = "CustomerPolicy")]
	public static SuccessResponse checkPayment(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Include(u => u.Organization.SubscriptionPlan)
			.First(u => u.Id == userId);

                user.Organization.SubscriptionPlan = user.Organization.SubscriptionPlan ?? UserController.GetSubscriptionPlanByKey(onTrackDBContext, StandardMonthlyPlanKey);
		onTrackDBContext.SaveChanges();

		return new SuccessResponse { Success=true };
	}

	public static async Task<AddUserResponse> addUser([FromServices] OnTrackDBContext onTrackDBContext, string fullname, string email, string password) {
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
		var passwordError = UserController.ValidatePasswordCreation(password);
		if (passwordError != null)
			return new AddUserResponse { Error=passwordError };

		// salt and hash the new password
		var passwordHash = Util.SaltAndHash(password);

		// generate a random token so that they can register
		var randomResetToken = Util.GetSecureRandomString(64); // 256 bits of security
		// create both the user, their organization, and the role between them
		var newUser = new User {
				Id=Guid.NewGuid(),
				Email=email,
				Password=passwordHash,
				CreatedAt=DateTime.Now,
				ResetPasswordToken=randomResetToken,
				UserState="Invited",
			};
		var newOrganization = new UserOrganization {
				Id=Guid.NewGuid(),
				CreatorId=newUser.Id,
				CreatedAt=DateTime.Now,
				SubscriptionPlan=null,
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

		// email the user to get started
		await EmailController.SendEmail(email, "Please verify your OnTrack Analytics account",
				$"Please follow <a href='{Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")}VerifyMainUserEmail?resetkey={randomResetToken}'>this link</a> to verify your account and use the platform.");

		// return is pointless
		return new AddUserResponse { Id=newUser.Id };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static async Task<AddUserResponse> addUserToOrganization(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string email) {
		// get the current user and their organization
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Include(u => u.Organization.Users)
			.Include(u => u.Organization.SubscriptionPlan)
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

		// check if the organization subscription plan allows for this additional user
		if (user.Organization.SubscriptionPlan == null || user.Organization.Users.Count >= user.Organization.SubscriptionPlan.UsersLimitPerPlan) {
			Console.WriteLine($"[+] organization has reached users limit! denied!");
			return new AddUserResponse { Error="Your subscription plan has reached user limit." };
		}

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

		await EmailController.SendEmail(email, "You've been invited to an OnTrack Analytics organization",
				$"Please follow <a href='{Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")}SignUpOrgUser?resetkey={randomResetToken}'>this link</a> to register your account and join the organization.");

		// response is only for success
		return new AddUserResponse { Id=newUser.Id };
	}

	public static AddUserResponse registerNewOrganizationalUser(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string resetToken, string fullname, string password) {
		// assert that the resetToken is not invalid
		if (resetToken.Length < 8)
			return new AddUserResponse { Error="Invalid or missing user." };
		
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
		var passwordError = UserController.ValidatePasswordCreation(password);
		if (passwordError != null)
			return new AddUserResponse { Error=passwordError };

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

	public static LoginUserResponse verifyUserEmail(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string resetToken) {
		// assert that the resetToken is not invalid
		if (resetToken.Length < 8)
			return new LoginUserResponse { Error="Invalid or missing user." };
		
		// find a user by email
		var user = onTrackDBContext.Users
				.Where(u => u.UserState == "Invited" && u.ResetPasswordToken == resetToken)
				.Include(u => u.Organization)
				.FirstOrDefault();
		if (user == null) {
			Console.WriteLine("[!] new user not found");
			return new LoginUserResponse { Error="Invalid or missing user." };
		}

		// set properties
		user.UserState = "Active";
		user.ResetPasswordToken = ""; // clear the reset token to prevent reuse

                // save the properties
                onTrackDBContext.SaveChanges();

		// return token
		return new LoginUserResponse { BearerToken=Util.SignAuthToken(user) };
	}

	[Authorize(Policy = "CustomerPolicy")]
	public static Guid? createCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, TrackingCampaignSubmission campaign) {
		var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
			.Include(u => u.Organization.OrganizationalTrackers)
			.ThenInclude(t => t.Campaigns)
			.Include(u => u.Organization.SubscriptionPlan)
			.First(u => u.Id == userId);

		Console.WriteLine($"[+] searching user trackers by userId: {userId}");
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		// check if the organization subscription plan allows for this additional campaign
		if (user.Organization.SubscriptionPlan == null || user.Organization.OrganizationalTrackers[0].Campaigns.Count >= user.Organization.SubscriptionPlan.CampaignsLimitPerPlan) {
			Console.WriteLine($"[+] organization has reached campaigns limit! denied!");
			return null;
		}

		Console.WriteLine($"[+] creating the new campaign: {campaign.CampaignName}");
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

	[Authorize(Policy = "CustomerPolicy")]
	public static Guid? updateCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, TrackingCampaignSubmission campaign, string campaignId) {
		var userId = UserController.GetCurrentUserId(context);

		Console.WriteLine($"[+] searching user trackers by userId: {userId}");
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		Console.WriteLine($"[+] searching campaign by campaignId: {campaignId}");
		var oldCampaign = TrackerController.GetCampaignById(onTrackDBContext, userTracker.Id, Guid.Parse(campaignId));

		Console.WriteLine($"[+] updating the campaign: {campaignId}");
		
		oldCampaign.Platform = campaign.Platform ?? oldCampaign.Platform;
		oldCampaign.CampaignName = campaign.CampaignName ?? oldCampaign.CampaignName;
		oldCampaign.CampaignBudget = campaign.CampaignBudget ?? oldCampaign.CampaignBudget;
		oldCampaign.ConversionValue = campaign.ConversionValue ?? oldCampaign.ConversionValue;
		oldCampaign.WebsiteDomain = campaign.WebsiteDomain ?? oldCampaign.WebsiteDomain;
		oldCampaign.CartPageURL = campaign.CartPageURL ?? oldCampaign.CartPageURL;
		oldCampaign.LandingPageURL = campaign.LandingPageURL ?? oldCampaign.LandingPageURL;
		oldCampaign.PrivacyPageURL = campaign.PrivacyPageURL ?? oldCampaign.PrivacyPageURL;

		onTrackDBContext.TrackingCampaigns.Update(oldCampaign);
		onTrackDBContext.SaveChanges();

		return oldCampaign.Id;
	}
	[Authorize(Policy = "CustomerPolicy")]
	public static string? deleteCampaign(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string campaignId) {
		var userId = UserController.GetCurrentUserId(context);

		Console.WriteLine($"[+] searching user trackers by userId: {userId}");
		var userTracker = TrackerController.GetUserTrackerByUser(onTrackDBContext, userId);

		Console.WriteLine($"[+] searching campaign by campaignId: {campaignId}");
		var campaign = TrackerController.GetCampaignById(onTrackDBContext, userTracker.Id, Guid.Parse(campaignId));

		var clicks = onTrackDBContext.TrackerClicks.Where(t=>t.Campaign.Id==campaign.Id).ToList();
		foreach(var click in clicks){
			var trackerClickExtraProperties = onTrackDBContext.TrackerClickExtraProperties.Where(t=>t.ClickParent.Id==click.Id).ToList();
			Console.WriteLine($"[+] remove click extras: {click.Id}");
			onTrackDBContext.RemoveRange(trackerClickExtraProperties);
		}
		Console.WriteLine($"[+] remove clicks: {campaignId}");
		onTrackDBContext.RemoveRange(clicks);
		var campaignExtraProperties = onTrackDBContext.TrackingCampaignExtraProperties.Where(t=>t.Parent.Id==campaign.Id).ToList();

		Console.WriteLine($"[+] remove campaign extras: {campaignId}");
		onTrackDBContext.RemoveRange(campaignExtraProperties);

		Console.WriteLine($"[+] removing the campaign: {campaignId}");

		onTrackDBContext.TrackingCampaigns.Remove(campaign);
		onTrackDBContext.SaveChanges();

		return campaignId;
	}
}
