# AWS RDS Connection Info
$DB_HOST = "ontrackdb-us-east-1-1.citzbss5x6lu.us-east-1.rds.amazonaws.com"
$DB_PORT = "5432"
$DB_NAME = "ontrack"
$DB_USER = "aura"
$DB_PASS = "3MgzQXHGfJjzeiPG"

# Build connection string
$CONN = "Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS;SSL Mode=Require;Trust Server Certificate=true"

Write-Output "Applying EF Core migrations to $DB_NAME on $DB_HOST ..."

# Try to find the project file automatically
$ProjectPath = Get-ChildItem -Recurse -Filter OnTrackGraphQLServer.csproj | Select-Object -First 1 -ExpandProperty FullName

Write-Output $ProjectPath

if (-not $ProjectPath) {
    Write-Error "Could not find OnTrackGraphQLServer.csproj in this repo. Make sure you run this script from the repo root."
    exit 1
}

Write-Warning "Found project: $ProjectPath"

# Run EF Core migrations
dotnet ef database update --project "$ProjectPath" --connection "$CONN"

if ($LASTEXITCODE -ne 0) {
    Write-Error "EF Core migration failed."
    exit 1
}

Write-Warning "EF Core migrations applied successfully."

# Check that psql is installed
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Warning "⚠️ 'psql' not found on PATH. Skipping table verification."
    exit 0
}

Write-Warning "Verifying created tables in Postgres..."

# Use psql to list tables
$env:PGPASSWORD = $DB_PASS
psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $DB_NAME -c "\dt"
