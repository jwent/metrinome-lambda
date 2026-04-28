using System.Net;
using System.Security.Claims;
using System.Text;
using GraphQL;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class TrackingSnippetTests
{
    [Fact]
    public async Task LandingPageSnippet_CreatesClickEvent()
    {
        using var harness = TrackingTestHarness.Create();
        var context = CreateResolveFieldContext(harness.User.Id);

        var snippet = Query.trackerCode(context, harness.Db);

        Assert.NotNull(snippet);
        Assert.Contains($"https://tracking.test/click?t={harness.Tracker.Id}", snippet);
        Assert.Contains("var cookieName = 'ontrack-clid';", snippet);

        var clickId = await TrackerController.RegisterClickAsync(
            harness.Db,
            CreateRequest(origin: "https://landing.example.com"),
            harness.Tracker.Id.ToString(),
            Encode("https://landing.example.com/?cid=" + harness.Campaign.Id),
            Encode("https://google.com/search?q=ontrack"));

        var click = await harness.Db.TrackerClicks
            .Include(c => c.Campaign)
            .SingleAsync(c => c.Id == clickId);

        Assert.Equal(harness.Campaign.Id, click.Campaign?.Id);
        Assert.Equal("https://landing.example.com/?cid=" + harness.Campaign.Id, click.ClickUrl);
        Assert.Equal("https://google.com/search?q=ontrack", click.Referer);
        Assert.False(click.Conversion);
        Assert.False(click.IsBotClick);
        Assert.True(click.IsDesktop);
        Assert.Equal(3, await harness.Db.TrackerClickExtraProperties.CountAsync(p => p.ClickParent.Id == clickId));
    }

    [Fact]
    public async Task ThankYouPageSnippet_CreatesConversionAndCveEvent()
    {
        using var harness = TrackingTestHarness.Create();
        var context = CreateResolveFieldContext(harness.User.Id);

        var snippet = Query.postbackCode(context);

        Assert.NotNull(snippet.PagePostback);
        Assert.Contains("https://tracking.test/postback?clid=' + encodeURIComponent(clid)", snippet.PagePostback);

        var clickId = await CreateClickAsync(harness.Db, harness.Tracker.Id, harness.Campaign.Id, "https://thank-you.example.com/order-complete");
        var created = await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://thank-you.example.com"),
            clickId.ToString());

        Assert.True(created);

        var click = await harness.Db.TrackerClicks.SingleAsync(c => c.Id == clickId);
        var cve = await harness.Db.ConversionVerificationEvents.SingleAsync(e => e.TrackerClickId == clickId);
        var site = await harness.Db.OrganizationSites.SingleAsync(s => s.Id == cve.SiteId);

        Assert.True(click.Conversion);
        Assert.NotNull(click.ConversionDate);
        Assert.Equal("Verified", cve.Status);
        Assert.True(cve.CountsTowardCve);
        Assert.Equal(harness.Contract.Id, cve.ContractId);
        Assert.Equal(harness.Campaign.Id, cve.TrackingCampaignId);
        Assert.Equal("javascript_postback", cve.Source);
        Assert.Equal("thank-you.example.com", site.Domain);
    }

    [Fact]
    public async Task ConfirmButtonSnippet_CreatesConversionAndCveEvent()
    {
        using var harness = TrackingTestHarness.Create();
        var context = CreateResolveFieldContext(harness.User.Id);

        var snippet = Query.postbackCode(context);

        Assert.NotNull(snippet.ButtonPostback);
        Assert.Contains("document.getElementById('{id}')", snippet.ButtonPostback);
        Assert.Contains("https://tracking.test/postback?clid='+clid", snippet.ButtonPostback);

        var clickId = await CreateClickAsync(harness.Db, harness.Tracker.Id, harness.Campaign.Id, "https://checkout.example.com/confirm");
        var created = await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://checkout.example.com"),
            clickId.ToString());

        Assert.True(created);

        var cve = await harness.Db.ConversionVerificationEvents.SingleAsync(e => e.TrackerClickId == clickId);
        var site = await harness.Db.OrganizationSites.SingleAsync(s => s.Id == cve.SiteId);

        Assert.Equal("Verified", cve.Status);
        Assert.True(cve.CountsTowardCve);
        Assert.Equal(harness.Tracker.Id, cve.TrackerId);
        Assert.Equal("checkout.example.com", site.Domain);
    }

    private static async Task<Guid> CreateClickAsync(OnTrackDBContext db, Guid trackerId, Guid campaignId, string url)
    {
        return await TrackerController.RegisterClickAsync(
            db,
            CreateRequest(origin: new Uri(url).GetLeftPart(UriPartial.Authority)),
            trackerId.ToString(),
            Encode(url + "?cid=" + campaignId),
            Encode("https://google.com/search?q=ontrack"));
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

    private static HttpRequest CreateRequest(string origin)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        httpContext.Request.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
        httpContext.Request.Headers["CF-IPCountry"] = "US";
        httpContext.Request.Headers["X-Region"] = "NY";
        httpContext.Request.Headers["X-City"] = "New York";
        httpContext.Request.Headers["Origin"] = origin;
        return httpContext.Request;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private sealed class TrackingTestHarness : IDisposable
    {
        public OnTrackDBContext Db { get; }
        public User User { get; }
        public UserOrganization Organization { get; }
        public UserTracker Tracker { get; }
        public TrackingCampaign Campaign { get; }
        public OrganizationCveContract Contract { get; }

        private TrackingTestHarness(
            OnTrackDBContext db,
            User user,
            UserOrganization organization,
            UserTracker tracker,
            TrackingCampaign campaign,
            OrganizationCveContract contract)
        {
            Db = db;
            User = user;
            Organization = organization;
            Tracker = tracker;
            Campaign = campaign;
            Contract = contract;
        }

        public static TrackingTestHarness Create()
        {
            Environment.SetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL", "https://tracking.test");
            Environment.SetEnvironmentVariable("ONTRACK_SITE_URL", "https://app.test/");
            Environment.SetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY", "test-signing-key-1234567890");

            var connectionString = Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ONTRACK_DATABASE_CONNECT_STRING must be set to run CVE tests against Postgres.");

            var options = new DbContextOptionsBuilder<OnTrackDBContext>()
                .UseNpgsql(connectionString)
                .Options;

            var db = new OnTrackDBContext(options);

            var now = DateTime.UtcNow;
            var runId = Guid.NewGuid().ToString("N")[..8];
            var organization = new UserOrganization
            {
                Id = Guid.NewGuid(),
                CreatorId = Guid.NewGuid(),
                CreatedAt = now.AddDays(-30),
                SubscriptionPlan = null,
                Users = new List<User>(),
                OrganizationalTrackers = new List<UserTracker>(),
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                OrganizationId = organization.Id,
                Email = $"cve-test-{runId}@example.com",
                Password = "unused",
                CreatedAt = now.AddDays(-30),
                ResetPasswordToken = string.Empty,
                UserState = "Admin",
                ExtraProperties = new List<UserExtraProperty>
                {
                    new UserExtraProperty
                    {
                        Id = Guid.NewGuid(),
                        PropertyKey = "FullName",
                        PropertyValue = $"CVE Test {runId}",
                    }
                },
                UserRoles = new List<UserOrganizationalRoleAssociation>(),
            };

            foreach (var prop in user.ExtraProperties)
                prop.Parent = user;

            var tracker = new UserTracker
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                CreatedAt = now.AddDays(-30),
                Campaigns = new List<TrackingCampaign>(),
            };

            var campaign = new TrackingCampaign
            {
                Id = Guid.NewGuid(),
                ParentTracker = tracker,
                CreatedAt = now.AddDays(-7),
                CampaignName = $"CVE Test Campaign {runId}",
                Platform = "google",
                ConversionValue = "100",
                Clicks = new List<TrackerClick>(),
                ExtraProperties = new List<TrackingCampaignExtraProperty>(),
            };

            var contract = new OrganizationCveContract
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                TierName = $"CVE Test Contract {runId}",
                CommittedAnnualCVEs = 100,
                ContractStartDate = now.AddDays(-1),
                ContractEndDate = now.AddYears(1),
                CVEHardLimitEnabled = true,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            };

            organization.Users.Add(user);
            organization.OrganizationalTrackers.Add(tracker);
            tracker.Campaigns.Add(campaign);

            db.UserOrganizations.Add(organization);
            db.Users.Add(user);
            db.UserExtraProperties.AddRange(user.ExtraProperties);
            db.UserTrackers.Add(tracker);
            db.TrackingCampaigns.Add(campaign);
            db.SaveChanges();

            db.OrganizationCveContracts.Add(contract);
            db.SaveChanges();

            return new TrackingTestHarness(db, user, organization, tracker, campaign, contract);
        }

        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
