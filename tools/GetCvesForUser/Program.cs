using System.Text.Json;
using Npgsql;

var options = ParseArgs(args);

if (string.IsNullOrWhiteSpace(options.ConnectionString))
{
    Console.Error.WriteLine("Connection string is required.");
    return 1;
}

if (string.IsNullOrWhiteSpace(options.FullName) && string.IsNullOrWhiteSpace(options.Email))
{
    Console.Error.WriteLine("Provide --full-name or --email.");
    return 1;
}

const string sql = """
SELECT
    u."Email" AS "UserEmail",
    uep."PropertyValue" AS "FullName",
    cve."Id",
    cve."SubmittedAtUtc",
    cve."OriginalEventTimestampUtc",
    cve."Status",
    cve."CountsTowardCve",
    cve."CountedAtUtc",
    cve."RejectionReason",
    cve."Source",
    cve."ExternalSubmissionId",
    cve."ExternalConversionId",
    site."SiteName",
    site."Domain",
    site."TrackingId",
    contract."TierName" AS "ContractTierName",
    campaign."CampaignName",
    cve."TrackingCampaignId",
    cve."TrackerId",
    cve."TrackerClickId",
    cve."DuplicateOfEventId"
FROM "Users" u
LEFT JOIN "UserExtraProperties" uep
    ON uep."ParentId" = u."Id"
    AND uep."PropertyKey" = 'FullName'
INNER JOIN "ConversionVerificationEvents" cve
    ON cve."OrganizationId" = u."OrganizationId"
LEFT JOIN "OrganizationSites" site
    ON cve."SiteId" = site."Id"
LEFT JOIN "OrganizationCveContracts" contract
    ON cve."ContractId" = contract."Id"
LEFT JOIN "TrackingCampaigns" campaign
    ON cve."TrackingCampaignId" = campaign."Id"
WHERE
    (@fullName IS NULL OR uep."PropertyValue" ILIKE @fullNamePattern ESCAPE '\')
    AND (@email IS NULL OR u."Email" ILIKE @emailPattern ESCAPE '\')
ORDER BY cve."SubmittedAtUtc" DESC
""";

var rows = new List<CveRow>();

await using var connection = new NpgsqlConnection(options.ConnectionString);
await connection.OpenAsync();

await using var command = new NpgsqlCommand(sql, connection);
var fullName = string.IsNullOrWhiteSpace(options.FullName) ? null : options.FullName.Trim();
var email = string.IsNullOrWhiteSpace(options.Email) ? null : options.Email.Trim();
var fullNameParameter = new NpgsqlParameter("fullName", NpgsqlTypes.NpgsqlDbType.Text)
{
    Value = fullName ?? (object)DBNull.Value
};
var fullNamePatternParameter = new NpgsqlParameter("fullNamePattern", NpgsqlTypes.NpgsqlDbType.Text)
{
    Value = fullName == null ? DBNull.Value : ToSqlLikePattern(fullName)
};
var emailParameter = new NpgsqlParameter("email", NpgsqlTypes.NpgsqlDbType.Text)
{
    Value = email ?? (object)DBNull.Value
};
var emailPatternParameter = new NpgsqlParameter("emailPattern", NpgsqlTypes.NpgsqlDbType.Text)
{
    Value = email == null ? DBNull.Value : ToSqlLikePattern(email)
};
command.Parameters.Add(fullNameParameter);
command.Parameters.Add(fullNamePatternParameter);
command.Parameters.Add(emailParameter);
command.Parameters.Add(emailPatternParameter);

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    rows.Add(new CveRow
    {
        UserEmail = reader["UserEmail"] as string,
        FullName = reader["FullName"] as string,
        Id = (Guid)reader["Id"],
        SubmittedAtUtc = (DateTime)reader["SubmittedAtUtc"],
        OriginalEventTimestampUtc = GetNullableDateTime(reader, "OriginalEventTimestampUtc"),
        Status = reader["Status"] as string,
        CountsTowardCve = (bool)reader["CountsTowardCve"],
        CountedAtUtc = GetNullableDateTime(reader, "CountedAtUtc"),
        RejectionReason = reader["RejectionReason"] as string,
        Source = reader["Source"] as string,
        ExternalSubmissionId = reader["ExternalSubmissionId"] as string,
        ExternalConversionId = reader["ExternalConversionId"] as string,
        SiteName = reader["SiteName"] as string,
        Domain = reader["Domain"] as string,
        TrackingId = reader["TrackingId"] as string,
        ContractTierName = reader["ContractTierName"] as string,
        CampaignName = reader["CampaignName"] as string,
        TrackingCampaignId = GetNullableGuid(reader, "TrackingCampaignId"),
        TrackerId = GetNullableGuid(reader, "TrackerId"),
        TrackerClickId = GetNullableGuid(reader, "TrackerClickId"),
        DuplicateOfEventId = GetNullableGuid(reader, "DuplicateOfEventId"),
    });
}

var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions
{
    WriteIndented = true
});

Console.WriteLine(json);
return 0;

static Options ParseArgs(string[] args)
{
    var options = new Options();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--full-name":
                options.FullName = ReadValue(args, ref i, "--full-name");
                break;
            case "--email":
                options.Email = ReadValue(args, ref i, "--email");
                break;
            case "--connection-string":
                options.ConnectionString = ReadValue(args, ref i, "--connection-string");
                break;
            default:
                throw new ArgumentException($"Unknown argument '{args[i]}'.");
        }
    }

    return options;
}

static string ReadValue(string[] args, ref int index, string argumentName)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"Missing value for {argumentName}.");

    index++;
    return args[index];
}

static Guid? GetNullableGuid(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}

static DateTime? GetNullableDateTime(NpgsqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
}

static string ToSqlLikePattern(string value)
{
    return value
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_")
        .Replace("*", "%");
}

internal sealed class Options
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? ConnectionString { get; set; }
}

internal sealed class CveRow
{
    public string? UserEmail { get; set; }
    public string? FullName { get; set; }
    public Guid Id { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? OriginalEventTimestampUtc { get; set; }
    public string? Status { get; set; }
    public bool CountsTowardCve { get; set; }
    public DateTime? CountedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public string? Source { get; set; }
    public string? ExternalSubmissionId { get; set; }
    public string? ExternalConversionId { get; set; }
    public string? SiteName { get; set; }
    public string? Domain { get; set; }
    public string? TrackingId { get; set; }
    public string? ContractTierName { get; set; }
    public string? CampaignName { get; set; }
    public Guid? TrackingCampaignId { get; set; }
    public Guid? TrackerId { get; set; }
    public Guid? TrackerClickId { get; set; }
    public Guid? DuplicateOfEventId { get; set; }
}
