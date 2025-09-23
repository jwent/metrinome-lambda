using System.Linq;
using GraphQL.Types;
using Xunit;

namespace OnTrackGraphQLServer.Tests;

public class MutationSchemaTests
{
    [Fact]
    public void Mutation_ExposesCheckoutMutations()
    {
        var mutationGraphType = new AutoRegisteringObjectGraphType<Mutation>();
        var fieldNames = mutationGraphType.Fields.Select(field => field.Name).ToList();

        Assert.Contains("setupSubscription", fieldNames);
        Assert.Contains("startSubscriptionCheckout", fieldNames);
        Assert.Contains("completeSubscription", fieldNames);
    }
}
