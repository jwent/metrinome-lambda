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



// Program.cs
var builder = WebApplication.CreateBuilder(args);

var stripeSecretKey = builder.Configuration["ONTRACK_STRIPE_SECRET_KEY"];
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
                var statusCode = ex.HttpStatusCode != default ? (int)ex.HttpStatusCode : StatusCodes.Status400BadRequest;
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
// app.UseGraphQL("/graphql", config => {
//     // // require that the user be authenticated
//     config.AuthorizationRequired = false;

//     // require that the user be a member of at least one role listed
//     config.AuthorizedRoles.Add("Customer");
// });
app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

