using Amazon.Runtime.Internal.Util;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using GraphQL;
using GraphQL.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.TestHelpers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using StripeCustomerService = Stripe.CustomerService;

public class Mutation {
    private const string StarterMonthlyPlanKey  = StripePlanConfiguration.StarterMonthlyKey;
    private const string StarterYearlyPlanKey   = StripePlanConfiguration.StarterYearlyKey;
    private const string AdvancedMonthlyPlanKey = StripePlanConfiguration.AdvancedMonthlyKey;
    private const string AdvancedYearlyPlanKey  = StripePlanConfiguration.AdvancedYearlyKey;

    //[Authorize(Policy = "AdminPolicy")] // optional; adjust to CustomerPolicy if you prefer
    public static async Task<SuccessResponse> runTrialExpirationTest([FromServices] OnTrackDBContext onTrackDBContext)
    {
        try
        {
            Console.WriteLine("[i] Running trial expiration check via GraphQL mutation...");

            // Call the existing routine from EmailController
            await EmailController.CheckTrialExpirationsAsync(onTrackDBContext);

            Console.WriteLine("[✅] Trial expiration test complete via GraphQL.");
            return new SuccessResponse { Success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error running trial expiration check: {ex}");
            return new SuccessResponse { Success = false };
        }
    }

    public static async Task<bool> SendEmail(string to, string subject, string body)
    {
        var ses = new AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.USEast1);
        var request = new SendEmailRequest
        {
            Source = "noreply@noreply@metrinome.io",
            Destination = new Destination { ToAddresses = new List<string> { to } },
            Message = new Message
            {
                Subject = new Content(subject),
                Body = new Body { Html = new Content(body) }
            }
        };
        await ses.SendEmailAsync(request);
        return true;
    }

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
        if (!user.UserState.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            !user.UserState.Equals("subscribed", StringComparison.OrdinalIgnoreCase) &&
            !user.UserState.Equals("MagicLinkUser", StringComparison.OrdinalIgnoreCase) &&
            !user.UserState.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return new LoginUserResponse { Error = "Account disabled." };
        }

            // return token
            return new LoginUserResponse { BearerToken=Util.SignAuthToken(user) };
    }

    public static async Task<CheckMagicLinkResult> checkMagicLink(
        IResolveFieldContext context,
        [FromServices] OnTrackDBContext db,
        string email,
        string magicLink)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(magicLink))
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "Email and magic link are required",
                BearerToken = null
            };
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        if (user == null)
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "User not found",
                BearerToken = null
            };
        }

        if (string.IsNullOrWhiteSpace(user.MagicLink))
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "User does not have a magic link",
                BearerToken = null
            };
        }

        var providedToken = ExtractMagicLinkToken(magicLink);
        if (string.IsNullOrWhiteSpace(providedToken))
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "Magic link token is missing",
                BearerToken = null
            };
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var payload = $"{normalizedEmail}:{user.MagicLink}";
        var expectedToken = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))
        );

        if (!string.Equals(providedToken, expectedToken, StringComparison.OrdinalIgnoreCase))
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "Magic link is invalid",
                BearerToken = null
            };
        }

        if (user.UserState == "MagicLinkUser" && user.CreatedAt >= DateTime.UtcNow.AddDays(-1)
)       {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = true,
                IsExpired = false,
                Error = null,
                BearerToken = Util.SignAuthToken(user)
            };
        }
        else
        {
            return new CheckMagicLinkResult
            {
                HasValidMagicLink = false,
                IsExpired = true,
                Error = "User does not have magic link access",
                BearerToken = null
            };
        }
    }

    private static string ExtractMagicLinkToken(string magicLink)
    {
        var trimmed = magicLink?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        // Try full URL parsing first
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            // 1) Query string (?token=abc)
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.TryGetValue("token", out var queryToken) &&
                !string.IsNullOrWhiteSpace(queryToken))
            {
                return queryToken.ToString();
            }

            // 2) Fragment (#abc OR #token=abc OR #uuid=abc)
            var fragment = uri.Fragment; // includes leading #
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                var frag = fragment.StartsWith("#", StringComparison.Ordinal)
                    ? fragment.Substring(1)
                    : fragment;

                // If fragment contains key/value pairs, parse them
                if (frag.Contains("=", StringComparison.Ordinal))
                {
                    var fragParams = QueryHelpers.ParseQuery("?" + frag);

                    if (fragParams.TryGetValue("token", out var fragToken) &&
                        !string.IsNullOrWhiteSpace(fragToken))
                    {
                        return fragToken.ToString();
                    }

                    if (fragParams.TryGetValue("uuid", out var fragUuid) &&
                        !string.IsNullOrWhiteSpace(fragUuid))
                    {
                        return fragUuid.ToString();
                    }
                }

                // Otherwise fragment *is* the token
                return frag;
            }
        }

        // 3) Fallback: string-based parsing
        var tokenIndex = trimmed.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (tokenIndex >= 0)
        {
            var start = tokenIndex + "token=".Length;

            // Handle token=#abc
            if (start < trimmed.Length && trimmed[start] == '#')
                start++;

            var end = trimmed.IndexOf('&', start);
            return end >= 0
                ? trimmed.Substring(start, end - start)
                : trimmed.Substring(start);
        }

        // 4) Last resort: raw token or "#token"
        return trimmed.StartsWith("#", StringComparison.Ordinal)
            ? trimmed.Substring(1)
            : trimmed;
    }


    public static async Task<LoginUserResponse> loginUserWithGoogle([FromServices] OnTrackDBContext onTrackDBContext, string googleIdToken)
    {
        var googleClientId = Environment.GetEnvironmentVariable("ONTRACK_GOOGLE_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(googleClientId))
        {
            return new LoginUserResponse { Error = "Google login is not configured." };
        }

        GoogleJsonWebSignature.Payload payload;

        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                googleIdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { googleClientId }
                });
        }
        catch (Exception e)
        {
            Console.WriteLine($"[!] Google token validation failed: {e.Message}");
            return new LoginUserResponse { Error = "Invalid Google token." };
        }

        if (payload.EmailVerified != true)
        {
            return new LoginUserResponse { Error = "Google account email is not verified." };
        }

        var email = payload.Email?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return new LoginUserResponse { Error = "Google account is missing an email address." };
        }

        var user = await onTrackDBContext.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                Password = string.Empty,
                CreatedAt = DateTime.Now,
                ResetPasswordToken = string.Empty,
                UserState = "Active",
            };

            var newOrganization = new UserOrganization
            {
                Id = Guid.NewGuid(),
                CreatorId = newUser.Id,
                CreatedAt = DateTime.Now,
                SubscriptionPlan = null,
            };

            newUser.Organization = newOrganization;

            var newRole = new UserOrganizationalRoleAssociation
            {
                Id = Guid.NewGuid(),
                OrganizationUser = newUser,
                Organization = newOrganization,
                RoleName = "Owner",
            };

            try
            {
                onTrackDBContext.Users.Add(newUser);
                onTrackDBContext.UserOrganizations.Add(newOrganization);
                onTrackDBContext.UserOrganizationalRoleAssociations.Add(newRole);
                onTrackDBContext.SaveChanges();
            }
            catch (DbUpdateException e)
            {
                Console.WriteLine($"[!] Failed to create Google user: {e.Message}");
                onTrackDBContext.Users.Remove(newUser);
                onTrackDBContext.UserOrganizations.Remove(newOrganization);
                onTrackDBContext.UserOrganizationalRoleAssociations.Remove(newRole);
                return new LoginUserResponse { Error = "Invalid or duplicate email." };
            }

            var tracker = new UserTracker { Id = Guid.NewGuid(), Organization = newOrganization, CreatedAt = DateTime.Now };
            onTrackDBContext.UserTrackers.Add(tracker);

            if (!string.IsNullOrWhiteSpace(payload.Name))
            {
                onTrackDBContext.UserExtraProperties.Add(new UserExtraProperty
                {
                    Id = Guid.NewGuid(),
                    Parent = newUser,
                    PropertyKey = "FullName",
                    PropertyValue = payload.Name,
                });
            }

            onTrackDBContext.SaveChanges();
            user = newUser;
        }

        if (user.UserState == "Invited")
        {
            user.UserState = "Active";
            user.ResetPasswordToken = string.Empty;
            onTrackDBContext.SaveChanges();
        }

        if (!user.UserState.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            !user.UserState.Equals("subscribed", StringComparison.OrdinalIgnoreCase) &&
            !user.UserState.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return new LoginUserResponse { Error = "Account disabled." };
        }

        return new LoginUserResponse { BearerToken = Util.SignAuthToken(user) };
    }

    public static async Task<SuccessResponse> forgotPassword(
        [FromServices] OnTrackDBContext onTrackDBContext,
        string email)
    {
        Console.WriteLine($"[DB INFO] Server: {onTrackDBContext.Database.GetDbConnection().DataSource}");
        var conn = onTrackDBContext.Database.GetDbConnection();
        Console.WriteLine($"[DB DEBUG] Provider={onTrackDBContext.Database.ProviderName ?? "(none)"}");
        Console.WriteLine($"[DB DEBUG] DataSource='{conn.DataSource}', Database='{conn.Database}'");
        Console.WriteLine($"[DB INFO] ConnectionString={conn.ConnectionString}");
        Console.WriteLine($"[DB INFO] State={conn.State}");
        conn.Open();
        Console.WriteLine($"[DB INFO] Host after open: {((Npgsql.NpgsqlConnection)conn).Host}");



        // find a user by email
        var query = onTrackDBContext.Users
            .Where(u => u.Email == email);

        // Print the SQL EF Core will generate
        Console.WriteLine("[SQL] " + query.ToQueryString());

        // Execute it
        var user = query.FirstOrDefault();

        if (user == null)
        {
            Console.WriteLine("[!] user not found");
            // Return success even if not found (avoid exposing which emails exist)
            return new SuccessResponse { Success = true };
        }

        // generate a random token so that they can reset their password
        var randomResetToken = Util.GetSecureRandomString(64); // 256 bits of security
        user.ResetPasswordToken = randomResetToken;
        onTrackDBContext.SaveChanges();

        // --- EMAIL SETUP ---
        var resetUrl = $"https://app.metrinome.io/actualresetpassword?token={randomResetToken}";
        var sender = "noreply@metrinome.io"; // must be verified in SES
        var subject = "Reset your Metrinome Analytics password";
        var bodyHtml = $@"
            <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2>Password Reset Requested</h2>
                    <p>Hello {user.Email ?? "there"},</p>
                    <p>We received a request to reset your password for your OnTrack Analytics account.</p>
                    <p>
                        Click the link below to reset it:<br/>
                        <a href='{resetUrl}' style='color:#2563EB; font-weight:bold;'>{resetUrl}</a>
                    </p>
                    <p>If you didn’t request this, you can safely ignore this email.</p>
                    <br/>
                    <p>— The OnTrack Team</p>
                </body>
            </html>";

        var bodyText = $"Reset your password using this link: {resetUrl}";

        // --- SEND EMAIL VIA SES ---
        try
        {
            using var sesClient = new AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.USEast1);

            var sendRequest = new SendEmailRequest
            {
                Source = sender,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { email }
                },
                Message = new Message
                {
                    Subject = new Content(subject),
                    Body = new Body
                    {
                        Html = new Content(bodyHtml),
                        Text = new Content(bodyText)
                    }
                }
            };

            var response = await sesClient.SendEmailAsync(sendRequest);
            Console.WriteLine($"[+] Forgot password email sent to {email}, MessageId: {response.MessageId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Failed to send forgot-password email: {ex.Message}");
            // You might log this in CloudWatch but still return success for security
        }

        return new SuccessResponse { Success = true };
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

    [Authorize(Policy = "CustomerPolicy")]
    public static Task<CreatePaymentIntentResponse> setupSubscription(
            IResolveFieldContext context,
            [FromServices] OnTrackDBContext onTrackDBContext,
            [FromServices] PaymentIntentService paymentIntentService,
            [FromServices] StripeCustomerService customerService,
            [FromServices] ILogger<Mutation> logger,
            string plan)
    {
        Console.WriteLine($"[+] Starting subscription checkout for plan: {plan} for user: {UserController.GetCurrentUserId(context)}");
        return startSubscriptionCheckout(context, onTrackDBContext, paymentIntentService, customerService, logger, plan);

    }

    [Authorize(Policy = "CustomerPolicy")]
    public static async Task<CreatePaymentIntentResponse> startSubscriptionCheckout(
		IResolveFieldContext context, 
		[FromServices] OnTrackDBContext onTrackDBContext,
        [FromServices] PaymentIntentService paymentIntentService,
        [FromServices] StripeCustomerService customerService,
        [FromServices] ILogger<Mutation> logger,
        string plan) 
	{
        var userId = UserController.GetCurrentUserId(context);
		var user = onTrackDBContext.Users
                .Include(u => u.ExtraProperties)
                .Include(u => u.Organization.SubscriptionPlan)
				.First(u => u.Id == userId);

        var customerEmail = user.Email?.Trim();
        var fullName = user.ExtraProperties
            .FirstOrDefault(p => p.PropertyKey == "FullName")
            ?.PropertyValue?.Trim();

        if (string.IsNullOrWhiteSpace(plan))
			return new CreatePaymentIntentResponse { Success = false, Error = "missing plan" };

		plan = plan.Trim();

        IReadOnlyDictionary<string, StripePlanDetails> SubscriptionPlanOptions =
			StripePlanConfiguration.GetAllPlans().ToDictionary(plan => plan.PlanKey);

		// check that the plan is valid
		if (!SubscriptionPlanOptions.ContainsKey(plan))
			return new CreatePaymentIntentResponse { Success = false, Error = "invalid plan" };

		OrganizationalSubscriptionPlan? selectedPlan;
		try {
		    selectedPlan = UserController.GetSubscriptionPlanByKey(onTrackDBContext, plan);
		} 
        catch (InvalidOperationException) {
		    return new CreatePaymentIntentResponse { Success = false, Error = "plan unavailable" };
		}

		user.Organization.SubscriptionPlan = selectedPlan;
		onTrackDBContext.SaveChanges();

        var stripeCustomer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Email = customerEmail,   // 👈 critical: this is what shows in Stripe dashboard
            Name = fullName,
            Metadata = new Dictionary<string, string>
            {
                { "ontrack_user_id", user.Id.ToString() }
                // you can also add plan info etc. here if useful
            }
        });


        var planDetails = SubscriptionPlanOptions[plan];

		var options = new PaymentIntentCreateOptions
		{
			Amount = planDetails.AmountCents,
			Currency = planDetails.Currency,
            ReceiptEmail = customerEmail,
            Customer = stripeCustomer.Id,

            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
			{
				Enabled = true,
			},
			Metadata = new Dictionary<string, string>
		    {
			    { "planKey", planDetails.PlanKey },
			    { "userId", user.Id.ToString() }
		    }
    };

    try
    {
        var paymentIntent = await paymentIntentService.CreateAsync(options);

        return new CreatePaymentIntentResponse
        {
            Success = true,
            ClientSecret = paymentIntent.ClientSecret,
        };
    }
    catch (StripeException ex)
    {
        logger.LogError(ex, "Failed to create payment intent for subscription.");
        return new CreatePaymentIntentResponse { Success = false, Error = ex.StripeError?.Message ?? "Stripe error" };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error during subscription checkout.");
        return new CreatePaymentIntentResponse { Success = false, Error = "Unexpected error" };
    }
}

    [Authorize(Policy = "CustomerPolicy")]
        public static SuccessResponse checkPayment(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext) {
                var userId = UserController.GetCurrentUserId(context);
                var user = onTrackDBContext.Users
                        .Include(u => u.Organization.SubscriptionPlan)
                        .First(u => u.Id == userId);

                user.Organization.SubscriptionPlan = user.Organization.SubscriptionPlan ?? UserController.GetSubscriptionPlanByKey(onTrackDBContext, StarterMonthlyPlanKey);
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

    [Authorize(Policy = "CustomerPolicy")]
    public static async Task<CreateSubscriptionResponse> completeSubscription(
        IResolveFieldContext context,
        [FromServices] StripeCustomerService customerService,
        [FromServices] SubscriptionService subscriptionService,
        [FromServices] PaymentMethodService paymentMethodService,
        [FromServices] ILogger<Mutation> logger,
        [FromServices] OnTrackDBContext onTrackDBContext,
        string clientSecret,
        string planKey,
        string paymentMethodId,
        string priceId)
    {
        var userId = UserController.GetCurrentUserId(context);
        var user = onTrackDBContext.Users
            .Include(u => u.ExtraProperties)
            .First(u => u.Id == userId);

        var fullName = user.ExtraProperties
            .FirstOrDefault(p => p.PropertyKey == "FullName")?.PropertyValue;
        var customerEmail = user.Email;
        var customerName = fullName;

        if (string.IsNullOrWhiteSpace(planKey))
            return new CreateSubscriptionResponse { Success = false, Error = "Plan is required." };

        var trimmedPlanKey = planKey.Trim();
        var planDetails = StripePlanConfiguration.GetPlanDetails(trimmedPlanKey);

        if (planDetails == null)
            return new CreateSubscriptionResponse { Success = false, Error = "Invalid plan." };

        if (string.IsNullOrWhiteSpace(paymentMethodId))
            return new CreateSubscriptionResponse { Success = false, Error = "Payment method is required." };

        var trimmedPaymentMethodId = paymentMethodId.Trim();
        var trimmedCustomerEmail = customerEmail?.Trim() ?? "";

        // ✅ Determine trial period length
        int? trialDays = null;
        if (trimmedPlanKey == StripePlanConfiguration.StarterYearlyKey ||
            trimmedPlanKey == StripePlanConfiguration.AdvancedYearlyKey)
        {
            trialDays = 14; // 14 days free trial for yearly plans
        }
        else
        {
            trialDays = 7; // 7 days free trial for monthly plans
        }

            // 🔹 Fetch or create Stripe customer (unchanged)
            Customer? customer = null;
        try
        {
            var existingCustomers = await customerService.ListAsync(new CustomerListOptions
            {
                Email = trimmedCustomerEmail,
                Limit = 1,
            });
            customer = existingCustomers.Data.FirstOrDefault();
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Failed to list Stripe customers.");
            return new CreateSubscriptionResponse { Success = false, Error = ex.StripeError?.Message ?? "Stripe rejected the request." };
        }

        if (customer == null)
        {
            try
            {
                customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = trimmedCustomerEmail,
                    Name = customerName?.Trim(),
                    Metadata = new Dictionary<string, string> { { "planKey", planDetails.PlanKey } },
                });
            }
            catch (StripeException ex)
            {
                logger.LogError(ex, "Failed to create Stripe customer.");
                return new CreateSubscriptionResponse { Success = false, Error = ex.StripeError?.Message ?? "Stripe rejected the request." };
            }
        }

        // 🔹 Attach payment method (unchanged)
        try
        {
            await paymentMethodService.AttachAsync(trimmedPaymentMethodId, new PaymentMethodAttachOptions
            {
                Customer = customer.Id,
            });
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_already_exists")
        {
            logger.LogDebug("Payment method already attached to customer {CustomerId}.", customer.Id);
        }

        // 2️⃣ Attach the payment method the user entered
        await paymentMethodService.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
        {
            Customer = customer.Id,
        });

        await customerService.UpdateAsync(customer.Id, new CustomerUpdateOptions
        {
            Email = trimmedCustomerEmail,
            Name = customerName?.Trim(),
            InvoiceSettings = new CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId
            }
        });

        // 🔹 Create subscription with con1ditional trial
        try
        {
            var subscriptionOptions = new SubscriptionCreateOptions
            {
                Customer = customer.Id,
                DefaultPaymentMethod = paymentMethodId,
                TrialPeriodDays = trialDays,
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions { Price = priceId },
                },
                PaymentBehavior = "default_incomplete",
                PaymentSettings = new SubscriptionPaymentSettingsOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    SaveDefaultPaymentMethod = "on_subscription",
                },
                CollectionMethod = "charge_automatically",
                Metadata = new Dictionary<string, string>
                {
                    { "planKey", planDetails.PlanKey },
                },
                Expand = new List<string> { "latest_invoice.payment_intent" },
            };

            var subscription = await subscriptionService.CreateAsync(subscriptionOptions);

            var latestInvoice = subscription.LatestInvoice as Invoice;
            var paymentIntent = latestInvoice?.PaymentIntent;
            var publishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY")?.Trim();

            if (string.IsNullOrWhiteSpace(user.StripeCustomerId))
            {
                user.StripeCustomerId = customer.Id;
                user.UserState = "subscribed";
                onTrackDBContext.SaveChanges();
            }

            if (paymentIntent == null || string.IsNullOrWhiteSpace(paymentIntent.ClientSecret))
            {
                logger.LogDebug("Subscription {SubscriptionId} created without payment intent.", subscription.Id);
                
                return new CreateSubscriptionResponse
                {
                    Success = true,
                    SubscriptionId = subscription.Id,
                    CustomerId = customer.Id,
                    PublishableKey = publishableKey,
                    PlanKey = planDetails.PlanKey,
                };
            }

            return new CreateSubscriptionResponse
            {
                Success = true,
                ClientSecret = paymentIntent.ClientSecret,
                SubscriptionId = subscription.Id,
                CustomerId = customer.Id,
                PublishableKey = publishableKey,
                PlanKey = planDetails.PlanKey,
                RequiresAction = paymentIntent.Status == "requires_action" ||
                                 paymentIntent.Status == "requires_payment_method",
            };
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Failed to create Stripe subscription.");
            return new CreateSubscriptionResponse
            {
                Success = false,
                Error = ex.StripeError?.Message ?? "Stripe rejected the request.",
            };
        }
    }

    [Authorize(Policy = "CustomerPolicy")]
    public static async Task<SuccessOrErrorResponse> cancelSubscription(
        [FromServices] OnTrackDBContext onTrackDBContext,
        [FromServices] SubscriptionService subscriptionService,
        [FromServices] StripeCustomerService customerService,
        IResolveFieldContext context)
    {
        try
        {
            var userId = UserController.GetCurrentUserId(context);
            var user = onTrackDBContext.Users
                    .Include(u => u.ExtraProperties)
                    .Include(u => u.Organization.SubscriptionPlan)
                    .First(u => u.Id == userId);

            var email = user.Email?.Trim();
            var fullName = user.ExtraProperties
                .FirstOrDefault(p => p.PropertyKey == "FullName")
                ?.PropertyValue?.Trim();

            var claimsPrincipal = user;

            if (string.IsNullOrWhiteSpace(email))
            {
                Console.WriteLine("[cancelSubscription] Missing email claim.");
                return new SuccessOrErrorResponse { Success = false, Error = "User not authenticated." };
            }

            // 🔹 Lookup DB user
            var dbUser = await onTrackDBContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.UserState == "Active");

            if (dbUser == null)
            {
                Console.WriteLine($"[cancelSubscription] No active user found for {email}");
                return new SuccessOrErrorResponse { Success = false, Error = "User not found." };
            }

            if (string.IsNullOrWhiteSpace(dbUser.StripeCustomerId))
            {
                Console.WriteLine($"[cancelSubscription] User {email} has no StripeCustomerId");
                return new SuccessOrErrorResponse { Success = false, Error = "No Stripe customer ID found." };
            }

            // 🔹 Get active subscription from Stripe
            var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
            {
                Customer = dbUser.StripeCustomerId,
                Status = "active",
                Limit = 1
            });

            if (subscriptions.Data.Count == 0)
            {
                Console.WriteLine($"[cancelSubscription] No active subscriptions found for {email}");
                return new SuccessOrErrorResponse { Success = false, Error = "No active subscriptions found." };
            }

            var subscriptionId = subscriptions.Data.First().Id;

            // 🔹 Cancel subscription
            var canceled = await subscriptionService.CancelAsync(subscriptionId);
            Console.WriteLine($"[cancelSubscription] Subscription {subscriptionId} canceled for {email}");

            // 🔹 Update DB if necessary
            var org = await onTrackDBContext.UserOrganizations
                .FirstOrDefaultAsync(o => o.Id == dbUser.OrganizationId);

            if (org != null)
            {
                org.SubscriptionPlan = null;
                await onTrackDBContext.SaveChangesAsync();
            }

            return new SuccessOrErrorResponse { Success = true };
        }
        catch (StripeException ex)
        {
            Console.WriteLine($"[cancelSubscription] Stripe error: {ex.Message}");
            return new SuccessOrErrorResponse { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[cancelSubscription] Unexpected error: {ex.Message}");
            return new SuccessOrErrorResponse { Success = false, Error = "An unexpected error occurred." };
        }
    }

    public static async Task<AddUserResponse> addUser([FromServices] OnTrackDBContext onTrackDBContext, string fullname, string email, string password, Boolean canUseMagicLink)
    {
        // find a user by email
        var possibleUser = onTrackDBContext.Users
                .Where(u => u.Email == email)
                .FirstOrDefault();
        if (possibleUser != null)
        {
            Console.WriteLine("duplicate user!");
            return new AddUserResponse { Error = "Invalid or duplicate email." };
        }

        // assert stuff
        if (fullname.Length > 128)
            return new AddUserResponse { Error = "Invalid fullname." };
        if (email.Length > 128)
            return new AddUserResponse { Error = "Invalid or duplicate email." };
        var passwordError = UserController.ValidatePasswordCreation(password);
        if (passwordError != null)
            return new AddUserResponse { Error = passwordError };

        // salt and hash the new password
        var passwordHash = Util.SaltAndHash(password);

        // generate a random token so that they can register
        var randomResetToken = Util.GetSecureRandomString(64); // 256 bits of security
                                                               // create both the user, their organization, and the role between them
        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Password = passwordHash,
            CreatedAt = DateTime.UtcNow,
            ResetPasswordToken = randomResetToken,
            UserState = "Invited",
            MagicLink = ""
        };

        if (canUseMagicLink)
        {
            newUser.MagicLink = Guid.NewGuid().ToString();
            newUser.UserState = "MagicLinkUser";
        }

        var baseUrl = Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")
            ?? throw new InvalidOperationException("ONTRACK_SITE_URL is not configured");

        var magicLinkUrl = Util.CreateMagicLinkUrl(email, newUser.MagicLink, baseUrl);

        var newOrganization = new UserOrganization
        {
            Id = Guid.NewGuid(),
            CreatorId = newUser.Id,
            CreatedAt = DateTime.Now,
            SubscriptionPlan = null,
        };
        newUser.Organization = newOrganization;
        var newRole = new UserOrganizationalRoleAssociation
        {
            Id = Guid.NewGuid(),
            OrganizationUser = newUser,
            Organization = newOrganization,
            RoleName = "Owner",
        };


        try
        {
            // add the new user
            onTrackDBContext.Users.Add(newUser);
            // add the new organization
            onTrackDBContext.UserOrganizations.Add(newOrganization);
            onTrackDBContext.UserOrganizationalRoleAssociations.Add(newRole);
            // try to commit the user
            onTrackDBContext.SaveChanges();

        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException e)
        {
            Console.WriteLine("duplicate user key error:" + e.ToString());
            onTrackDBContext.Users.Remove(newUser);
            onTrackDBContext.UserOrganizations.Remove(newOrganization);
            onTrackDBContext.UserOrganizationalRoleAssociations.Remove(newRole);
            return new AddUserResponse { Error = "Invalid or duplicate email." };
        }

        // add the user's tracker immediately
        var tracker = new UserTracker { Id = Guid.NewGuid(), Organization = newOrganization, CreatedAt = DateTime.Now };
        onTrackDBContext.UserTrackers.Add(tracker);

        // add a meta property for the user's fullname
        onTrackDBContext.UserExtraProperties.Add(new UserExtraProperty
        {
            Id = Guid.NewGuid(),
            Parent = newUser,
            PropertyKey = "FullName",
            PropertyValue = fullname,
        });
        onTrackDBContext.SaveChanges();

        // email the user to get started
        if (!canUseMagicLink)
        {
            await EmailController.SendEmail(email, "Please verify your Metrinome Analytics account",
                $"Please follow <a href='{Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")}VerifyMainUserEmail?resetkey={randomResetToken}'>this link</a> to verify your account and use the platform.");
        }
        else {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var payload = $"{normalizedEmail}:{newUser.MagicLink}";

            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload))
            );

            // URL-encode email for safety
            var encodedEmail = Uri.EscapeDataString(normalizedEmail);

            await EmailController.SendEmail(email, "Your Metrinome Analytics magic link",
                $"Please follow <a href='{Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")}invite?email={encodedEmail}&token=#{hash}'>this link</a> to log in to your account and use the platform.");
        }

        // return is pointless
        return new AddUserResponse { Id = newUser.Id, MagicLinkUrl = magicLinkUrl };
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

        var baseUrl = Environment.GetEnvironmentVariable("ONTRACK_SITE_URL")?.TrimEnd('/');

        var body = $"Please follow <a href='{baseUrl}/signuporguser/?resetkey={randomResetToken}'>this link</a> to register your account and join the organization.";

        await EmailController.SendEmail(
            email,
            "You've been invited to an OnTrack Analytics organization",
            body
        );

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

    public static LoginUserResponse verifyUserToken(IResolveFieldContext context, [FromServices] OnTrackDBContext onTrackDBContext, string resetToken)
    {
        // assert that the resetToken is not invalid
        if (resetToken.Length < 8)
            return new LoginUserResponse { Error = "Invalid or missing user." };

        // find a user by email
        var user = onTrackDBContext.Users
                .Where(u => u.ResetPasswordToken == resetToken)
                .Include(u => u.Organization)
                .FirstOrDefault();

        if (user == null)
        {
            Console.WriteLine("[!] new user not found");
            return new LoginUserResponse { Error = "Invalid or missing user." };
        }

        user.ResetPasswordToken = ""; // clear the reset token to prevent reuse

        // save the properties
        onTrackDBContext.SaveChanges();

        // return token
        return new LoginUserResponse { BearerToken = Util.SignAuthToken(user) };
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

    [Authorize(Policy = "CustomerPolicy")]
    public static async Task<SuccessOrErrorResponse> changeSubscription(
        IResolveFieldContext context,
        [FromServices] OnTrackDBContext onTrackDBContext,
        [FromServices] SubscriptionService subscriptionService,
        string planKey)
    {
        try
        {
            // Get user email from JWT claims
            var userId = UserController.GetCurrentUserId(context);
            var user = onTrackDBContext.Users
                    .Include(u => u.ExtraProperties)
                    .Include(u => u.Organization.SubscriptionPlan)
                    .First(u => u.Id == userId);

            var email = user.Email?.Trim();

            if (string.IsNullOrEmpty(email))
                return new SuccessOrErrorResponse { Success = false, Error = "User not authenticated." };

            // Find user in DB
            var dbUser = onTrackDBContext.Users.FirstOrDefault(u => u.Email == email);
            if (dbUser == null || string.IsNullOrEmpty(dbUser.StripeCustomerId))
                return new SuccessOrErrorResponse { Success = false, Error = "Stripe customer not found." };

            // Lookup the Stripe plan
            var plan = await onTrackDBContext.OrganizationalSubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.PlanKey == planKey);
            if (plan == null)
                return new SuccessOrErrorResponse { Success = false, Error = $"Unknown plan '{planKey}'." };

            // Retrieve active subscription
            var subs = await subscriptionService.ListAsync(new SubscriptionListOptions
            {
                Customer = dbUser.StripeCustomerId,
                Status = "active",
                Limit = 1
            });

            var currentSub = subs.Data.FirstOrDefault();
            if (currentSub == null)
                return new SuccessOrErrorResponse { Success = false, Error = "No active subscription found." };

            var planDetails = StripePlanConfiguration.GetPlanDetails(planKey);
            // Update subscription
            var updateOptions = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = false, // apply immediately
                ProrationBehavior = "create_prorations",
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = currentSub.Items.Data[0].Id,
                        Plan = planDetails.PriceId,
                    }
                }
            };

            var updatedSub = await subscriptionService.UpdateAsync(currentSub.Id, updateOptions);
            Console.WriteLine($"[Stripe] Updated subscription {updatedSub.Id} → plan {plan.PlanKey}");

            //Update subscription status in db

            return new SuccessOrErrorResponse { Success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Failed to change subscription: {ex.Message}");
            return new SuccessOrErrorResponse { Success = false, Error = ex.Message };
        }
    }

}
