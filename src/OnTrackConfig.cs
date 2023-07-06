



public class OnTrackConfig {
	public class OnTrackSubscriptionPlanConfig {
		public readonly int usersLimitPerPlan;
		public readonly int campaignsLimitPerPlan;

		public OnTrackSubscriptionPlanConfig(int usersLimit, int campaignsLimit) {
			usersLimitPerPlan = usersLimit;
			campaignsLimitPerPlan = campaignsLimit;
		}
	}

	// public readonly OnTrackSubscriptionPlanConfig onTrackStarterPlan = new OnTrackSubscriptionPlanConfig(1,3);
	// public readonly OnTrackSubscriptionPlanConfig onTrackPremiumPlan = new OnTrackSubscriptionPlanConfig(2,7);

	public static readonly Dictionary<string, OnTrackSubscriptionPlanConfig> onTrackSubscriptionPlans = new Dictionary<string, OnTrackSubscriptionPlanConfig> {
		{"StarterPlan", new OnTrackSubscriptionPlanConfig(1,3) },
		{"PremiumPlan", new OnTrackSubscriptionPlanConfig(2,7) },
		{"EnterprisePlan", new OnTrackSubscriptionPlanConfig(1000,10000) },
	};
}
