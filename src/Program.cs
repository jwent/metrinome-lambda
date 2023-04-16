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

// builder.Services
//    .AddGraphQLServer()
//    .AddQueryType<Query>();

builder.Services
	.AddAWSLambdaHosting(LambdaEventSource.HttpApi)
	.AddCors(options => {
			options.AddPolicy("DefaultPolicy", builder => {
				builder.AllowAnyHeader()
						.WithMethods("GET", "POST")
						.WithOrigins("*");
			});
		});
// builder.Services
// 		.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
// 			Console.WriteLine("cookie auth?");
// 			o.Cookie.Name = "graphql-auth";
// 		});

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
			ValidAudience = "audience",
			ValidIssuer = "issuer",
			RequireSignedTokens = false,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY")))
        };

		options.RequireHttpsMetadata = false;
		options.SaveToken = true;
	});
builder.Services
	.AddAuthorization(policyBuilder => {
		policyBuilder.AddPolicy("CustomerPolicy", p => p.RequireClaim(ClaimTypes.Role, "Customer"));
	});
// builder.Services
// 	.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
// builder.Services
// 	.AddSingleton<IAuthorizationEvaluator, AuthorizationEvaluator>()
// 	.AddTransient<IValidationRule, AuthorizationValidationRule>()
// 	;



AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

// builder.Services.AddErrorInfoProvider(opt => opt.ExposeExceptionStackTrace = true);
builder.Services.AddDbContext<OnTrackDBContext>(options =>
    options.UseNpgsql(Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING")));

builder.Services
	.AddGraphQL(b => b
		.AddAutoSchema<Query>(s => s.WithMutation<Mutation>())
		.AddSystemTextJson()
		.AddAuthorizationRule()
		.AddUserContextBuilder(httpContext => new MyGraphQLUserContext(httpContext.User))
    .AddErrorInfoProvider((opts, serviceProvider) => {
        opts.ExposeExceptionStackTrace = true;
    }));

var app = builder.Build();

app.UseRouting();
app.UseGraphQLPlayground(
	"/",
	new GraphQL.Server.Ui.Playground.PlaygroundOptions {
		GraphQLEndPoint = "/graphql",
		SubscriptionsEndPoint = "/graphql",
	});

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

