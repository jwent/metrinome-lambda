param(
    [string]$FullName,
    [string]$Email = "cve-test-*",
    [string]$ConnectionString = $env:ONTRACK_DATABASE_CONNECT_STRING,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ONTRACK_DATABASE_CONNECT_STRING is not set. Pass -ConnectionString or export the environment variable first."
}

if ([string]::IsNullOrWhiteSpace($FullName) -and [string]::IsNullOrWhiteSpace($Email)) {
    throw "Provide -FullName or -Email."
}

$helperProject = Join-Path $PSScriptRoot "tools\GetCvesForUser\GetCvesForUser.csproj"
$helperDll = Join-Path $PSScriptRoot "tools\GetCvesForUser\bin\Debug\net10.0\GetCvesForUser.dll"

if (-not (Test-Path $helperProject)) {
    throw "Helper project not found at $helperProject"
}

& dotnet build $helperProject -nologo -clp:ErrorsOnly | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build CVE helper project."
}

$arguments = @($helperDll)

if (-not [string]::IsNullOrWhiteSpace($FullName)) {
    $arguments += "--full-name"
    $arguments += $FullName.Trim()
}

if (-not [string]::IsNullOrWhiteSpace($Email)) {
    $arguments += "--email"
    $arguments += $Email.Trim()
}

$arguments += "--connection-string"
$arguments += $ConnectionString

$json = & dotnet $arguments 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to query CVEs.`n$json"
}

if ($AsJson) {
    $json
    return
}

$rows = $json | ConvertFrom-Json
if ($null -eq $rows -or @($rows).Count -eq 0) {
    Write-Warning "No CVEs found for the supplied user filter."
    return
}

$rows | Format-Table `
    UserEmail, `
    FullName, `
    SubmittedAtUtc, `
    Status, `
    CountsTowardCve, `
    Domain, `
    CampaignName, `
    ContractTierName, `
    TrackerClickId `
    -AutoSize
