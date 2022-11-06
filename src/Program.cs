using GraphQL;
using GraphQL.Validation;
// using GraphQL.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
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
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secretsecretsecret"))
		};

		options.RequireHttpsMetadata = false;
		options.SaveToken = true;
	});
builder.Services
	.AddAuthorization(policyBuilder => {
		Console.WriteLine("policy builder?");
		policyBuilder.AddPolicy("AdminPolicy", p => p.RequireClaim(ClaimTypes.Role, "Admin"));
	});
// builder.Services
// 	.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
// builder.Services
// 	.AddSingleton<IAuthorizationEvaluator, AuthorizationEvaluator>()
// 	.AddTransient<IValidationRule, AuthorizationValidationRule>()
// 	;
		
builder.Services.AddGraphQL(b => b
	.AddAutoSchema<Query>(s => s.WithMutation<Mutation>())
	.AddSystemTextJson()
	.AddAuthorizationRule());

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
//     config.AuthorizedRoles.Add("Admin");
// });
app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

