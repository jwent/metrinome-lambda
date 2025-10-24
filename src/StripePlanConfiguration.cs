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
                    Name = "OnTrack Starter Plan ($19/mo)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_STARTER_MONTHLY_PRICE_ID")
                              ?? "price_1SACseCJP1EZuX5mwgIyhPi4",
                    AmountCents = 1900,
                    Currency = "usd",
                }
            },
            {
                StarterYearlyKey,
                new StripePlanDetails
                {
                    PlanKey = StarterYearlyKey,
                    Name = "OnTrack Starter Plan ($190/yr)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_STARTER_YEARLY_PRICE_ID")
                              ?? "price_1SLPHfCJP1EZuX5mr570QHOh",
                    AmountCents = 19000,
                    Currency = "usd",
                }
            },
            {
                AdvancedMonthlyKey,
                new StripePlanDetails
                {
                    PlanKey = AdvancedMonthlyKey,
                    Name = "OnTrack Advanced Plan ($49/mo)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_ADVANCED_MONTHLY_PRICE_ID")
                              ?? "price_1SACttCJP1EZuX5mz9qWK7e0",
                    AmountCents = 4900,
                    Currency = "usd",
                }
            },
            {
                AdvancedYearlyKey,
                new StripePlanDetails
                {
                    PlanKey = AdvancedYearlyKey,
                    Name = "OnTrack Advanced Plan ($490/yr)",
                    PriceId = Environment.GetEnvironmentVariable("STRIPE_ADVANCED_YEARLY_PRICE_ID")
                              ?? "price_1SLPTSCJP1EZuX5mXr0QDNmw",
                    AmountCents = 49000,
                    Currency = "usd",
                }
            },
        });

    public static bool TryGetPlanDetails(string planKey, out StripePlanDetails details)
        {
                if (string.IsNullOrWhiteSpace(planKey))
                {
                        details = default!;
                        return false;
                }

                return Plans.TryGetValue(planKey.Trim(), out details!);
        }

        public static StripePlanDetails GetPlanDetails(string planKey)
        {
                if (!TryGetPlanDetails(planKey, out var details))
                        throw new KeyNotFoundException($"Unknown Stripe plan '{planKey}'.");
                return details;
        }

        public static IReadOnlyCollection<StripePlanDetails> GetAllPlans() => (IReadOnlyCollection<StripePlanDetails>)Plans.Values;
}
