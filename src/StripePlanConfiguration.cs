using System.Collections.ObjectModel;

public class StripePlanDetails
{
        public string PlanKey { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string PriceEnvironmentVariable { get; init; } = string.Empty;
        public long AmountCents { get; init; }
        public string Currency { get; init; } = "usd";

        public string? ResolvePriceIdFromEnvironment()
        {
                var raw = Environment.GetEnvironmentVariable(PriceEnvironmentVariable);
                return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
}

public static class StripePlanConfiguration
{
        public const string BasicPlanKey = "basic_plan";
        public const string ProPlanKey = "pro_plan";

        private static readonly IReadOnlyDictionary<string, StripePlanDetails> Plans =
                new ReadOnlyDictionary<string, StripePlanDetails>(new Dictionary<string, StripePlanDetails>
                {
                        {
                                BasicPlanKey,
                                new StripePlanDetails
                                {
                                        PlanKey = BasicPlanKey,
                                        Name = "OnTrack Basic Plan ($299/mo)",
                                        PriceEnvironmentVariable = "STRIPE_BASIC_PLAN_ID",
                                        AmountCents = 29900,
                                        Currency = "usd",
                                }
                        },
                        {
                                ProPlanKey,
                                new StripePlanDetails
                                {
                                        PlanKey = ProPlanKey,
                                        Name = "OnTrack Pro Plan ($499/mo)",
                                        PriceEnvironmentVariable = "STRIPE_PRO_PLAN_ID",
                                        AmountCents = 49900,
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
