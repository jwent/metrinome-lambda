using GraphQL;
using GraphQL.Authorization;



public class Blog {
	public int? BlogId { get; set; }
	public string? Url { get; set; }

	public List<Post>? Posts { get; set; }
}

public class Post {
	public int? PostId { get; set; }
	public string? Title { get; set; }
	public string? Content { get; set; }

	public int? BlogId { get; set; }
	public Blog? Blog { get; set; }
}

public class Comment {
	public int? CommentId { get; set; }
	public string? Text { get; set; }
}

public class User {
	public Guid? Id { get; set; }
	public string? Email { get; set; }
	public string? Password { get; set; }
}

public class TrackingCampaign {
	public Guid? Id { get; set; }
	public Guid? OwnerId { get; set; }

	public DateTime? CreatedAt { get; set; }
	public int? Audience { get; set; }

	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? TargetedCountries { get; set; }
	public string? CampaignBudget { get; set; }
	public string? CampaignDuration { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }
}


public class TrackingCampaignSubmission {
	public string? Platform { get; set; }
	public string? CampaignName { get; set; }
	public string? TargetedCountries { get; set; }
	public string? CampaignBudget { get; set; }
	public string? CampaignDuration { get; set; }

	public string? WebsiteDomain { get; set; }
	public string? CartPageURL { get; set; }
	public string? LandingPageURL { get; set; }
	public string? PrivacyPageURL { get; set; }
}
