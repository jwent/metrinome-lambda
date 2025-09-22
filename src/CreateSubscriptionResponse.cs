public class CreateSubscriptionResponse
{
        public bool Success { get; set; }
        public string? ClientSecret { get; set; }
        public string? SubscriptionId { get; set; }
        public string? CustomerId { get; set; }
        public string? PublishableKey { get; set; }
        public string? PlanKey { get; set; }
        public string? PriceId { get; set; }
        public bool? RequiresAction { get; set; }
        public bool? AlreadySubscribed { get; set; }
        public string? Error { get; set; }
}
