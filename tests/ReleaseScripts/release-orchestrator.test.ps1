Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$releaseScript = Join-Path $repoRoot "scripts/release.ps1"
$testRoots = [Collections.Generic.List[string]]::new()

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$MessageContains) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$MessageContains*") {
            throw "Unexpected error '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected failure containing '$MessageContains'."
}

. $releaseScript

Assert-Equal "0.1.1" (Get-NextPatchVersion ([Version]"0.1.0")).ToString(3) "Patch bump failed."
Assert-Equal "0.2.0" (Resolve-TargetVersion ([Version]"0.1.0") "0.2.0").ToString(3) "Explicit version failed."
Assert-Throws { ConvertTo-StrictReleaseVersion "0.1" } "M.m.p"
Assert-Throws { ConvertTo-StrictReleaseVersion "v0.1.1" } "M.m.p"
Assert-Throws { ConvertTo-StrictReleaseVersion "01.1.1" } "M.m.p"
Assert-Throws { Resolve-TargetVersion ([Version]"0.1.0") "0.1.0" } "must be greater"

$propsTemplate = @'
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
  </PropertyGroup>
</Project>
'@

$packageTemplate = @'
{
  "name": "word-search-book-frontend",
  "version": "0.1.0",
  "private": true
}
'@

$lockTemplate = @'
{
  "name": "word-search-book-frontend",
  "version": "0.1.0",
  "lockfileVersion": 3,
  "requires": true,
  "packages": {
    "": {
      "name": "word-search-book-frontend",
      "version": "0.1.0"
    }
  }
}
'@

function New-FakeGh([string]$Directory) {
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory "gh.cmd"), @'
@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0gh.fake.ps1" %*
exit /b %ERRORLEVEL%
'@, [Text.UTF8Encoding]::new($false))

    [IO.File]::WriteAllText((Join-Path $Directory "gh.fake.ps1"), @'
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ArgumentValue([string]$Name) {
    $index = [Array]::IndexOf($Arguments, $Name)
    if ($index -ge 0 -and $index -lt ($Arguments.Count - 1)) { return $Arguments[$index + 1] }
    return ""
}
function Get-CurrentSha { return (& git rev-parse HEAD).Trim() }
function Get-StateValue {
    if (-not $env:FAKE_GH_STATE_FILE -or -not (Test-Path -LiteralPath $env:FAKE_GH_STATE_FILE)) { return 0 }
    $text = [IO.File]::ReadAllText($env:FAKE_GH_STATE_FILE).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return 0 }
    return [int]$text
}
function Set-StateValue([int]$Value) {
    if ($env:FAKE_GH_STATE_FILE) { [IO.File]::WriteAllText($env:FAKE_GH_STATE_FILE, "$Value", [Text.UTF8Encoding]::new($false)) }
}

if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "auth" -and $Arguments[1] -eq "status") { exit 0 }
if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "list") {
    $workflow = Get-ArgumentValue "--workflow"
    $sha = Get-CurrentSha
    if ($workflow -eq "build-and-test.yml") {
        $conclusion = if ($env:FAKE_GH_BUILD_CONCLUSION) { $env:FAKE_GH_BUILD_CONCLUSION } else { "success" }
        $event = if ($env:FAKE_GH_BUILD_EVENT) { $env:FAKE_GH_BUILD_EVENT } else { "push" }
        @(@{ headSha = $sha; headBranch = "main"; event = $event; status = "completed"; conclusion = $conclusion; databaseId = 1001; url = "https://example.invalid/build/1001" }) | ConvertTo-Json -Compress
        exit 0
    }
    if ($workflow -eq "release.yml") {
        @(@{ headSha = $sha; status = "completed"; conclusion = "success"; databaseId = 2001; url = "https://example.invalid/release/2001"; createdAt = "2026-10-01T00:00:00Z" }) | ConvertTo-Json -Compress
        exit 0
    }
}
if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "watch") {
    $remainingFailures = Get-StateValue
    if ($remainingFailures -gt 0) { Set-StateValue ($remainingFailures - 1); exit 1 }
    if ($env:FAKE_GH_RELEASE_STATE_FILE) { [IO.File]::WriteAllText($env:FAKE_GH_RELEASE_STATE_FILE, "visible", [Text.UTF8Encoding]::new($false)) }
    exit 0
}
if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "run" -and $Arguments[1] -eq "rerun") { exit 0 }
if ($Arguments.Count -ge 2 -and $Arguments[0] -eq "release" -and $Arguments[1] -eq "view") {
    if ($env:FAKE_GH_RELEASE_ERROR) { Write-Output $env:FAKE_GH_RELEASE_ERROR; exit 1 }
    if ($env:FAKE_GH_RELEASE_VISIBLE -eq "after-watch") {
        if (-not $env:FAKE_GH_RELEASE_STATE_FILE -or -not (Test-Path -LiteralPath $env:FAKE_GH_RELEASE_STATE_FILE) -or ([IO.File]::ReadAllText($env:FAKE_GH_RELEASE_STATE_FILE).Trim() -ne "visible")) {
            Write-Output "release not found"
            exit 1
        }
    }
    $tag = $Arguments[2]
    $assets = @(@{ name = "WordSearchBook.exe" }, @{ name = "WordSearchBook.exe.sha256" })
    if ($env:FAKE_GH_RELEASE_ASSET_SET -eq "missing") { $assets = @($assets | Select-Object -Skip 1) }
    @{ tagName = $tag; isDraft = ($env:FAKE_GH_RELEASE_DRAFT -eq "true"); isPrerelease = $false; url = "https://example.invalid/releases/$tag"; assets = $assets } | ConvertTo-Json -Compress -Depth 4
    exit 0
}
throw "Unsupported fake gh invocation: $($Arguments -join ' ')"
'@, [Text.UTF8Encoding]::new($false))
    return $Directory
}

function Invoke-TestGit([string]$WorkingDirectory, [string[]]$Arguments) {
    $output = & git -C $WorkingDirectory @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)" }
    return @($output)
}

function New-TestRoot {
    $path = Join-Path $env:TEMP ("WordSearchBook-release-script-test-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $path | Out-Null
    $testRoots.Add($path)
    return $path
}

function New-ReleaseTestRepository([string]$Root, [switch]$RejectAtomicPush) {
    $origin = Join-Path $Root "origin.git"
    $work = Join-Path $Root "work"
    $fakeBin = New-FakeGh (Join-Path $Root "fake-bin")
    & git init --bare $origin | Out-Null
    & git init -b main $work | Out-Null
    Push-Location $work
    try {
        & git config user.name "Release Script Test"
        & git config user.email "release-test@example.invalid"
        New-Item -ItemType Directory -Path "scripts" -Force | Out-Null
        New-Item -ItemType Directory -Path "src/WordSearchBook.Desktop/Frontend" -Force | Out-Null
        Copy-Item $releaseScript "scripts/release.ps1"
        [IO.File]::WriteAllText((Join-Path $work "Directory.Build.props"), $propsTemplate, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $work "src/WordSearchBook.Desktop/Frontend/package.json"), $packageTemplate, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $work "src/WordSearchBook.Desktop/Frontend/package-lock.json"), $lockTemplate, [Text.UTF8Encoding]::new($false))
        & git add .
        & git commit -m "test: initial main" | Out-Null
        & git remote add origin $origin
        & git push -u origin main | Out-Null
        if ($RejectAtomicPush) {
            [IO.File]::WriteAllText((Join-Path $origin "hooks/pre-receive"), "#!/bin/sh`nexit 1`n", [Text.UTF8Encoding]::new($false))
        }
    }
    finally { Pop-Location }

    return @{ Origin = $origin; Work = $work; FakeBin = $fakeBin }
}

function Invoke-TestRelease([hashtable]$Repository, [string]$Version = "", [hashtable]$Environment = @{}) {
    $oldPath = $env:PATH
    $oldValues = @{}
    foreach ($item in $Environment.GetEnumerator()) {
        $oldValues[$item.Key] = [Environment]::GetEnvironmentVariable($item.Key, "Process")
        Set-Item -Path "Env:$($item.Key)" -Value ([string]$item.Value)
    }
    try {
        $env:PATH = "$($Repository.FakeBin);$oldPath"
        Push-Location $Repository.Work
        $arguments = @("-NoProfile", "-File", "scripts/release.ps1")
        if ($Version) { $arguments += @("-Version", $Version) }
        $output = & pwsh @arguments 2>&1
        return @{ ExitCode = $LASTEXITCODE; Output = @($output) }
    }
    finally {
        Pop-Location
        $env:PATH = $oldPath
        foreach ($item in $Environment.GetEnumerator()) {
            if ($null -eq $oldValues[$item.Key]) { Remove-Item -Path "Env:$($item.Key)" -ErrorAction SilentlyContinue }
            else { Set-Item -Path "Env:$($item.Key)" -Value $oldValues[$item.Key] }
        }
    }
}

function Get-RemoteFile([hashtable]$Repository, [string]$Path) {
    return (& git "--git-dir=$($Repository.Origin)" show "refs/heads/main:$Path") -join "`n"
}
function Get-TestRemoteTag([hashtable]$Repository, [string]$Tag) {
    $value = & git "--git-dir=$($Repository.Origin)" rev-parse "refs/tags/$Tag" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($value -join "").Trim()
}

try {
    $defaultRepo = New-ReleaseTestRepository (New-TestRoot)
    $defaultResult = Invoke-TestRelease $defaultRepo
    if ($defaultResult.ExitCode -ne 0) {
        throw "Default patch release failed: $($defaultResult.Output -join [Environment]::NewLine)"
    }
    Assert-True ((Get-RemoteFile $defaultRepo "Directory.Build.props") -match '<Version>0.1.1</Version>') "Props version was not bumped."
    Assert-True ((Get-RemoteFile $defaultRepo "src/WordSearchBook.Desktop/Frontend/package.json") -match '"version": "0.1.1"') "Package version was not bumped."
    Assert-True ((Get-RemoteFile $defaultRepo "src/WordSearchBook.Desktop/Frontend/package-lock.json") -match '(?s)"version": "0.1.1".*"version": "0.1.1"') "Lock versions were not bumped."
    Assert-True ($null -ne (Get-TestRemoteTag $defaultRepo "v0.1.1")) "Default release tag was not pushed."
    Assert-Equal "chore: release v0.1.1" ((@(Invoke-TestGit $defaultRepo.Work @("log", "-1", "--format=%s")))[0].Trim()) "Release commit mismatch."

    $explicitRepo = New-ReleaseTestRepository (New-TestRoot)
    $explicitResult = Invoke-TestRelease $explicitRepo "0.2.0"
    Assert-Equal 0 $explicitResult.ExitCode "Explicit release failed."
    Assert-True ($null -ne (Get-TestRemoteTag $explicitRepo "v0.2.0")) "Explicit tag was not pushed."

    $dirtyRepo = New-ReleaseTestRepository (New-TestRoot)
    [IO.File]::WriteAllText((Join-Path $dirtyRepo.Work "dirty.txt"), "dirty")
    Assert-True ((Invoke-TestRelease $dirtyRepo).ExitCode -ne 0) "Dirty tree was accepted."

    $branchRepo = New-ReleaseTestRepository (New-TestRoot)
    Invoke-TestGit $branchRepo.Work @("checkout", "-b", "feature/test") | Out-Null
    Assert-True ((Invoke-TestRelease $branchRepo).ExitCode -ne 0) "Non-main branch was accepted."

    $ciRepo = New-ReleaseTestRepository (New-TestRoot)
    Assert-True ((Invoke-TestRelease $ciRepo -Environment @{ FAKE_GH_BUILD_CONCLUSION = "failure" }).ExitCode -ne 0) "Failed CI was accepted."

    $mismatchRepo = New-ReleaseTestRepository (New-TestRoot)
    $packagePath = Join-Path $mismatchRepo.Work "src/WordSearchBook.Desktop/Frontend/package.json"
    [IO.File]::WriteAllText($packagePath, $packageTemplate.Replace('"version": "0.1.0"', '"version": "0.0.9"'))
    Invoke-TestGit $mismatchRepo.Work @("add", ".") | Out-Null
    Invoke-TestGit $mismatchRepo.Work @("commit", "-m", "test: mismatch") | Out-Null
    Invoke-TestGit $mismatchRepo.Work @("push", "origin", "main") | Out-Null
    Assert-True ((Invoke-TestRelease $mismatchRepo).ExitCode -ne 0) "Mismatched source versions were accepted."

    $resumeRepo = New-ReleaseTestRepository (New-TestRoot)
    $watchState = Join-Path (Split-Path $resumeRepo.Work -Parent) "watch-state.txt"
    $releaseState = Join-Path (Split-Path $resumeRepo.Work -Parent) "release-state.txt"
    [IO.File]::WriteAllText($watchState, "2")
    $resumeEnvironment = @{ FAKE_GH_STATE_FILE = $watchState; FAKE_GH_RELEASE_STATE_FILE = $releaseState; FAKE_GH_RELEASE_VISIBLE = "after-watch" }
    Assert-True ((Invoke-TestRelease $resumeRepo -Environment $resumeEnvironment).ExitCode -ne 0) "Failed publication was accepted."
    Assert-True ($null -ne (Get-TestRemoteTag $resumeRepo "v0.1.1")) "Failed publication lost its remote tag."
    Assert-Equal 0 (Invoke-TestRelease $resumeRepo -Environment $resumeEnvironment).ExitCode "Publication did not resume."
    Assert-True ($null -eq (Get-TestRemoteTag $resumeRepo "v0.1.2")) "Resume created a new patch tag."

    $assetRepo = New-ReleaseTestRepository (New-TestRoot)
    Assert-True ((Invoke-TestRelease $assetRepo -Environment @{ FAKE_GH_RELEASE_ASSET_SET = "missing" }).ExitCode -ne 0) "Incomplete asset set was accepted."

    $rollbackRepo = New-ReleaseTestRepository (New-TestRoot) -RejectAtomicPush
    $originalSha = ((@(Invoke-TestGit $rollbackRepo.Work @("rev-parse", "HEAD")))[0]).Trim()
    Assert-True ((Invoke-TestRelease $rollbackRepo).ExitCode -ne 0) "Rejected atomic push was accepted."
    Assert-Equal $originalSha ((@(Invoke-TestGit $rollbackRepo.Work @("rev-parse", "HEAD")))[0]).Trim() "Local HEAD was not restored."
    Assert-True ($null -eq (Get-TestRemoteTag $rollbackRepo "v0.1.1")) "Rollback left a remote tag."

    Write-Output "all release orchestrator tests passed"
}
finally {
    foreach ($path in $testRoots) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}
