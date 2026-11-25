<#
.SYNOPSIS
    Local smoke test for the Docker setup (PowerShell twin of scripts/smoke.sh).

.DESCRIPTION
    Builds the images, checks that the api refuses to start without a token, starts the api, runs the scraper on the
    saved fixtures (no network), and then calls the main endpoints with curl. Whatever happens, it removes the
    containers and the volume it created. It uses its own compose project name and an ephemeral admin token, so it
    never touches the data or the .env of a normal local run. Works in Windows PowerShell 5.1 and PowerShell 7.

    Needs: docker with the compose plugin, and curl.exe (included in Windows 10 and later).

    Exit codes:
      0  all checks passed
      10 docker, docker compose or curl is missing
      11 image build failed
      12 the api (or compose) did not refuse to start without ADMIN_TOKEN
      13 the api did not become healthy
      14 the scraper failed
      20 /health                      21 /api/v1/cinemas
      22 /api/v1/films                23 /api/v1/showings
      24 admin endpoint accepted a request without the token
      25 admin endpoint rejected the right token or returned unexpected counts
      26 a second scraper run changed the data
      27 a failing source emptied the data or the scraper did not report the failure

.EXAMPLE
    ./scripts/smoke.ps1
    $env:SMOKE_API_PORT = 19000; ./scripts/smoke.ps1
#>
[CmdletBinding()]
param()

# Native commands print progress on stderr. Keep the default so it is shown rather than turned into errors.
$ErrorActionPreference = 'Continue'

Push-Location (Join-Path $PSScriptRoot '..')

$ExpectedCounts = '{"cinemas":1,"films":2,"showings":2}'
$Started = Get-Date
$script:Base = ''

# Names of the environment variables this script sets. Their original values are restored at the end, because
# `$env:` changes would otherwise stay in the caller's session.
$ManagedVariables = @(
    'COMPOSE_PROJECT_NAME', 'API_PORT', 'SCRAPER_MODE', 'SCRAPER_FIXTURES_CASE', 'FIXTURES_DIR',
    'CINEPOSTO_DB_PATH', 'SCRAPER_USER_AGENT', 'SCRAPER_HTTP_TIMEOUT_SECONDS', 'ADMIN_TOKEN'
)

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" }
function Write-Pass([string]$Text) { Write-Host "PASS  $Text" -ForegroundColor Green }

# Runs docker and captures stdout and stderr together, without PowerShell turning stderr into error records.
function Invoke-Docker([string[]]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'docker'
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Output = ($stdout.Result + $stderr.Result).TrimEnd() }
}

# Prints what failed and the last logs of the service involved, then stops the run with the given exit code.
function Stop-Smoke([int]$Code, [string]$Service, [string]$Message, [string]$Details = '') {
    Write-Host "FAIL  $Message" -ForegroundColor Red
    if ($Details) {
        Write-Host '--- details ---' -ForegroundColor Red
        Write-Host $Details
    }
    if ($Service) {
        Write-Host "--- last logs of service '$Service' ---" -ForegroundColor Red
        $logs = Invoke-Docker @('compose', '--profile', 'jobs', 'logs', '--no-color', '--tail', '100', $Service)
        Write-Host $logs.Output
    }
    throw "SMOKE_EXIT:$Code"
}

# Calls the api with curl.exe. The status code is appended to the body by curl itself.
function Get-Http([string]$Path, [string[]]$ExtraArguments = @()) {
    $output = & curl.exe -sS --max-time 10 -w "`n%{http_code}" @ExtraArguments "$($script:Base)$Path" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $output) {
        return [pscustomobject]@{ Status = '000'; Body = '' }
    }
    $text = ($output -join "`n")
    $cut = $text.LastIndexOf("`n")
    if ($cut -lt 0) {
        return [pscustomobject]@{ Status = '000'; Body = $text }
    }
    [pscustomobject]@{ Status = $text.Substring($cut + 1).Trim(); Body = $text.Substring(0, $cut) }
}

function Test-Data([string]$Name, [int]$Code, [string]$Path, [string]$Needle) {
    $response = Get-Http $Path
    if ($response.Status -ne '200') { Stop-Smoke $Code 'api' "${Name}: expected 200 from $Path, got $($response.Status)" $response.Body }
    if ([string]::IsNullOrWhiteSpace($response.Body) -or $response.Body -in @('[]', '{}')) { Stop-Smoke $Code 'api' "${Name}: $Path returned no data" $response.Body }
    if (-not $response.Body.Contains($Needle)) { Stop-Smoke $Code 'api' "${Name}: $Path does not contain '$Needle'" $response.Body }
    Write-Pass "$Name  (200, contains '$Needle')"
}

function New-Token {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $generator.GetBytes($bytes)
    $generator.Dispose()
    -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function Invoke-Smoke {
    Write-Step '0/9 prerequisites'
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Stop-Smoke 10 '' 'docker is not installed or not on PATH' }
    if ((Invoke-Docker @('compose', 'version')).ExitCode -ne 0) { Stop-Smoke 10 '' 'the docker compose plugin is not available' }
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) { Stop-Smoke 10 '' 'curl.exe is not installed or not on PATH' }

    # Isolation: these values win over .env, so the run is the same on every machine.
    $port = if ($env:SMOKE_API_PORT) { $env:SMOKE_API_PORT } else { '18080' }
    $project = if ($env:SMOKE_PROJECT) { $env:SMOKE_PROJECT } else { 'cineposto-smoke' }
    $env:COMPOSE_PROJECT_NAME = $project
    $env:API_PORT = $port
    $env:SCRAPER_MODE = 'fixtures'
    $env:SCRAPER_FIXTURES_CASE = 'settings_valid'
    $env:FIXTURES_DIR = './tests/CinePosto.Connectors.Tests/Golden'
    $env:CINEPOSTO_DB_PATH = '/data/cineposto.db'
    # An empty value deletes the variable on Windows, so these two fall back to the defaults of docker-compose.yml.
    Remove-Item Env:SCRAPER_USER_AGENT -ErrorAction SilentlyContinue
    $env:SCRAPER_HTTP_TIMEOUT_SECONDS = '30'
    $token = New-Token
    $env:ADMIN_TOKEN = $token
    $script:Base = "http://127.0.0.1:$port"
    Write-Pass "docker, compose and curl.exe found; project '$project', api on port $port"

    Write-Step '1/9 build the images'
    & docker compose --profile jobs build
    if ($LASTEXITCODE -ne 0) { Stop-Smoke 11 '' 'docker compose build failed' }
    Write-Pass 'images built'

    Write-Step '2/9 the api must refuse to start without a token'
    $apiImage = 'cineposto-api:local'
    $images = Invoke-Docker @('compose', 'config', '--images')
    $found = $images.Output -split "`r?`n" | Where-Object { $_ -like '*cineposto-api*' } | Select-Object -First 1
    if ($found) { $apiImage = $found.Trim() }
    $run = Invoke-Docker @('run', '--rm', '--pull=never', $apiImage)
    if ($run.ExitCode -eq 0) { Stop-Smoke 12 '' 'the api image started without ADMIN_TOKEN' $run.Output }
    if ($run.Output -notmatch 'ADMIN_TOKEN') { Stop-Smoke 12 '' "the api image exited ($($run.ExitCode)) without explaining that ADMIN_TOKEN is missing" $run.Output }
    Write-Pass "image exits with code $($run.ExitCode) and says ADMIN_TOKEN is missing"
    # A variable cannot be set to empty on Windows, so remove it and point compose at an empty env file instead of .env.
    $emptyEnvFile = [System.IO.Path]::GetTempFileName()
    try {
        Remove-Item Env:ADMIN_TOKEN
        $config = Invoke-Docker @('compose', '--env-file', $emptyEnvFile, 'config', '--quiet')
    }
    finally {
        $env:ADMIN_TOKEN = $token
        Remove-Item -LiteralPath $emptyEnvFile -Force -ErrorAction SilentlyContinue
    }
    if ($config.ExitCode -eq 0) { Stop-Smoke 12 '' 'docker compose accepted a missing ADMIN_TOKEN' }
    if ($config.Output -notmatch 'ADMIN_TOKEN') { Stop-Smoke 12 '' 'docker compose failed, but not because of ADMIN_TOKEN' $config.Output }
    Write-Pass 'docker compose rejects a missing ADMIN_TOKEN'

    Write-Step '3/9 start the api and wait for the healthcheck'
    & docker compose up -d --wait --wait-timeout 120 api
    if ($LASTEXITCODE -ne 0) { Stop-Smoke 13 'api' 'the api did not become healthy within 120s' }
    Write-Pass 'api is healthy'

    Write-Step '4/9 run the scraper on the saved fixtures'
    $scrape = Invoke-Docker @('compose', 'run', '-T', '--rm', 'scraper')
    Write-Host $scrape.Output
    if ($scrape.ExitCode -ne 0) { Stop-Smoke 14 'api' "the scraper exited with code $($scrape.ExitCode)" $scrape.Output }
    if (-not $scrape.Output.Contains('films=2')) { Stop-Smoke 14 'api' 'the scraper did not report 2 films' $scrape.Output }
    Write-Pass 'scraper finished with exit code 0'

    Write-Step '5/9 public endpoints'
    Test-Data '/health' 20 '/health' 'Healthy'
    Test-Data 'cinemas' 21 '/api/v1/cinemas' 'clarici-foligno'
    Test-Data 'films' 22 '/api/v1/films' 'Nebbia sul Lago'
    Test-Data 'showings' 23 '/api/v1/showings' 'clarici-foligno'

    Write-Step '6/9 admin endpoint'
    $admin = Get-Http '/api/v1/admin/dataset-info'
    if ($admin.Status -ne '401') { Stop-Smoke 24 'api' "admin endpoint without a token: expected 401, got $($admin.Status)" $admin.Body }
    Write-Pass 'no token -> 401'
    $admin = Get-Http '/api/v1/admin/dataset-info' @('-H', 'X-Admin-Token: wrong-token-wrong-token')
    if ($admin.Status -ne '401') { Stop-Smoke 24 'api' "admin endpoint with a wrong token: expected 401, got $($admin.Status)" $admin.Body }
    Write-Pass 'wrong token -> 401'
    $admin = Get-Http '/api/v1/admin/dataset-info' @('-H', "X-Admin-Token: $token")
    if ($admin.Status -ne '200') { Stop-Smoke 25 'api' "admin endpoint with the right token: expected 200, got $($admin.Status)" $admin.Body }
    if ($admin.Body -ne $ExpectedCounts) { Stop-Smoke 25 'api' "unexpected counts: wanted $ExpectedCounts" $admin.Body }
    Write-Pass "right token -> 200 $($admin.Body)"

    Write-Step '7/9 a second scraper run must not change the data'
    $scrape = Invoke-Docker @('compose', 'run', '-T', '--rm', 'scraper')
    if ($scrape.ExitCode -ne 0) { Stop-Smoke 26 'api' "the second scraper run exited with code $($scrape.ExitCode)" $scrape.Output }
    $admin = Get-Http '/api/v1/admin/dataset-info' @('-H', "X-Admin-Token: $token")
    if ($admin.Body -ne $ExpectedCounts) { Stop-Smoke 26 'api' "counts changed after the second run: $($admin.Body)" $admin.Body }
    Write-Pass "counts unchanged: $($admin.Body)"

    Write-Step '8/9 a failing source must keep the last known showings'
    $env:SCRAPER_FIXTURES_CASE = 'movies_http500'
    $scrape = Invoke-Docker @('compose', 'run', '-T', '--rm', 'scraper')
    $env:SCRAPER_FIXTURES_CASE = 'settings_valid'
    Write-Host $scrape.Output
    if ($scrape.ExitCode -ne 1) { Stop-Smoke 27 'api' "the scraper should exit with code 1 when the source fails, got $($scrape.ExitCode)" $scrape.Output }
    $admin = Get-Http '/api/v1/admin/dataset-info' @('-H', "X-Admin-Token: $token")
    if ($admin.Body -ne $ExpectedCounts) { Stop-Smoke 27 'api' "the failing run changed the data: $($admin.Body)" $admin.Body }
    Write-Pass "scraper reported the failure (exit 1) and the data is still $($admin.Body)"

    Write-Step '9/9 the api is still healthy'
    Test-Data '/health' 20 '/health' 'Healthy'
}

# Remember the caller's values so they can be put back, then run.
$saved = @{}
foreach ($name in $ManagedVariables) { $saved[$name] = [System.Environment]::GetEnvironmentVariable($name) }

$exitCode = 0
try {
    Invoke-Smoke
}
catch {
    if ($_.Exception.Message -match '^SMOKE_EXIT:(\d+)$') {
        $exitCode = [int]$Matches[1]
    }
    else {
        Write-Host "FAIL  unexpected error: $($_.Exception.Message)" -ForegroundColor Red
        $exitCode = 1
    }
}
finally {
    Write-Host ''
    Write-Host '==> cleaning up (docker compose down --volumes)'
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $null = Invoke-Docker @('compose', '--profile', 'jobs', 'down', '--volumes', '--remove-orphans')
    }
    foreach ($name in $ManagedVariables) { [System.Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Pop-Location
}

$seconds = [int]((Get-Date) - $Started).TotalSeconds
if ($exitCode -eq 0) {
    Write-Host "SMOKE PASSED in ${seconds}s" -ForegroundColor Green
}
else {
    Write-Host "SMOKE FAILED (exit code $exitCode) after ${seconds}s" -ForegroundColor Red
}
exit $exitCode
