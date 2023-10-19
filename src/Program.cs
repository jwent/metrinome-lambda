using GraphQL;
using GraphQL.Validation;
// using GraphQL.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;



// Program.cs
var builder = WebApplication.CreateBuilder(args);

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
// app.UseGraphQL("/graphql", config => {
//     // // require that the user be authenticated
//     config.AuthorizationRequired = false;

//     // require that the user be a member of at least one role listed
//     config.AuthorizedRoles.Add("Customer");
// });
app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

