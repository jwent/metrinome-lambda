using GraphQL;
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
builder.Services.AddGraphQL(b => b
	.AddAutoSchema<Query>(s => s.WithMutation<Mutation>())  // schema
	.AddSystemTextJson());    // serializer

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

