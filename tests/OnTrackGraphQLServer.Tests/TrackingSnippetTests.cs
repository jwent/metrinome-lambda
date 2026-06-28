using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GraphQL;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class TrackingSnippetTests
{
    private const string DefaultExistingTestEmail = "jeremy.mark.went@gmail.com";

    [Fact]
    public async Task LandingPageSnippet_CreatesClickEvent()
    {
        using var harness = TrackingTestHarness.Create();
        var context = CreateResolveFieldContext(harness.User.Id);

        var snippet = Query.trackerCode(context, harness.Db);
        var activeTracker = TrackerController.GetUserTrackerByUser(harness.Db, harness.User.Id);

        Assert.NotNull(snippet);
        Assert.Contains($"https://tracking.test/click?t={activeTracker.Id}", snippet);
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

    [Fact]
    public async Task ActiveSubscriptionWithoutContract_StillCountsCveEvent()
    {
        using var harness = TrackingTestHarness.Create(includeContract: false, useExistingUser: false);

        var clickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://plan-backed.example.com/thank-you");

        var created = await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://plan-backed.example.com"),
            clickId.ToString());

        Assert.True(created);

        var cve = await harness.Db.ConversionVerificationEvents.SingleAsync(e => e.TrackerClickId == clickId);
        Assert.Equal("Verified", cve.Status);
        Assert.True(cve.CountsTowardCve);
        Assert.Null(cve.ContractId);
    }

    [Fact]
    public async Task DuplicatePostbacks_CreateDuplicateCve_AndStillCountTowardUsage()
    {
        using var harness = TrackingTestHarness.Create();

        var clickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://duplicate.example.com/thank-you");

        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://duplicate.example.com"),
            clickId.ToString());
        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://duplicate.example.com"),
            clickId.ToString());

        var cves = await harness.Db.ConversionVerificationEvents
            .Where(e => e.TrackerClickId == clickId)
            .OrderBy(e => e.SubmittedAtUtc)
            .ToListAsync();

        Assert.Equal(2, cves.Count);
        Assert.Equal("Verified", cves[0].Status);
        Assert.True(cves[0].CountsTowardCve);
        Assert.Equal("Duplicate", cves[1].Status);
        Assert.True(cves[1].CountsTowardCve);
        Assert.NotNull(cves[1].CountedAtUtc);
        Assert.Equal(cves[0].Id, cves[1].DuplicateOfEventId);
        Assert.Null(cves[1].RejectionReason);
    }

    [Fact]
    public async Task BotLikePostback_IsFlagged_AndStillCountsTowardUsage()
    {
        using var harness = TrackingTestHarness.Create();

        var clickId = await TrackerController.RegisterClickAsync(
            harness.Db,
            CreateRequest(origin: "https://botlike.example.com", userAgent: "Googlebot/2.1 (+http://www.google.com/bot.html)"),
            harness.Tracker.Id.ToString(),
            Encode("https://botlike.example.com/thank-you?cid=" + harness.Campaign.Id),
            Encode("https://google.com/search?q=ontrack"));

        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://botlike.example.com"),
            clickId.ToString());

        var cve = await harness.Db.ConversionVerificationEvents.SingleAsync(e => e.TrackerClickId == clickId);

        Assert.Equal("Flagged", cve.Status);
        Assert.True(cve.CountsTowardCve);
        Assert.NotNull(cve.CountedAtUtc);
        Assert.Null(cve.RejectionReason);
    }

    [Fact]
    public async Task HardLimitRejectedPostback_DoesNotCountTowardUsage()
    {
        using var harness = TrackingTestHarness.Create(committedAnnualCves: 1);

        var firstClickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://limit.example.com/thank-you-one");
        var secondClickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://limit.example.com/thank-you-two");

        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://limit.example.com"),
            firstClickId.ToString());
        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://limit.example.com"),
            secondClickId.ToString());

        var cves = await harness.Db.ConversionVerificationEvents
            .Where(e =>
                e.TrackerClickId == firstClickId ||
                e.TrackerClickId == secondClickId)
            .OrderBy(e => e.SubmittedAtUtc)
            .ToListAsync();

        Assert.Equal(2, cves.Count);
        Assert.Equal("Verified", cves[0].Status);
        Assert.True(cves[0].CountsTowardCve);
        Assert.Equal("Rejected", cves[1].Status);
        Assert.False(cves[1].CountsTowardCve);
        Assert.Null(cves[1].CountedAtUtc);
        Assert.Equal("CVE hard limit reached for active contract.", cves[1].RejectionReason);
    }

    [Fact]
    public async Task ExpiredTrialSubscription_DoesNotAcceptTrackedClicks()
    {
        using var harness = TrackingTestHarness.Create(
            planKey: SubscriptionPlanCatalog.TrialKey,
            subscriptionTrialStartDate: DateTime.UtcNow.AddDays(-(SubscriptionPlanCatalog.TrialDurationDays + 1)),
            useExistingUser: false);

        await Assert.ThrowsAsync<BadHttpRequestException>(() => TrackerController.RegisterClickAsync(
            harness.Db,
            CreateRequest(origin: "https://expired.example.com"),
            harness.Tracker.Id.ToString(),
            Encode("https://expired.example.com/?cid=" + harness.Campaign.Id),
            Encode("https://google.com/search?q=ontrack")));
    }

    [Fact]
    public async Task CveQueries_AreAvailableToNonAdminUsers_AndReturnOnlyCurrentOrganizationData()
    {
        using var viewerHarness = TrackingTestHarness.Create();
        using var otherHarness = TrackingTestHarness.Create(userState: "Viewer", useExistingUser: false);
        var context = CreateResolveFieldContext(viewerHarness.User.Id);

        var viewerClickId = await CreateClickAsync(
            viewerHarness.Db,
            viewerHarness.Tracker.Id,
            viewerHarness.Campaign.Id,
            "https://viewer.example.com/thank-you");
        await TrackerController.RegisterPostbackAsync(
            viewerHarness.Db,
            CreateRequest(origin: "https://viewer.example.com"),
            viewerClickId.ToString());

        var otherClickId = await CreateClickAsync(
            otherHarness.Db,
            otherHarness.Tracker.Id,
            otherHarness.Campaign.Id,
            "https://other.example.com/thank-you");
        await TrackerController.RegisterPostbackAsync(
            otherHarness.Db,
            CreateRequest(origin: "https://other.example.com"),
            otherClickId.ToString());

        var legacyResults = Query.adminCves(context, viewerHarness.Db);
        var userResults = Query.myCves(context, viewerHarness.Db);

        Assert.Contains(legacyResults, cve => cve.TrackerClickId == viewerClickId);
        Assert.Contains(userResults, cve => cve.TrackerClickId == viewerClickId);
        Assert.DoesNotContain(legacyResults, cve => cve.TrackerClickId == otherClickId);
        Assert.DoesNotContain(userResults, cve => cve.TrackerClickId == otherClickId);
    }

    [Fact]
    public async Task AccountSummary_ReturnsContractUsageMetrics_FromProcessedCves()
    {
        using var harness = TrackingTestHarness.Create();
        var context = CreateResolveFieldContext(harness.User.Id);
        var baseline = Query.accountSummary(context, harness.Db);

        var firstClickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://summary.example.com/thank-you-one");
        var secondClickId = await CreateClickAsync(
            harness.Db,
            harness.Tracker.Id,
            harness.Campaign.Id,
            "https://summary.example.com/thank-you-two");

        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://summary.example.com"),
            firstClickId.ToString());
        await TrackerController.RegisterPostbackAsync(
            harness.Db,
            CreateRequest(origin: "https://summary.example.com"),
            secondClickId.ToString());

        var summary = Query.accountSummary(context, harness.Db);

        Assert.True(summary.Success);
        Assert.Equal(harness.Organization.Id, summary.OrganizationId);
        Assert.Equal(baseline.UserCount, summary.UserCount);
        Assert.Equal(baseline.CampaignCount, summary.CampaignCount);
        Assert.Equal(baseline.TotalCves + 2, summary.TotalCves);
        Assert.Equal(baseline.CountedCves + 2, summary.CountedCves);
        Assert.Equal(baseline.VerifiedCves + 2, summary.VerifiedCves);
        Assert.Equal(baseline.CurrentPeriodCountedCves + 2, summary.CurrentPeriodCountedCves);
        Assert.Equal(baseline.CurrentPeriodVerifiedCves + 2, summary.CurrentPeriodVerifiedCves);
        Assert.Equal(baseline.CurrentPeriodProcessedCves + 2, summary.CurrentPeriodProcessedCves);
        Assert.Equal("Core", summary.ContractTierName);
        Assert.Equal(20000, summary.CurrentPeriodCveLimit);
        Assert.Equal(20000, summary.CommittedAnnualCves);
        Assert.Equal(20000 - summary.CurrentPeriodProcessedCves, summary.RemainingCommittedCves);
        Assert.Equal(150, summary.RatePerCveCents);
        Assert.Equal(3000000, summary.AnnualMinimumFeeCents);
        Assert.Equal(3000000, summary.AnnualContractValueCents);
        Assert.Equal(3000000, summary.CurrentPlanCostCents);
        Assert.Equal((baseline.CurrentPeriodProcessedCves + 2) * 150, summary.UsageCostCents);
        Assert.Equal((baseline.CurrentPeriodProcessedCves + 2) * 150, summary.UsageValueCents);
        Assert.NotNull(summary.SubscriptionPlan?.PlanKey);
        Assert.NotNull(summary.SubscriptionStatus);
        Assert.NotNull(summary.CostCalculation);
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

    private static HttpRequest CreateRequest(string origin, string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
        httpContext.Request.Headers["User-Agent"] = userAgent;
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

        public static TrackingTestHarness Create(
            string userState = "Admin",
            string planKey = SubscriptionPlanCatalog.StarterMonthlyKey,
            DateTime? subscriptionTrialStartDate = null,
            bool includeContract = true,
            int committedAnnualCves = 20_000,
            bool useExistingUser = true,
            string? userEmail = null)
        {
            Environment.SetEnvironmentVariable("ONTRACK_CLICK_ENDPOINT_URL", "https://tracking.test");
            Environment.SetEnvironmentVariable("ONTRACK_SITE_URL", "https://app.test/");
            Environment.SetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY", "test-signing-key-1234567890");
            LoadLaunchProfileEnvironmentVariables();

            var connectionString = Environment.GetEnvironmentVariable("ONTRACK_DATABASE_CONNECT_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ONTRACK_DATABASE_CONNECT_STRING must be set to run CVE tests against Postgres.");

            var options = new DbContextOptionsBuilder<OnTrackDBContext>()
                .UseNpgsql(connectionString)
                .Options;

            var db = new OnTrackDBContext(options);
            var plan = UserController.GetSubscriptionPlanByKey(db, planKey);

            var now = DateTime.UtcNow;
            var runId = Guid.NewGuid().ToString("N")[..8];
            var preferredEmail = userEmail ?? (useExistingUser ? DefaultExistingTestEmail : $"cve-test-{runId}@example.com");

            UserOrganization organization;
            User user;

            if (useExistingUser)
            {
                user = db.Users
                    .Include(u => u.Organization)
                        .ThenInclude(o => o.SubscriptionPlan)
                    .Include(u => u.ExtraProperties)
                    .Include(u => u.UserRoles)
                    .FirstOrDefault(u => u.Email == preferredEmail)
                    ?? throw new InvalidOperationException($"Expected existing test user '{preferredEmail}' was not found.");

                organization = user.Organization;
            }
            else
            {
                organization = new UserOrganization
                {
                    Id = Guid.NewGuid(),
                    CreatorId = Guid.NewGuid(),
                    CreatedAt = now.AddDays(-30),
                    SubscriptionPlan = plan,
                    SubscriptionTrialStartDate = subscriptionTrialStartDate,
                    Users = new List<User>(),
                    OrganizationalTrackers = new List<UserTracker>(),
                };

                user = new User
                {
                    Id = Guid.NewGuid(),
                    Organization = organization,
                    OrganizationId = organization.Id,
                    Email = preferredEmail,
                    Password = "unused",
                    CreatedAt = now.AddDays(-30),
                    ResetPasswordToken = string.Empty,
                    UserState = userState,
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

                organization.Users.Add(user);
                db.UserOrganizations.Add(organization);
                db.Users.Add(user);
                db.UserExtraProperties.AddRange(user.ExtraProperties);
            }

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
                TierName = "Core",
                CommittedAnnualCVEs = committedAnnualCves,
                ContractStartDate = now.AddMinutes(-1),
                ContractEndDate = now.AddYears(1),
                CVEHardLimitEnabled = true,
                CreatedAt = now.AddMinutes(-1),
                UpdatedAt = now.AddMinutes(-1),
            };

            if (organization.OrganizationalTrackers == null)
                organization.OrganizationalTrackers = new List<UserTracker>();

            organization.OrganizationalTrackers.Add(tracker);
            tracker.Campaigns.Add(campaign);

            db.UserTrackers.Add(tracker);
            db.TrackingCampaigns.Add(campaign);
            db.SaveChanges();

            if (includeContract)
            {
                db.OrganizationCveContracts.Add(contract);
                db.SaveChanges();
            }

            return new TrackingTestHarness(db, user, organization, tracker, campaign, contract);
        }

        private static void LoadLaunchProfileEnvironmentVariables()
        {
            var launchSettingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Properties",
                "launchSettings.json"));

            if (!File.Exists(launchSettingsPath))
                return;

            using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) ||
                !profiles.TryGetProperty("OnTrackGraphQLServer", out var profile) ||
                !profile.TryGetProperty("environmentVariables", out var environmentVariables))
                return;

            foreach (var environmentVariable in environmentVariables.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable.Name)))
                    continue;

                if (environmentVariable.Value.ValueKind == JsonValueKind.String)
                    Environment.SetEnvironmentVariable(environmentVariable.Name, environmentVariable.Value.GetString());
            }
        }

        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
