using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class SignupDuplicateEmailTests
{
    private const string DuplicateEmailError = "Invalid or duplicate email.";

    [Fact]
    public async Task AddUser_CreatesAccount_WhenGuidEmailIsUnique()
    {
        using var harness = SignupTestHarness.Create();
        var email = $"signup-{Guid.NewGuid():N}@example.com";

        var result = await Mutation.addUser(
            harness.Db,
            fullname: "Signup Test User",
            email: email,
            password: "ValidPassword123!",
            canUseMagicLink: true);

        Assert.NotNull(result.Id);
        Assert.Null(result.Error);
        Assert.NotNull(result.MagicLinkUrl);
        var user = await harness.Db.Users.SingleAsync(user => user.Email == email);
        Assert.True(await harness.Db.OrganizationCveContracts.AnyAsync(contract => contract.OrganizationId == user.OrganizationId));
    }

    [Fact]
    public async Task AddUser_ReturnsDuplicateEmailError_WhenGuidEmailAlreadyExists()
    {
        using var harness = SignupTestHarness.Create();
        var email = $"signup-{Guid.NewGuid():N}@example.com";
        harness.SeedUser(email);

        var result = await Mutation.addUser(
            harness.Db,
            fullname: "Signup Test User",
            email: email,
            password: "ValidPassword123!",
            canUseMagicLink: false);

        Assert.Null(result.Id);
        Assert.Equal(DuplicateEmailError, result.Error);
    }

    [Fact]
    public async Task AddUser_ReturnsDuplicateEmailError_WhenGuidEmailIsTooLong()
    {
        using var harness = SignupTestHarness.Create();
        var email = $"signup-{Guid.NewGuid():N}-{Guid.NewGuid():N}-{Guid.NewGuid():N}-{Guid.NewGuid():N}@example.com";

        Assert.True(email.Length > 128);

        var result = await Mutation.addUser(
            harness.Db,
            fullname: "Signup Test User",
            email: email,
            password: "ValidPassword123!",
            canUseMagicLink: false);

        Assert.Null(result.Id);
        Assert.Equal(DuplicateEmailError, result.Error);
    }

    [Fact]
    public void VerifyUserEmail_AcceptsLegacyResetTokenWithTrackingSuffix()
    {
        using var harness = SignupTestHarness.Create();
        var email = $"signup-{Guid.NewGuid():N}@example.com";
        var resetToken = "0E566FA9CDA5974591D4488C93F89F8BB8F4EBABFEEA3BADBC2E69FBE3F852FF";
        harness.SeedUser(email, userState: "Invited", resetPasswordToken: resetToken);

        var result = Mutation.verifyUserEmail(
            null!,
            harness.Db,
            resetToken + "/1/0100019f717663cc-f565d682-04c7-48f3-bb33-a026fa879a0a-000000/PLpMwF7uqI23YFc9zDMyEN-lL_k=473");

        Assert.Null(result.Error);
        Assert.NotNull(result.BearerToken);

        var user = harness.Db.Users.Single(user => user.Email == email);
        Assert.Equal("Active", user.UserState);
        Assert.Equal(string.Empty, user.ResetPasswordToken);
    }

    private sealed class SignupTestHarness : IDisposable
    {
        private readonly SqliteConnection connection;

        private SignupTestHarness(SqliteConnection connection, OnTrackDBContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public OnTrackDBContext Db { get; }

        public static SignupTestHarness Create()
        {
            Environment.SetEnvironmentVariable("ONTRACK_JWT_SIGNING_KEY", "test-signing-key-1234567890-abcdef");
            Environment.SetEnvironmentVariable("ONTRACK_SITE_URL", "https://app.test/");

            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<OnTrackDBContext>()
                .UseSqlite(connection)
                .Options;

            var db = new OnTrackDBContext(options);
            db.Database.EnsureCreated();

            return new SignupTestHarness(connection, db);
        }

        public void SeedUser(
            string email,
            string userState = "Active",
            string resetPasswordToken = "")
        {
            var organization = new UserOrganization
            {
                Id = Guid.NewGuid(),
                CreatorId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Organization = organization,
                OrganizationId = organization.Id,
                Email = email,
                Password = "unused",
                CreatedAt = DateTime.UtcNow,
                ResetPasswordToken = resetPasswordToken,
                UserState = userState,
            };

            organization.CreatorId = user.Id;

            Db.UserOrganizations.Add(organization);
            Db.Users.Add(user);
            Db.SaveChanges();
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }
}
