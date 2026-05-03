using System.Collections.ObjectModel;

public class SubscriptionPlanDefinition
{
	public Guid Id { get; init; }
	public string PlanKey { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public int UsersLimitPerPlan { get; init; }
	public int CampaignsLimitPerPlan { get; init; }
	public bool CanUseInsightAnalytics { get; init; }
	public bool IsFreePlan { get; init; }
}

public static class SubscriptionPlanCatalog
{
	public const string StarterMonthlyKey = "starter_monthly_plan";
	public const string StarterYearlyKey = "starter_yearly_plan";
	public const string AdvancedMonthlyKey = "advanced_monthly_plan";
	public const string AdvancedYearlyKey = "advanced_yearly_plan";
	public const string TrialKey = "trial";
	public const int TrialDurationDays = 14;

	private static readonly IReadOnlyDictionary<string, SubscriptionPlanDefinition> Plans =
		new ReadOnlyDictionary<string, SubscriptionPlanDefinition>(new Dictionary<string, SubscriptionPlanDefinition>
		{
			{
				StarterMonthlyKey,
				new SubscriptionPlanDefinition
				{
					Id = Guid.Parse("0312ecaf-74da-427c-8750-f8b69aa948c3"),
					PlanKey = StarterMonthlyKey,
					Name = "OnTrack Starter Plan ($299/mo)",
					UsersLimitPerPlan = 1,
					CampaignsLimitPerPlan = 10,
					CanUseInsightAnalytics = false,
					IsFreePlan = false,
				}
			},
			{
				StarterYearlyKey,
				new SubscriptionPlanDefinition
				{
					Id = Guid.Parse("9e19b577-d158-479c-85ee-341e9c7d7564"),
					PlanKey = StarterYearlyKey,
					Name = "OnTrack Starter Plan ($2090/yr)",
					UsersLimitPerPlan = 1,
					CampaignsLimitPerPlan = 10,
					CanUseInsightAnalytics = false,
					IsFreePlan = false,
				}
			},
			{
				AdvancedMonthlyKey,
				new SubscriptionPlanDefinition
				{
					Id = Guid.Parse("8fa4f92b-0680-4a27-87a2-2baa0323671b"),
					PlanKey = AdvancedMonthlyKey,
					Name = "OnTrack Advanced Plan ($499/mo)",
					UsersLimitPerPlan = 3,
					CampaignsLimitPerPlan = 50,
					CanUseInsightAnalytics = true,
					IsFreePlan = false,
				}
			},
			{
				AdvancedYearlyKey,
				new SubscriptionPlanDefinition
				{
					Id = Guid.Parse("93abde3a-d53f-4337-b5fd-27c2bf870a34"),
					PlanKey = AdvancedYearlyKey,
					Name = "OnTrack Advanced Plan ($4900/yr)",
					UsersLimitPerPlan = 3,
					CampaignsLimitPerPlan = 50,
					CanUseInsightAnalytics = true,
					IsFreePlan = false,
				}
			},
			{
				TrialKey,
				new SubscriptionPlanDefinition
				{
					Id = Guid.Parse("6a95ab64-668f-4d3e-9047-4b7b881dfcf2"),
					PlanKey = TrialKey,
					Name = "Trial Plan",
					UsersLimitPerPlan = 1,
					CampaignsLimitPerPlan = 1,
					CanUseInsightAnalytics = false,
					IsFreePlan = true,
				}
			},
		});

	public static SubscriptionPlanDefinition GetPlanDetails(string planKey)
	{
		if (string.IsNullOrWhiteSpace(planKey))
			throw new ArgumentException("Plan key cannot be null or whitespace.", nameof(planKey));

		if (!Plans.TryGetValue(planKey.Trim(), out var details))
			throw new KeyNotFoundException($"Unknown subscription plan '{planKey}'.");

		return details;
	}

	public static IReadOnlyCollection<SubscriptionPlanDefinition> GetAllPlans() => Plans.Values.ToArray();
}
