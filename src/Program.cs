using GraphQL;
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// builder.Services
//    .AddGraphQLServer()
//    .AddQueryType<Query>();

builder.Services
  .AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddGraphQL(b => b
	.AddAutoSchema<Query>()  // schema
	.AddSystemTextJson())    // serializer
	.AddCors(options =>
			{
				options.AddPolicy("DefaultPolicy", builder =>
				{
					builder.AllowAnyHeader()
							.WithMethods("GET", "POST")
							.WithOrigins("*");
				});
			});

var app = builder.Build();

app.UseRouting();
app.UseGraphQLPlayground(
	"/",
	new GraphQL.Server.Ui.Playground.PlaygroundOptions {
		GraphQLEndPoint = "/graphql",
		SubscriptionsEndPoint = "/graphql",
	});

app.UseCors("DefaultPolicy");
app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

