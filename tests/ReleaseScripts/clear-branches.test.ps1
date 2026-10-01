Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$sourceScript = Join-Path $repoRoot "scripts/clear-branches.ps1"
$testRoot = Join-Path $env:TEMP ("WordSearchBook-clear-branches-test-" + [guid]::NewGuid().ToString("N"))
$origin = Join-Path $testRoot "origin.git"
$work = Join-Path $testRoot "work"

function Invoke-Git([string[]]$Arguments) {
    $output = & git -C $work @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)" }
    return @($output)
}

function Test-LocalBranch([string]$Branch) {
    & git -C $work show-ref --verify --quiet "refs/heads/$Branch"
    return $LASTEXITCODE -eq 0
}

function Test-RemoteBranch([string]$Branch) {
    $output = & git --git-dir=$origin show-ref --verify --quiet "refs/heads/$Branch" 2>&1
    return $LASTEXITCODE -eq 0
}

function Commit-File([string]$Name, [string]$Value, [string]$Message) {
    [IO.File]::WriteAllText((Join-Path $work $Name), $Value, [Text.UTF8Encoding]::new($false))
    Invoke-Git @("add", "--", $Name) | Out-Null
    Invoke-Git @("commit", "-m", $Message) | Out-Null
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    & git init --bare $origin | Out-Null
    & git init -b main $work | Out-Null
    Invoke-Git @("config", "user.name", "Branch Cleanup Test") | Out-Null
    Invoke-Git @("config", "user.email", "branch-test@example.invalid") | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $work "scripts") | Out-Null
    Copy-Item $sourceScript (Join-Path $work "scripts/clear-branches.ps1")
    Commit-File "base.txt" "base" "test: base"
    Invoke-Git @("remote", "add", "origin", $origin) | Out-Null
    Invoke-Git @("push", "-u", "origin", "main") | Out-Null

    Invoke-Git @("checkout", "-b", "feat/merged") | Out-Null
    Commit-File "merged.txt" "merged" "test: merged branch"
    Invoke-Git @("push", "-u", "origin", "feat/merged") | Out-Null
    Invoke-Git @("checkout", "main") | Out-Null
    Invoke-Git @("merge", "--no-ff", "feat/merged", "-m", "test: merge safe branch") | Out-Null
    Invoke-Git @("push", "origin", "main") | Out-Null

    Invoke-Git @("checkout", "-b", "feat/unmerged") | Out-Null
    Commit-File "unmerged.txt" "unmerged" "test: unmerged work"
    Invoke-Git @("push", "-u", "origin", "feat/unmerged") | Out-Null
    Invoke-Git @("checkout", "main") | Out-Null

    Invoke-Git @("checkout", "-b", "feat/local-work") | Out-Null
    Commit-File "local-work.txt" "published" "test: published local-work"
    Invoke-Git @("push", "-u", "origin", "feat/local-work") | Out-Null
    Invoke-Git @("checkout", "main") | Out-Null
    Invoke-Git @("merge", "--no-ff", "feat/local-work", "-m", "test: merge remote local-work") | Out-Null
    Invoke-Git @("push", "origin", "main") | Out-Null
    Invoke-Git @("checkout", "feat/local-work") | Out-Null
    Commit-File "local-only.txt" "keep" "test: unpushed local work"
    Invoke-Git @("checkout", "main") | Out-Null

    Push-Location $work
    try {
        $dryRun = & pwsh -NoProfile -File scripts/clear-branches.ps1 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Dry-run failed: $($dryRun -join [Environment]::NewLine)" }
        $dryText = $dryRun -join [Environment]::NewLine
        if ($dryText -notlike "*[SAFE]*feat/merged*") { throw "Dry-run did not mark feat/merged safe." }
        if ($dryText -notlike "*[KEEP]*feat/unmerged*") { throw "Dry-run did not keep feat/unmerged." }
        if ($dryText -notlike "*[KEEP]*feat/local-work*") { throw "Dry-run did not protect local work." }
        if (-not (Test-RemoteBranch "feat/merged") -or -not (Test-LocalBranch "feat/merged")) { throw "Dry-run deleted a safe branch." }

        $deleteRun = & pwsh -NoProfile -File scripts/clear-branches.ps1 -Delete 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Delete run failed: $($deleteRun -join [Environment]::NewLine)" }
    }
    finally { Pop-Location }

    if (Test-RemoteBranch "feat/merged" -or Test-LocalBranch "feat/merged") { throw "Merged branch was not removed remotely and locally." }
    if (-not (Test-RemoteBranch "feat/unmerged") -or -not (Test-LocalBranch "feat/unmerged")) { throw "Unmerged branch was removed." }
    if (-not (Test-RemoteBranch "feat/local-work") -or -not (Test-LocalBranch "feat/local-work")) { throw "Branch with local-only work was removed." }
    if (-not (Test-RemoteBranch "main") -or -not (Test-LocalBranch "main")) { throw "Main branch was removed." }

    Write-Output "all clear-branches tests passed"
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
