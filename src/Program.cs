using GraphQL;
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// builder.Services
//   .AddGraphQLServer()
//   .AddQueryType<Query>();

builder.Services
  .AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddGraphQL(b => b
    .AddAutoSchema<Query>()  // schema
    .AddSystemTextJson());   // serializer

var app = builder.Build();

app.UseRouting();
app.UseGraphQLPlayground(
    "/",                               // url to host Playground at
    new GraphQL.Server.Ui.Playground.PlaygroundOptions {
        GraphQLEndPoint = "/graphql",         // url of GraphQL endpoint
        SubscriptionsEndPoint = "/graphql",   // url of GraphQL endpoint
    });

app.UseEndpoints(endpoints =>
  endpoints.MapGraphQL());

await app.RunAsync();

