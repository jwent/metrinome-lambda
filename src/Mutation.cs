using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GraphQL;
using GraphQL.Authorization;
using Stripe;

public class Mutation {
        private const string StandardMonthlyPlanKey = StripePlanConfiguration.BasicPlanKey;
        private const string PremiumMonthlyPlanKey = StripePlanConfiguration.ProPlanKey;
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

        private static readonly IReadOnlyDictionary<string, StripePlanDetails> SubscriptionPlanOptions =
                StripePlanConfiguration.GetAllPlans().ToDictionary(plan => plan.PlanKey, plan => plan);

        [Authorize(Policy = "CustomerPolicy")]
        public static Task<CheckoutResponse> setupSubscription(
                IResolveFieldContext context,
                [FromServices] OnTrackDBContext onTrackDBContext,
                string plan) {
                return startSubscriptionCheckout(context, onTrackDBContext, plan);
        }

        [Authorize(Policy = "CustomerPolicy")]
        public static Task<CheckoutResponse> startSubscriptionCheckout(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string plan) {
                var userId = UserController.GetCurrentUserId(context);
                var user = onTrackDBContext.Users
                        .Include(u => u.Organization.SubscriptionPlan)
                        .First(u => u.Id == userId);

                if (string.IsNullOrWhiteSpace(plan))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="missing plan" });

                plan = plan.Trim();

                // check that the plan is valid
                if (!SubscriptionPlanOptions.ContainsKey(plan))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="invalid plan" });

                OrganizationalSubscriptionPlan? selectedPlan;
                try {
                        selectedPlan = UserController.GetSubscriptionPlanByKey(onTrackDBContext, plan);
                } catch (InvalidOperationException) {
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="plan unavailable" });
                }

                user.Organization.SubscriptionPlan = selectedPlan;
                onTrackDBContext.SaveChanges();
                var publishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
                if (string.IsNullOrWhiteSpace(publishableKey))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="Stripe publishable key not configured." });

                var planDetails = SubscriptionPlanOptions[plan];
                var priceId = planDetails.ResolvePriceIdFromEnvironment();
                if (string.IsNullOrWhiteSpace(priceId))
                        return Task.FromResult(new CheckoutResponse { Success=false, Error="Stripe price id not configured for plan." });

                return Task.FromResult(new CheckoutResponse {
                        Success=true,
                        Url=null,
                        Error=null,
                        SelectedPlanKey=selectedPlan?.PlanKey,
                        PublishableKey=publishableKey.Trim(),
                        PriceId=priceId,
                        Currency=planDetails.Currency,
                        AmountCents=planDetails.AmountCents,
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

        public static async Task<CreatePaymentIntentResponse> createPaymentIntent(
                [FromServices] PaymentIntentService paymentIntentService,
                [FromServices] ILogger<Mutation> logger,
                long amount,
                string currency,
                string? plan) {
                if (amount <= 0)
                        return new CreatePaymentIntentResponse { Success = false, Error = "Amount must be greater than zero." };

                if (string.IsNullOrWhiteSpace(currency))
                        return new CreatePaymentIntentResponse { Success = false, Error = "Currency is required." };

                var trimmedCurrency = currency.Trim();
                var metadata = new Dictionary<string, string>();

                if (!string.IsNullOrWhiteSpace(plan))
                        metadata["plan"] = plan.Trim();

                var options = new PaymentIntentCreateOptions {
                        Amount = amount,
                        Currency = trimmedCurrency.ToLowerInvariant(),
                        AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions {
                                Enabled = true,
                        },
                        Metadata = metadata.Count > 0 ? metadata : null,
                };

                try {
                        var paymentIntent = await paymentIntentService.CreateAsync(options);
                        return new CreatePaymentIntentResponse {
                                Success = true,
                                ClientSecret = paymentIntent.ClientSecret,
                        };
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to create Stripe payment intent.");
                        return new CreatePaymentIntentResponse {
                                Success = false,
                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                        };
                } catch (Exception ex) {
                        logger.LogError(ex, "Unexpected error while creating payment intent.");
                        return new CreatePaymentIntentResponse {
                                Success = false,
                                Error = "Unable to create payment intent.",
                        };
                }
        }

        public static async Task<CreateSubscriptionResponse> completeSubscription(
                [FromServices] CustomerService customerService,
                [FromServices] SubscriptionService subscriptionService,
                [FromServices] PaymentMethodService paymentMethodService,
                [FromServices] ILogger<Mutation> logger,
                string planKey,
                string paymentMethodId,
                string customerEmail,
                string? customerName) {
                if (string.IsNullOrWhiteSpace(planKey))
                        return new CreateSubscriptionResponse { Success = false, Error = "Plan is required." };

                var trimmedPlanKey = planKey.Trim();
                if (!StripePlanConfiguration.TryGetPlanDetails(trimmedPlanKey, out var planDetails))
                        return new CreateSubscriptionResponse { Success = false, Error = "Invalid plan." };

                if (string.IsNullOrWhiteSpace(paymentMethodId))
                        return new CreateSubscriptionResponse { Success = false, Error = "Payment method is required." };

                var trimmedPaymentMethodId = paymentMethodId.Trim();

                if (string.IsNullOrWhiteSpace(customerEmail))
                        return new CreateSubscriptionResponse { Success = false, Error = "Customer email is required." };

                var trimmedCustomerEmail = customerEmail.Trim();
                var priceId = planDetails.ResolvePriceIdFromEnvironment();
                if (string.IsNullOrWhiteSpace(priceId))
                        return new CreateSubscriptionResponse { Success = false, Error = "Stripe price id is not configured for the requested plan." };

                Customer? customer = null;
                try {
                        var existingCustomers = await customerService.ListAsync(new CustomerListOptions {
                                Email = trimmedCustomerEmail,
                                Limit = 1,
                        });
                        customer = existingCustomers.Data.FirstOrDefault();
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to list Stripe customers.");
                        return new CreateSubscriptionResponse {
                                Success = false,
                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                        };
                }

                var trimmedCustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();

                if (customer == null) {
                        try {
                                customer = await customerService.CreateAsync(new CustomerCreateOptions {
                                        Email = trimmedCustomerEmail,
                                        Name = trimmedCustomerName,
                                        Metadata = new Dictionary<string, string> {
                                                { "planKey", planDetails.PlanKey },
                                        },
                                });
                        } catch (StripeException ex) {
                                logger.LogError(ex, "Failed to create Stripe customer.");
                                return new CreateSubscriptionResponse {
                                        Success = false,
                                        Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                                };
                        }
                } else if (!string.IsNullOrWhiteSpace(trimmedCustomerName)) {
                        if (!string.Equals(customer.Name, trimmedCustomerName, StringComparison.Ordinal)) {
                                try {
                                        customer = await customerService.UpdateAsync(customer.Id, new CustomerUpdateOptions {
                                                Name = trimmedCustomerName,
                                        });
                                } catch (StripeException ex) {
                                        logger.LogError(ex, "Failed to update Stripe customer.");
                                        return new CreateSubscriptionResponse {
                                                Success = false,
                                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                                        };
                                }
                        }
                }

                if (customer == null)
                        return new CreateSubscriptionResponse { Success = false, Error = "Unable to prepare customer for subscription." };

                try {
                        await paymentMethodService.AttachAsync(trimmedPaymentMethodId, new PaymentMethodAttachOptions {
                                Customer = customer.Id,
                        });
                } catch (StripeException ex) when (ex.StripeError?.Code == "resource_already_exists") {
                        logger.LogDebug("Payment method already attached to customer {CustomerId}.", customer.Id);
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to attach payment method to customer.");
                        return new CreateSubscriptionResponse {
                                Success = false,
                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                        };
                }

                try {
                        await customerService.UpdateAsync(customer.Id, new CustomerUpdateOptions {
                                InvoiceSettings = new CustomerInvoiceSettingsOptions {
                                        DefaultPaymentMethod = trimmedPaymentMethodId,
                                },
                        });
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to set default payment method for customer.");
                        return new CreateSubscriptionResponse {
                                Success = false,
                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                        };
                }

                Subscription? existingSubscription = null;
                try {
                        var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions {
                                Customer = customer.Id,
                                Status = "active",
                                Limit = 1,
                                Expand = new List<string> { "data.latest_invoice.payment_intent", "data.items.data.price" },
                        });
                        existingSubscription = subscriptions.Data.FirstOrDefault();
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to list Stripe subscriptions.");
                }

                var publishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
                var publishableKeyValue = string.IsNullOrWhiteSpace(publishableKey) ? null : publishableKey.Trim();

                if (existingSubscription != null && existingSubscription.Items?.Data?.Any() == true) {
                        var subscriptionItem = existingSubscription.Items.Data.First();
                        if (subscriptionItem.Price != null && string.Equals(subscriptionItem.Price.Id, priceId, StringComparison.Ordinal)) {
                                return new CreateSubscriptionResponse {
                                        Success = true,
                                        SubscriptionId = existingSubscription.Id,
                                        CustomerId = customer.Id,
                                        PublishableKey = publishableKeyValue,
                                        PlanKey = planDetails.PlanKey,
                                        PriceId = priceId,
                                        AlreadySubscribed = true,
                                };
                        }

                        try {
                                var updatedSubscription = await subscriptionService.UpdateAsync(existingSubscription.Id, new SubscriptionUpdateOptions {
                                        CancelAtPeriodEnd = false,
                                        ProrationBehavior = "create_prorations",
                                        Items = new List<SubscriptionItemOptions> {
                                                new SubscriptionItemOptions {
                                                        Id = subscriptionItem.Id,
                                                        Price = priceId,
                                                },
                                        },
                                        Expand = new List<string> { "latest_invoice.payment_intent" },
                                });

                                var updateInvoice = updatedSubscription.LatestInvoice as Invoice;
                                var updatePaymentIntent = updateInvoice?.PaymentIntent;

                                return new CreateSubscriptionResponse {
                                        Success = true,
                                        SubscriptionId = updatedSubscription.Id,
                                        CustomerId = customer.Id,
                                        PublishableKey = publishableKeyValue,
                                        PlanKey = planDetails.PlanKey,
                                        PriceId = priceId,
                                        ClientSecret = updatePaymentIntent?.ClientSecret,
                                        RequiresAction = updatePaymentIntent?.Status == "requires_action" || updatePaymentIntent?.Status == "requires_payment_method",
                                };
                        } catch (StripeException ex) {
                                logger.LogError(ex, "Failed to update Stripe subscription.");
                                return new CreateSubscriptionResponse {
                                        Success = false,
                                        Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                                };
                        }
                }

                try {
                        var subscription = await subscriptionService.CreateAsync(new SubscriptionCreateOptions {
                                Customer = customer.Id,
                                Items = new List<SubscriptionItemOptions> {
                                        new SubscriptionItemOptions {
                                                Price = priceId,
                                        },
                                },
                                PaymentBehavior = "default_incomplete",
                                PaymentSettings = new SubscriptionPaymentSettingsOptions {
                                        PaymentMethodTypes = new List<string> { "card" },
                                        SaveDefaultPaymentMethod = "on_subscription",
                                },
                                CollectionMethod = "charge_automatically",
                                Metadata = new Dictionary<string, string> {
                                        { "planKey", planDetails.PlanKey },
                                },
                                Expand = new List<string> { "latest_invoice.payment_intent" },
                        });

                        var latestInvoice = subscription.LatestInvoice as Invoice;
                        var paymentIntent = latestInvoice?.PaymentIntent;

                        if (paymentIntent == null || string.IsNullOrWhiteSpace(paymentIntent.ClientSecret)) {
                                logger.LogError("Subscription {SubscriptionId} created without payment intent.", subscription.Id);
                                return new CreateSubscriptionResponse {
                                        Success = false,
                                        Error = "Subscription created without payment intent.",
                                };
                        }

                        return new CreateSubscriptionResponse {
                                Success = true,
                                ClientSecret = paymentIntent.ClientSecret,
                                SubscriptionId = subscription.Id,
                                CustomerId = customer.Id,
                                PublishableKey = publishableKeyValue,
                                PlanKey = planDetails.PlanKey,
                                PriceId = priceId,
                                RequiresAction = paymentIntent.Status == "requires_action" || paymentIntent.Status == "requires_payment_method",
                        };
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to create Stripe subscription.");
                        return new CreateSubscriptionResponse {
                                Success = false,
                                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
                        };
                }
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
