using System.Collections.ObjectModel;

public class StripePlanDetails
{
        public string PlanKey { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string PriceId { get; init; } = string.Empty;
        public long AmountCents { get; init; }
        public string Currency { get; init; } = "usd";

        public string? ResolvePriceIdFromEnvironment()
        {
                var raw = Environment.GetEnvironmentVariable(PriceId);
                return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
}

public static class StripePlanConfiguration
{
    // Plan key definitions
    public const string StarterMonthlyKey = "starter_monthly_plan";
    public const string StarterYearlyKey = "starter_yearly_plan";
    public const string AdvancedMonthlyKey = "advanced_monthly_plan";
    public const string AdvancedYearlyKey = "advanced_yearly_plan";

    private static readonly IReadOnlyDictionary<string, StripePlanDetails> Plans =
        new ReadOnlyDictionary<string, StripePlanDetails>(new Dictionary<string, StripePlanDetails>
        {
            {
                StarterMonthlyKey,
                new StripePlanDetails
                {
                    PlanKey = StarterMonthlyKey,
                    Name = "OnTrack Starter Plan ($299/mo)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_STARTER_MONTHLY_PRICE_ID")
                              ?? "price_1SACseCJP1EZuX5mwgIyhPi4",
                    AmountCents = 29900,
                    Currency = "usd",
                }
            },
            {
                StarterYearlyKey,
                new StripePlanDetails
                {
                    PlanKey = StarterYearlyKey,
                    Name = "OnTrack Starter Plan ($2,990/yr)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_STARTER_YEARLY_PRICE_ID")
                              ?? "price_1SLPHfCJP1EZuX5mr570QHOh",
                    AmountCents = 299000,
                    Currency = "usd",
                }
            },
            {
                AdvancedMonthlyKey,
                new StripePlanDetails
                {
                    PlanKey = AdvancedMonthlyKey,
                    Name = "OnTrack Advanced Plan ($499/mo)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_ADVANCED_MONTHLY_PRICE_ID")
                              ?? "price_1SACttCJP1EZuX5mz9qWK7e0",
                    AmountCents = 49900,
                    Currency = "usd",
                }
            },
            {
                AdvancedYearlyKey,
                new StripePlanDetails
                {
                    PlanKey = AdvancedYearlyKey,
                    Name = "OnTrack Advanced Plan ($4,990/yr)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_ADVANCED_YEARLY_PRICE_ID")
                              ?? "price_1SLPTSCJP1EZuX5mXr0QDNmw",
                    AmountCents = 490000,
                    Currency = "usd",
                }
            },
        });

        public static StripePlanDetails GetPlanDetails(string planKey)
        {
            if (string.IsNullOrWhiteSpace(planKey))
                throw new ArgumentException("Plan key cannot be null or whitespace.", nameof(planKey));

            if (!Plans.TryGetValue(planKey.Trim(), out var details))
                throw new KeyNotFoundException($"Unknown Stripe plan '{planKey}'.");

            Console.WriteLine($"[Stripe] Retrieved plan details for '{planKey}': " +
                              $"planKey={planKey}, Name={details.Name}, PriceId={details.PriceId}, Amount={details.AmountCents}");

            return details;
        }


    public static IReadOnlyCollection<StripePlanDetails> GetAllPlans() => (IReadOnlyCollection<StripePlanDetails>)Plans.Values;
}
