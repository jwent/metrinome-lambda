using GraphQL;
using GraphQL.Validation;
// using GraphQL.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Stripe;
using System.Security.Claims;
using System.Text;
using System.Linq;
using System.Collections.Generic;



// Program.cs
var builder = WebApplication.CreateBuilder(args);

var stripeSecretKey = builder.Configuration["STRIPE_SECRET_KEY"];
if (string.IsNullOrWhiteSpace(stripeSecretKey)) {
        throw new InvalidOperationException("Stripe secret key is not configured.");
}
StripeConfiguration.ApiKey = stripeSecretKey.Trim();

builder.Services
	.AddAWSLambdaHosting(LambdaEventSource.HttpApi)
	.AddCors(options => {
			options.AddPolicy("DefaultPolicy", builder => {
				builder.AllowAnyHeader()
						.WithMethods("GET", "POST")
						.WithOrigins("*");
			});
		});

builder.Services
	.AddAuthentication(options => {
		options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
		options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
		options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
	})
	.AddJwtBearer(options => {
		options.TokenValidationParameters = new TokenValidationParameters {
			ValidateAudience = true,
			ValidateIssuer = true,
			ValidateIssuerSigningKey = true,
			ValidAudience = Environment.GetEnvironmentVariable("ONTRACK_SITE_URL"),
			ValidIssuer = Environment.GetEnvironmentVariable("ONTRACK_SITE_URL"),
			RequireSignedTokens = false,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Util.ValueOrDie(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY"))))
        };

		options.RequireHttpsMetadata = false;
		options.SaveToken = true;
	});
builder.Services
	.AddAuthorization(policyBuilder => {
		policyBuilder.AddPolicy("CustomerPolicy", p => p.RequireClaim(ClaimTypes.Role, "Customer"));
	});



AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

builder.Services.AddDbContext<OnTrackDBContext>(options =>
    options.UseNpgsql(Util.ValueOrDie(Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING"))));

builder.Services
        .AddGraphQL(b => b
		.AddAutoSchema<Query>(s => s.WithMutation<Mutation>())
		.ConfigureExecutionOptions(opts => {})
		.AddSystemTextJson()
		.AddAuthorizationRule()
		.AddUserContextBuilder(httpContext => new MyGraphQLUserContext(httpContext.User))
    .AddErrorInfoProvider((opts, serviceProvider) => {
    	// only expose stack traces if we are not in production
        opts.ExposeExceptionDetails = !Util.IsEnvironmentStage("PROD");
    }));

builder.Services.AddSingleton<PaymentIntentService>();
builder.Services.AddSingleton<CustomerService>();
builder.Services.AddSingleton<SubscriptionService>();
builder.Services.AddSingleton<PaymentMethodService>();

var app = builder.Build();

app.UseRouting();
// only enable playground if we are testing locally
if (Util.IsEnvironmentStage("LOCALTEST")) {
	app.UseGraphQLPlayground(
		"/",
		new GraphQL.Server.Ui.Playground.PlaygroundOptions {
			GraphQLEndPoint = "/graphql",
			SubscriptionsEndPoint = "/graphql",
		});
}

app.UseAuthentication();
app.UseAuthorization();
app.UseCors("DefaultPolicy");

app.MapPost("/create-payment-intent", async ([FromBody] CreatePaymentIntentRequest request,
        PaymentIntentService paymentIntentService,
        ILoggerFactory loggerFactory) => {
        var logger = loggerFactory.CreateLogger("CreatePaymentIntent");

        if (request == null) {
                return Results.BadRequest(new { error = "Request body is required." });
        }

        if (request.Amount <= 0) {
                return Results.BadRequest(new { error = "Amount must be greater than zero." });
        }

        if (string.IsNullOrWhiteSpace(request.Currency)) {
                return Results.BadRequest(new { error = "Currency is required." });
        }

        var currency = request.Currency.Trim();

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(request.Plan)) {
                metadata["plan"] = request.Plan.Trim();
        }

        var options = new PaymentIntentCreateOptions {
                Amount = request.Amount,
                Currency = currency.ToLowerInvariant(),
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions {
                        Enabled = true,
                },
                Metadata = metadata.Count > 0 ? metadata : null,
        };

        try {
                var paymentIntent = await paymentIntentService.CreateAsync(options);
                return Results.Ok(new { clientSecret = paymentIntent.ClientSecret });
        } catch (StripeException ex) {
                logger.LogError(ex, "Failed to create Stripe payment intent.");
                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                var errorMessage = ex.StripeError?.Message ?? "Stripe rejected the request.";
                return Results.Problem(
                        title: "Stripe error",
                        detail: errorMessage,
                        statusCode: statusCode);
        } catch (Exception ex) {
                logger.LogError(ex, "Unexpected error while creating payment intent.");
                return Results.Problem(
                        title: "Server error",
                        detail: "Unable to create payment intent.",
                        statusCode: StatusCodes.Status500InternalServerError);
        }
});

app.MapGet("/stripe/config", () => {
        var publishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");

        var plans = StripePlanConfiguration
                .GetAllPlans()
                .Select(plan => new {
                        plan.PlanKey,
                        plan.Name,
                        PriceId = plan.ResolvePriceIdFromEnvironment(),
                        plan.Currency,
                        plan.AmountCents,
                });

        return Results.Ok(new {
                publishableKey = string.IsNullOrWhiteSpace(publishableKey) ? null : publishableKey.Trim(),
                plans,
        });
});

app.MapPost("/create-subscription", async ([FromBody] CreateSubscriptionRequest request,
        CustomerService customerService,
        SubscriptionService subscriptionService,
        PaymentMethodService paymentMethodService,
        ILoggerFactory loggerFactory) => {
        var logger = loggerFactory.CreateLogger("CreateSubscription");

        if (request == null)
                return Results.BadRequest(new CreateSubscriptionResponse { Success=false, Error="Request body is required." });

        var planKey = request.PlanKey?.Trim();
        if (string.IsNullOrWhiteSpace(planKey))
                return Results.BadRequest(new CreateSubscriptionResponse { Success=false, Error="Plan is required." });

        if (!StripePlanConfiguration.TryGetPlanDetails(planKey, out var planDetails))
                return Results.BadRequest(new CreateSubscriptionResponse { Success=false, Error="Invalid plan." });

        var paymentMethodId = request.PaymentMethodId?.Trim();
        if (string.IsNullOrWhiteSpace(paymentMethodId))
                return Results.BadRequest(new CreateSubscriptionResponse { Success=false, Error="Payment method is required." });

        var customerEmail = request.CustomerEmail?.Trim();
        if (string.IsNullOrWhiteSpace(customerEmail))
                return Results.BadRequest(new CreateSubscriptionResponse { Success=false, Error="Customer email is required." });

        var priceId = planDetails.ResolvePriceIdFromEnvironment();
        if (string.IsNullOrWhiteSpace(priceId))
                return Results.Problem(title: "Configuration error", detail: "Stripe price id is not configured for the requested plan.", statusCode: StatusCodes.Status500InternalServerError);

        Customer? customer = null;
        try {
                var existingCustomers = await customerService.ListAsync(new CustomerListOptions {
                        Email = customerEmail,
                        Limit = 1,
                });
                customer = existingCustomers.Data.FirstOrDefault();
        } catch (StripeException ex) {
                logger.LogError(ex, "Failed to list Stripe customers.");
                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
        }

        if (customer == null) {
                try {
                        customer = await customerService.CreateAsync(new CustomerCreateOptions {
                                Email = customerEmail,
                                Name = string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName.Trim(),
                                Metadata = new Dictionary<string, string> {
                                        { "planKey", planDetails.PlanKey },
                                },
                        });
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to create Stripe customer.");
                        var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                        return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
                }
        } else if (!string.IsNullOrWhiteSpace(request.CustomerName)) {
                var trimmedName = request.CustomerName.Trim();
                if (!string.Equals(customer.Name, trimmedName, StringComparison.Ordinal)) {
                        try {
                                customer = await customerService.UpdateAsync(customer.Id, new CustomerUpdateOptions {
                                        Name = trimmedName,
                                });
                        } catch (StripeException ex) {
                                logger.LogError(ex, "Failed to update Stripe customer.");
                                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                                return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
                        }
                }
        }

        if (customer == null)
                return Results.Problem(title: "Stripe error", detail: "Unable to prepare customer for subscription.", statusCode: StatusCodes.Status502BadGateway);

        try {
                await paymentMethodService.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions {
                        Customer = customer.Id,
                });
        } catch (StripeException ex) when (ex.StripeError?.Code == "resource_already_exists") {
                logger.LogDebug("Payment method already attached to customer {CustomerId}.", customer.Id);
        } catch (StripeException ex) {
                logger.LogError(ex, "Failed to attach payment method to customer.");
                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
        }

        try {
                await customerService.UpdateAsync(customer.Id, new CustomerUpdateOptions {
                        InvoiceSettings = new CustomerInvoiceSettingsOptions {
                                DefaultPaymentMethod = paymentMethodId,
                        },
                });
        } catch (StripeException ex) {
                logger.LogError(ex, "Failed to set default payment method for customer.");
                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
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
                        return Results.Ok(new CreateSubscriptionResponse {
                                Success = true,
                                SubscriptionId = existingSubscription.Id,
                                CustomerId = customer.Id,
                                PublishableKey = publishableKeyValue,
                                PlanKey = planDetails.PlanKey,
                                PriceId = priceId,
                                AlreadySubscribed = true,
                        });
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

                        return Results.Ok(new CreateSubscriptionResponse {
                                Success = true,
                                SubscriptionId = updatedSubscription.Id,
                                CustomerId = customer.Id,
                                PublishableKey = publishableKeyValue,
                                PlanKey = planDetails.PlanKey,
                                PriceId = priceId,
                                ClientSecret = updatePaymentIntent?.ClientSecret,
                                RequiresAction = updatePaymentIntent?.Status == "requires_action" || updatePaymentIntent?.Status == "requires_payment_method",
                        });
                } catch (StripeException ex) {
                        logger.LogError(ex, "Failed to update Stripe subscription.");
                        var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                        return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
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
                        return Results.Problem(title: "Stripe error", detail: "Subscription created without payment intent.", statusCode: StatusCodes.Status502BadGateway);
                }

                return Results.Ok(new CreateSubscriptionResponse {
                        Success = true,
                        ClientSecret = paymentIntent.ClientSecret,
                        SubscriptionId = subscription.Id,
                        CustomerId = customer.Id,
                        PublishableKey = publishableKeyValue,
                        PlanKey = planDetails.PlanKey,
                        PriceId = priceId,
                        RequiresAction = paymentIntent.Status == "requires_action" || paymentIntent.Status == "requires_payment_method",
                });
        } catch (StripeException ex) {
                logger.LogError(ex, "Failed to create Stripe subscription.");
                var statusCode = ex.HttpStatusCode.HasValue ? (int)ex.HttpStatusCode.Value : StatusCodes.Status400BadRequest;
                return Results.Problem(title: "Stripe error", detail: ex.StripeError?.Message ?? "Stripe rejected the request.", statusCode: statusCode);
        }
});
// app.UseGraphQL("/graphql", config => {
//     // // require that the user be authenticated
//     config.AuthorizationRequired = false;

//     // require that the user be a member of at least one role listed
//     config.AuthorizedRoles.Add("Customer");
// });
app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

