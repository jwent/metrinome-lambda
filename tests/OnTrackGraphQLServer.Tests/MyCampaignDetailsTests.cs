using System.Security.Claims;
using GraphQL;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class MyCampaignDetailsTests
{
    [Fact]
    public void myCampaignDetails_ReturnsCampaignType_WhenExtraPropertyExists()
    {
        using var harness = CampaignDetailsTestHarness.Create(includeCampaignType: true);
        var context = CreateResolveFieldContext(harness.User.Id);

        var details = Query.myCampaignDetails(context, harness.Db, harness.Campaign.Id.ToString());

        Assert.Equal(harness.Campaign.Id, details.TrackingCampaignData.Id);
        Assert.Equal("Search campaigns", details.TrackingCampaignData.CampaignType);
    }

    [Fact]
    public void myCampaignDetails_ReturnsUnknownCampaignType_WhenExtraPropertyIsMissing()
    {
        using var harness = CampaignDetailsTestHarness.Create(includeCampaignType: false);
        var context = CreateResolveFieldContext(harness.User.Id);

        var details = Query.myCampaignDetails(context, harness.Db, harness.Campaign.Id.ToString());

        Assert.Equal(harness.Campaign.Id, details.TrackingCampaignData.Id);
        Assert.Equal("Unknown", details.TrackingCampaignData.CampaignType);
    }

    [Fact]
    public void myCampaignDetails_ReturnsUnmatchedConversions_FromRealCveEvents()
    {
        using var harness = CampaignDetailsTestHarness.Create(includeCampaignType: true);
        harness.SeedUnmatchedConversionEvent();
        var context = CreateResolveFieldContext(harness.User.Id);

        var details = Query.myCampaignDetails(context, harness.Db, harness.Campaign.Id.ToString());

        Assert.Equal(1, details.TrackingCampaignData.UnmatchedConversions);
    }

    [Fact]
    public void myCampaignDetails_ReturnsEmptyDetails_WhenCampaignIdIsInvalid()
    {
        using var harness = CampaignDetailsTestHarness.Create(includeCampaignType: true);
        var context = CreateResolveFieldContext(harness.User.Id);

        var details = Query.myCampaignDetails(context, harness.Db, "not-a-guid");

        Assert.Equal(Guid.Empty, details.TrackingCampaignData.Id);
        Assert.Equal("Unknown", details.TrackingCampaignData.CampaignType);
        Assert.Equal(0, details.Clicks.ClickCount);
        Assert.Empty(details.Clicks.ClickDatas);
    }

    [Fact]
    public void myCampaignDetails_ReturnsEmptyDetails_ForCampaignInAnotherOrganization()
    {
        using var harness = CampaignDetailsTestHarness.Create(includeCampaignType: true);
        var otherCampaign = harness.SeedOtherOrganizationCampaign();
        var context = CreateResolveFieldContext(harness.User.Id);

        var details = Query.myCampaignDetails(context, harness.Db, otherCampaign.Id.ToString());

        Assert.Equal(Guid.Empty, details.TrackingCampaignData.Id);
        Assert.Equal("Unknown", details.TrackingCampaignData.CampaignType);
        Assert.Equal(0, details.Clicks.ClickCount);
    }

    private static IResolveFieldContext CreateResolveFieldContext(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("id", userId.ToString()),
                new Claim(ClaimTypes.Role, "Customer"),
            },
            authenticationType: "TestAuth");

        return new ResolveFieldContext
        {
            User = new ClaimsPrincipal(identity),
        };
    }

    private sealed class CampaignDetailsTestHarness : IDisposable
    {
        private readonly SqliteConnection connection;

        private CampaignDetailsTestHarness(
            SqliteConnection connection,
            OnTrackDBContext db,
            User user,
            TrackingCampaign campaign,
            UserOrganization organization,
            UserTracker tracker)
        {
            this.connection = connection;
            Db = db;
            User = user;
            Campaign = campaign;
            Organization = organization;
            Tracker = tracker;
        }

        public OnTrackDBContext Db { get; }
        public User User { get; }
        public TrackingCampaign Campaign { get; }
        public UserOrganization Organization { get; }
        public UserTracker Tracker { get; }

        public static CampaignDetailsTestHarness Create(bool includeCampaignType)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<OnTrackDBContext>()
                .UseSqlite(connection)
                .Options;

            var db = new OnTrackDBContext(options);
            db.Database.EnsureCreated();

            var now = DateTime.UtcNow;
            var organization = new UserOrganization
            {
                Id = Guid.NewGuid(),
                CreatorId = Guid.NewGuid(),
                CreatedAt = now,
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                OrganizationId = organization.Id,
                Email = $"campaign-details-{Guid.NewGuid():N}@example.com",
                Password = "unused",
                CreatedAt = now,
                ResetPasswordToken = string.Empty,
                UserState = "Active",
            };
            organization.CreatorId = user.Id;

            var tracker = new UserTracker
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                CreatedAt = now,
            };

            var campaign = new TrackingCampaign
            {
                Id = Guid.NewGuid(),
                ParentTracker = tracker,
                CreatedAt = now,
                CampaignName = "Campaign Details Test",
                Platform = "google",
                CampaignBudget = "100",
                ConversionValue = "25",
                LandingPageURL = "https://example.com",
            };

            db.UserOrganizations.Add(organization);
            db.Users.Add(user);
            db.UserTrackers.Add(tracker);
            db.TrackingCampaigns.Add(campaign);

            if (includeCampaignType)
            {
                db.TrackingCampaignExtraProperties.Add(new TrackingCampaignExtraProperty
                {
                    Id = Guid.NewGuid(),
                    Parent = campaign,
                    PropertyKey = "CampaignType",
                    PropertyValue = "Search campaigns",
                });
            }

            db.SaveChanges();

            return new CampaignDetailsTestHarness(connection, db, user, campaign, organization, tracker);
        }

        public void SeedUnmatchedConversionEvent()
        {
            var now = DateTime.UtcNow;
            var site = new OrganizationSite
            {
                Id = Guid.NewGuid(),
                OrganizationId = Organization.Id,
                SiteName = "example.com",
                Domain = "example.com",
                TrackingId = Tracker.Id.ToString(),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            Db.OrganizationSites.Add(site);
            Db.ConversionVerificationEvents.Add(new ConversionVerificationEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = Organization.Id,
                SiteId = site.Id,
                TrackerId = Tracker.Id,
                TrackingCampaignId = null,
                TrackerClickId = null,
                SubmittedAtUtc = now,
                OriginalEventTimestampUtc = now,
                Status = "Unmatched",
                CountsTowardCve = true,
                CountedAtUtc = now,
                Source = "javascript_postback_unmatched",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            Db.SaveChanges();
        }

        public TrackingCampaign SeedOtherOrganizationCampaign()
        {
            var now = DateTime.UtcNow;
            var organization = new UserOrganization
            {
                Id = Guid.NewGuid(),
                CreatorId = Guid.NewGuid(),
                CreatedAt = now,
            };

            var tracker = new UserTracker
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                CreatedAt = now,
            };

            var campaign = new TrackingCampaign
            {
                Id = Guid.NewGuid(),
                ParentTracker = tracker,
                CreatedAt = now,
                CampaignName = "Other Organization Campaign",
                Platform = "google",
            };

            Db.UserOrganizations.Add(organization);
            Db.UserTrackers.Add(tracker);
            Db.TrackingCampaigns.Add(campaign);
            Db.SaveChanges();

            return campaign;
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }
}
