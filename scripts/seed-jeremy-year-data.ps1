param(
    [ValidateSet("Local", "Prod")]
    [string]$Target = "Local",

    [string]$Email = "jeremy.mark.went@gmail.com",
    [string]$CampaignName = "Brand Search Demo",
    [int]$Year = 2026,
    [string]$StartDate = "2026-01-01",
    [string]$EndDate = (Get-Date).ToString("yyyy-MM-dd"),

    [int]$MinClicksPerDay = 5,
    [int]$MaxClicksPerDay = 18,
    [int]$BotEveryNDays = 3,
    [int]$ConversionEveryNDays = 2,
    [int]$DuplicateEveryNConversions = 3,

    [string]$ConnectionString = $env:ONTRACK_DATABASE_CONNECT_STRING,

    [switch]$AllowProdWrite
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ONTRACK_DATABASE_CONNECT_STRING is not set. Pass -ConnectionString or export the environment variable first."
}

if ($Target -eq "Prod" -and -not $AllowProdWrite) {
    throw "Refusing to write to Prod without -AllowProdWrite."
}

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    throw "'psql' was not found on PATH. Install PostgreSQL client tools or run the SQL file manually."
}

$scriptPath = Join-Path $PSScriptRoot "seed-jeremy-year-data.sql"
if (-not (Test-Path $scriptPath)) {
    throw "Seed SQL file was not found: $scriptPath"
}

function ConvertTo-ConnectionParts {
    param([Parameter(Mandatory = $true)][string]$RawConnectionString)

    $parts = @{}
    foreach ($segment in ($RawConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or -not $segment.Contains('=')) {
            continue
        }

        $key, $value = $segment -split '=', 2
        $normalizedKey = $key.Trim().ToLowerInvariant().Replace(' ', '')
        $parts[$normalizedKey] = $value.Trim()
    }

    $hostName = $parts['host']
    if ([string]::IsNullOrWhiteSpace($hostName)) {
        $hostName = $parts['server']
    }

    $port = $parts['port']
    if ([string]::IsNullOrWhiteSpace($port)) {
        $port = "5432"
    }

    $database = $parts['database']
    if ([string]::IsNullOrWhiteSpace($database)) {
        $database = $parts['initialcatalog']
    }

    $username = $parts['username']
    if ([string]::IsNullOrWhiteSpace($username)) {
        $username = $parts['userid']
    }
    if ([string]::IsNullOrWhiteSpace($username)) {
        $username = $parts['user']
    }

    $password = $parts['password']

    if ([string]::IsNullOrWhiteSpace($hostName) -or
        [string]::IsNullOrWhiteSpace($database) -or
        [string]::IsNullOrWhiteSpace($username)) {
        throw "Connection string must include Host, Database, and Username/User ID."
    }

    [pscustomobject]@{
        Host = $hostName
        Port = $port
        Database = $database
        Username = $username
        Password = $password
    }
}

$connection = ConvertTo-ConnectionParts -RawConnectionString $ConnectionString

$start = [DateTime]::Parse($StartDate).ToString("yyyy-MM-dd")
$end = [DateTime]::Parse($EndDate).ToString("yyyy-MM-dd")

if ([DateTime]::Parse($end) -lt [DateTime]::Parse($start)) {
    throw "-EndDate must be on or after -StartDate."
}

Write-Host "Seeding $Target database '$($connection.Database)' on '$($connection.Host)' for $Email..." -ForegroundColor Cyan
Write-Host "Campaign: $CampaignName" -ForegroundColor Cyan
Write-Host "Date range: $start through $end" -ForegroundColor Cyan

$previousPassword = $env:PGPASSWORD
try {
    if (-not [string]::IsNullOrWhiteSpace($connection.Password)) {
        $env:PGPASSWORD = $connection.Password
    }

    $arguments = @(
        "-v", "ON_ERROR_STOP=1",
        "-v", "seed_email=$Email",
        "-v", "campaign_name=$CampaignName",
        "-v", "seed_year=$Year",
        "-v", "start_date=$start",
        "-v", "end_date=$end",
        "-v", "min_clicks_per_day=$MinClicksPerDay",
        "-v", "max_clicks_per_day=$MaxClicksPerDay",
        "-v", "bot_every_n_days=$BotEveryNDays",
        "-v", "conversion_every_n_days=$ConversionEveryNDays",
        "-v", "duplicate_every_n_conversions=$DuplicateEveryNConversions",
        "-h", $connection.Host,
        "-p", $connection.Port,
        "-U", $connection.Username,
        "-d", $connection.Database,
        "-f", $scriptPath
    )

    & psql @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:PGPASSWORD = $previousPassword
}

Write-Host "Seed complete." -ForegroundColor Green
