using Microsoft.EntityFrameworkCore;
using GraphQL;
using GraphQL.Authorization;


public class OnTrackDBContext : DbContext {
	public OnTrackDBContext(DbContextOptions options) : base(options) {}
	public DbSet<User> Users { get; set; } = null!;
	public DbSet<UserExtraProperty> UserExtraProperties { get; set; } = null!;
	public DbSet<UserOrganization> UserOrganizations { get; set; } = null!;
	public DbSet<UserOrganizationalRoleAssociation> UserOrganizationalRoleAssociations { get; set; } = null!;
	public DbSet<UserTracker> UserTrackers { get; set; } = null!;
	public DbSet<TrackingCampaign> TrackingCampaigns { get; set; } = null!;
	public DbSet<TrackingCampaignExtraProperty> TrackingCampaignExtraProperties { get; set; } = null!;
	public DbSet<TrackerClick> TrackerClicks { get; set; } = null!;
	public DbSet<TrackerClickExtraProperty> TrackerClickExtraProperties { get; set; } = null!;
	public DbSet<OrganizationalSubscriptionPlan> OrganizationalSubscriptionPlans { get; set; } = null!;

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
		optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING") ?? "");
		// // use this to debug the actual sql queries sent to the server
		// optionsBuilder.LogTo(Console.WriteLine);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
                var basicPlan = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.BasicPlanKey);
                var proPlan = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.ProPlanKey);

                modelBuilder.Entity<OrganizationalSubscriptionPlan>().HasData(
                        new OrganizationalSubscriptionPlan {
                                Id=Guid.NewGuid(),
                                PlanKey=basicPlan.PlanKey,
                                PlanName=basicPlan.Name,
                                UsersLimitPerPlan=5,
                                CampaignsLimitPerPlan=15,
                                CanUseInsightAnalytics=true,
                                IsFreePlan=false,
                        },
                        new OrganizationalSubscriptionPlan {
                                Id=Guid.NewGuid(),
                                PlanKey=proPlan.PlanKey,
                                PlanName=proPlan.Name,
                                UsersLimitPerPlan=1000,
                                CampaignsLimitPerPlan=10000,
                                CanUseInsightAnalytics=true,
                                IsFreePlan=false,
                        });

		base.OnModelCreating(modelBuilder);
	}
}