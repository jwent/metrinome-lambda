param(
    [string]$CampaignId = "1f7ab636-8d45-4568-b617-42b71f5a0224",

    [string]$ConnectionString = $env:ONTRACK_DATABASE_CONNECT_STRING,

    [string]$PsqlPath = "psql"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ConnectionStringToPsqlArgs {
    param([Parameter(Mandatory = $true)][string]$RawConnectionString)

    $normalized = $RawConnectionString.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "Connection string is empty."
    }

    if ($normalized -match "^[a-zA-Z][a-zA-Z0-9+\-.]*://") {
        return @($normalized)
    }

    $dbHost = $null
    $dbPort = $null
    $dbName = $null
    $dbUser = $null
    $dbPassword = $null
    $sslMode = $null

    foreach ($part in $normalized.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $segment = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($segment) -or -not $segment.Contains("=")) {
            continue
        }

        $kv = $segment.Split('=', 2)
        $key = $kv[0].Trim().ToLowerInvariant()
        $value = $kv[1].Trim().Trim('"')

        switch ($key) {
            "host" { $dbHost = $value; continue }
            "server" { $dbHost = $value; continue }
            "port" { $dbPort = $value; continue }
            "database" { $dbName = $value; continue }
            "dbname" { $dbName = $value; continue }
            "username" { $dbUser = $value; continue }
            "user id" { $dbUser = $value; continue }
            "userid" { $dbUser = $value; continue }
            "user" { $dbUser = $value; continue }
            "password" { $dbPassword = $value; continue }
            "ssl mode" { $sslMode = $value; continue }
            "sslmode" { $sslMode = $value; continue }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($dbPassword)) {
        $env:PGPASSWORD = $dbPassword
    }
    if (-not [string]::IsNullOrWhiteSpace($sslMode) -and $sslMode -notmatch "disable") {
        $env:PGSSLMODE = "require"
    }

    $args = @()
    if (-not [string]::IsNullOrWhiteSpace($dbHost)) { $args += @("-h", $dbHost) }
    if (-not [string]::IsNullOrWhiteSpace($dbPort)) { $args += @("-p", $dbPort) }
    if (-not [string]::IsNullOrWhiteSpace($dbUser)) { $args += @("-U", $dbUser) }
    if (-not [string]::IsNullOrWhiteSpace($dbName)) { $args += @("-d", $dbName) }

    if ($args.Count -eq 0) {
        $args += $normalized
    }

    return $args
}

function Get-LaunchSettingsConnectionString {
    $launchSettingsPath = Join-Path $PSScriptRoot "..\Properties\launchSettings.json"
    if (-not (Test-Path -LiteralPath $launchSettingsPath)) {
        return $null
    }

    $launchSettings = Get-Content -LiteralPath $launchSettingsPath -Raw | ConvertFrom-Json
    return $launchSettings.profiles.OnTrackGraphQLServer.environmentVariables.ONTRACK_DATABASE_CONNECT_STRING
}

$parsedCampaignId = [Guid]::Empty
if (-not [Guid]::TryParse($CampaignId, [ref]$parsedCampaignId)) {
    throw "CampaignId must be a valid GUID."
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = Get-LaunchSettingsConnectionString
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is required. Provide -ConnectionString, set ONTRACK_DATABASE_CONNECT_STRING, or add it to metrinome-lambda\Properties\launchSettings.json."
}

if (-not (Get-Command $PsqlPath -ErrorAction SilentlyContinue)) {
    throw "psql not found in PATH. Install PostgreSQL client tools or pass -PsqlPath with a full path."
}

$escapedCampaignId = $CampaignId.Replace("'", "''")
$sql = @"
\set ON_ERROR_STOP on

WITH campaign_context AS (
    SELECT
        campaign."Id" AS "CampaignId",
        campaign."ParentTrackerId" AS "TrackerId",
        tracker."OrganizationId" AS "OrganizationId",
        COALESCE(
            NULLIF(campaign."WebsiteDomain", ''),
            NULLIF(regexp_replace(COALESCE(NULLIF(campaign."LandingPageURL", ''), 'unknown'), '^https?://([^/?#]+).*$', '\1'), ''),
            'unknown'
        ) AS "Host"
    FROM public."TrackingCampaigns" campaign
    JOIN public."UserTrackers" tracker
        ON tracker."Id" = campaign."ParentTrackerId"
    WHERE campaign."Id" = '$escapedCampaignId'::uuid
    LIMIT 1
),
active_contract AS (
    SELECT contract."Id"
    FROM public."OrganizationCveContracts" contract
    JOIN campaign_context context
        ON context."OrganizationId" = contract."OrganizationId"
    WHERE contract."ContractStartDate" <= NOW()
        AND contract."ContractEndDate" >= NOW()
    ORDER BY contract."ContractStartDate" DESC
    LIMIT 1
),
matching_site AS (
    SELECT site."Id"
    FROM public."OrganizationSites" site
    JOIN campaign_context context
        ON context."OrganizationId" = site."OrganizationId"
    WHERE lower(trim(both '.' from site."Domain")) = lower(trim(both '.' from context."Host"))
        OR site."TrackingId" = context."TrackerId"::text
    ORDER BY
        CASE
            WHEN lower(trim(both '.' from site."Domain")) = lower(trim(both '.' from context."Host")) THEN 0
            ELSE 1
        END
    LIMIT 1
),
inserted_site AS (
    INSERT INTO public."OrganizationSites" (
        "Id",
        "OrganizationId",
        "SiteName",
        "Domain",
        "TrackingId",
        "IsActive",
        "CreatedAt",
        "UpdatedAt"
    )
    SELECT
        gen_random_uuid(),
        context."OrganizationId",
        context."Host",
        context."Host",
        context."TrackerId"::text,
        true,
        NOW(),
        NOW()
    FROM campaign_context context
    WHERE NOT EXISTS (SELECT 1 FROM matching_site)
    RETURNING "Id"
),
resolved_site AS (
    SELECT "Id" FROM matching_site
    UNION ALL
    SELECT "Id" FROM inserted_site
    LIMIT 1
),
inserted_event AS (
    INSERT INTO public."ConversionVerificationEvents" (
        "Id",
        "OrganizationId",
        "SiteId",
        "ContractId",
        "TrackerId",
        "TrackingCampaignId",
        "TrackerClickId",
        "ExternalSubmissionId",
        "ExternalConversionId",
        "IdempotencyKey",
        "SubmittedAtUtc",
        "OriginalEventTimestampUtc",
        "Status",
        "CountsTowardCve",
        "CountedAtUtc",
        "DuplicateOfEventId",
        "RejectionReason",
        "RequestHash",
        "Source",
        "CreatedAtUtc",
        "UpdatedAtUtc"
    )
    SELECT
        gen_random_uuid(),
        context."OrganizationId",
        site."Id",
        contract."Id",
        context."TrackerId",
        context."CampaignId",
        NULL,
        gen_random_uuid()::text,
        gen_random_uuid()::text,
        'local-campaign-unmatched-signal:' || context."CampaignId"::text || ':' || gen_random_uuid()::text,
        NOW(),
        NOW(),
        'Unmatched',
        true,
        NOW(),
        NULL,
        NULL,
        md5(context."CampaignId"::text || ':' || NOW()::text || ':' || random()::text),
        'local_campaign_unmatched_signal',
        NOW(),
        NOW()
    FROM campaign_context context
    CROSS JOIN resolved_site site
    LEFT JOIN active_contract contract ON true
    RETURNING
        "Id",
        "OrganizationId",
        "SiteId",
        "ContractId",
        "TrackerId",
        "TrackingCampaignId",
        "Status",
        "CountsTowardCve",
        "Source",
        "SubmittedAtUtc"
)
SELECT *
FROM inserted_event;
"@

$tempSqlPath = Join-Path ([System.IO.Path]::GetTempPath()) ("create_campaign_unmatched_signal_{0}.sql" -f [Guid]::NewGuid().ToString("N"))
[System.IO.File]::WriteAllText($tempSqlPath, $sql, (New-Object System.Text.UTF8Encoding($false)))

$connectionArgs = Convert-ConnectionStringToPsqlArgs -RawConnectionString $ConnectionString
$arguments = @()
$arguments += $connectionArgs
$arguments += @("-v", "ON_ERROR_STOP=1", "-f", $tempSqlPath)

Write-Host "Creating campaign-scoped unmatched signal for campaign: $CampaignId"
Write-Host ("Using psql target args: {0}" -f ($connectionArgs -join " "))

try {
    & $PsqlPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "psql exited with code $LASTEXITCODE while creating campaign unmatched signal."
    }
}
finally {
    Remove-Item -LiteralPath $tempSqlPath -ErrorAction SilentlyContinue
}
