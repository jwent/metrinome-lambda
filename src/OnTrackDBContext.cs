using Microsoft.EntityFrameworkCore;
using GraphQL;
using GraphQL.Authorization;
using System.Diagnostics;

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
    public DbSet<OrganizationCveContract> OrganizationCveContracts { get; set; } = null!;
    public DbSet<OrganizationSite> OrganizationSites { get; set; } = null!;
    public DbSet<ConversionVerificationEvent> ConversionVerificationEvents { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;

        var connectString = Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING");

        if (string.IsNullOrWhiteSpace(connectString))
        {
            var error = "ONTRACK_DATABASE_CONNECT_STRING environment variable is missing or empty.";
            Console.WriteLine(error);
            throw new InvalidOperationException(error);
        }

        try
        {
            optionsBuilder
                .UseNpgsql(connectString)
                .EnableDetailedErrors()
                .EnableSensitiveDataLogging() // optional; remove for production
                .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);

            // Test the connection right away (optional but useful)
            using var testConnection = new Npgsql.NpgsqlConnection(connectString);
            testConnection.Open();
            Console.WriteLine($"Connected to database: {testConnection.Database} on {testConnection.Host}");
            testConnection.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to the database. Error: {ex.Message}");
            throw new InvalidOperationException("Unable to connect to the database. Check your connection string and network.", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationalSubscriptionPlan>().HasData(
            SubscriptionPlanCatalog.GetAllPlans().Select(plan => new OrganizationalSubscriptionPlan
            {
                Id = plan.Id,
                PlanKey = plan.PlanKey,
                PlanName = plan.Name,
                UsersLimitPerPlan = plan.UsersLimitPerPlan,
                CampaignsLimitPerPlan = plan.CampaignsLimitPerPlan,
                CanUseInsightAnalytics = plan.CanUseInsightAnalytics,
                IsFreePlan = plan.IsFreePlan,
            }).ToArray()
        );

        base.OnModelCreating(modelBuilder);
    }
}
