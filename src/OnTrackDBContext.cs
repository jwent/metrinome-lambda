using Microsoft.EntityFrameworkCore;
using GraphQL;
using GraphQL.Authorization;

public class OnTrackDBContext : DbContext
{
    public OnTrackDBContext(DbContextOptions options) : base(options) { }

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING") ?? "");
        // optionsBuilder.LogTo(Console.WriteLine); // Uncomment to debug SQL queries
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var starterMonthly = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.StarterMonthlyKey);
        var starterYearly = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.StarterYearlyKey);
        var advancedMonthly = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.AdvancedMonthlyKey);
        var advancedYearly = StripePlanConfiguration.GetPlanDetails(StripePlanConfiguration.AdvancedYearlyKey);

        modelBuilder.Entity<OrganizationalSubscriptionPlan>().HasData(
            // Starter Monthly
            new OrganizationalSubscriptionPlan
            {
                Id = Guid.NewGuid(),
                PlanKey = starterMonthly.PlanKey,
                PlanName = starterMonthly.Name,
                UsersLimitPerPlan = 1,
                CampaignsLimitPerPlan = 10,
                CanUseInsightAnalytics = false,
                IsFreePlan = false,
            },
            // Starter Yearly
            new OrganizationalSubscriptionPlan
            {
                Id = Guid.NewGuid(),
                PlanKey = starterYearly.PlanKey,
                PlanName = starterYearly.Name,
                UsersLimitPerPlan = 1,
                CampaignsLimitPerPlan = 10,
                CanUseInsightAnalytics = false,
                IsFreePlan = false,
            },
            // Advanced Monthly
            new OrganizationalSubscriptionPlan
            {
                Id = Guid.NewGuid(),
                PlanKey = advancedMonthly.PlanKey,
                PlanName = advancedMonthly.Name,
                UsersLimitPerPlan = 3,
                CampaignsLimitPerPlan = 50,
                CanUseInsightAnalytics = true,
                IsFreePlan = false,
            },
            // Advanced Yearly
            new OrganizationalSubscriptionPlan
            {
                Id = Guid.NewGuid(),
                PlanKey = advancedYearly.PlanKey,
                PlanName = advancedYearly.Name,
                UsersLimitPerPlan = 3,
                CampaignsLimitPerPlan = 50,
                CanUseInsightAnalytics = true,
                IsFreePlan = false,
            },
            // Trial
            new OrganizationalSubscriptionPlan
            {
                Id = Guid.NewGuid(),
                PlanKey = "trial",
                PlanName = "Trial Plan",
                UsersLimitPerPlan = 1,
                CampaignsLimitPerPlan = 1,
                CanUseInsightAnalytics = false,
                IsFreePlan = true,
            }
        );

        base.OnModelCreating(modelBuilder);
    }
}
