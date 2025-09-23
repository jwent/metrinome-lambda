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

        Assert.Contains("requestCheckout", fieldNames);
        Assert.Contains("setupSubscription", fieldNames);
    }
}
