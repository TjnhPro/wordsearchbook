[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function ConvertTo-StrictReleaseVersion {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Release version '$Value' must match M.m.p."
    }

    $parsed = [Version]$Value
    if ($parsed.Revision -ge 0) {
        throw "Release version '$Value' must have exactly three components."
    }

    return $parsed
}

function Get-NextPatchVersion {
    param([Parameter(Mandatory)][Version]$Current)

    return [Version]::new($Current.Major, $Current.Minor, $Current.Build + 1)
}

function Resolve-TargetVersion {
    param(
        [Parameter(Mandatory)][Version]$CurrentVersion,
        [string]$RequestedVersion = ""
    )

    if ([string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return Get-NextPatchVersion $CurrentVersion
    }

    $target = ConvertTo-StrictReleaseVersion $RequestedVersion
    if ($target -le $CurrentVersion) {
        throw "Target version $($target.ToString(3)) must be greater than current version $($CurrentVersion.ToString(3))."
    }

    return $target
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & git -c core.safecrlf=false @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return @{ ExitCode = $exitCode; Output = @($output) }
}

function Invoke-Gh {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & gh @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "gh $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }

    return @{ ExitCode = $exitCode; Output = @($output) }
}

function Assert-MainBranch {
    $branch = ((Invoke-Git -Arguments @("branch", "--show-current")).Output -join "").Trim()
    if ($branch -ne "main") {
        throw "Release must run from branch 'main'. Current branch is '$branch'."
    }
}

function Assert-CleanWorkingTree {
    $status = @((Invoke-Git -Arguments @("status", "--porcelain")).Output | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_)
    })
    if ($status.Count -gt 0) {
        throw "Working tree must be clean before release."
    }
}

function Sync-Main {
    Invoke-Git -Arguments @("fetch", "origin", "--prune", "--tags") | Out-Null
    Invoke-Git -Arguments @("pull", "--ff-only", "origin", "main") | Out-Null

    $head = ((Invoke-Git -Arguments @("rev-parse", "HEAD")).Output -join "").Trim()
    $originMain = ((Invoke-Git -Arguments @("rev-parse", "origin/main")).Output -join "").Trim()
    if ($head -ne $originMain) {
        throw "Local main does not match origin/main."
    }

    return $head
}

function Assert-GreenMainCi {
    param([Parameter(Mandatory)][string]$HeadSha)

    Invoke-Gh -Arguments @("auth", "status") | Out-Null
    $result = Invoke-Gh -Arguments @(
        "run", "list", "--workflow", "build-and-test.yml", "--branch", "main", "--commit", $HeadSha,
        "--limit", "20", "--json", "headSha,headBranch,event,status,conclusion,databaseId,url")
    $runs = @(($result.Output -join [Environment]::NewLine) | ConvertFrom-Json)
    $success = $runs | Where-Object {
        $_.headSha -eq $HeadSha -and $_.headBranch -eq "main" -and $_.event -eq "push" -and
        $_.status -eq "completed" -and $_.conclusion -eq "success"
    } | Select-Object -First 1

    if ($null -eq $success) {
        throw "Build and test must be successful for main SHA $HeadSha before release."
    }

    return $success
}

function Get-RemoteTagSha {
    param([Parameter(Mandatory)][string]$TagName)

    $result = Invoke-Git -Arguments @("ls-remote", "--tags", "origin", "refs/tags/$TagName") -AllowFailure
    if ($result.ExitCode -ne 0) {
        throw "Could not inspect remote tag '$TagName'."
    }

    $line = @($result.Output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -First 1)
    if ($line.Count -eq 0) {
        return $null
    }

    return (([string]$line[0]).Trim() -split '\s+')[0]
}

function Get-SourceVersion {
    param(
        [Parameter(Mandatory)][string]$DirectoryBuildPropsPath,
        [Parameter(Mandatory)][string]$PackageJsonPath,
        [Parameter(Mandatory)][string]$PackageLockPath
    )

    $propsText = [IO.File]::ReadAllText($DirectoryBuildPropsPath)
    $propsMatch = [regex]::Match($propsText, '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>')
    if (-not $propsMatch.Success) {
        throw "Directory.Build.props must contain one strict Version element."
    }

    $package = [IO.File]::ReadAllText($PackageJsonPath) | ConvertFrom-Json -AsHashtable
    $lock = [IO.File]::ReadAllText($PackageLockPath) | ConvertFrom-Json -AsHashtable
    if (-not $lock.ContainsKey("packages") -or -not $lock["packages"].ContainsKey("")) {
        throw "package-lock.json must contain the root package entry."
    }

    $versions = @(
        $propsMatch.Groups[1].Value,
        [string]$package["version"],
        [string]$lock["version"],
        [string]$lock["packages"][""]["version"]
    )
    foreach ($value in $versions) {
        ConvertTo-StrictReleaseVersion $value | Out-Null
    }

    if (@($versions | Select-Object -Unique).Count -ne 1) {
        throw "Source versions must match across Directory.Build.props, package.json and package-lock.json. Found: $($versions -join ', ')."
    }

    return ConvertTo-StrictReleaseVersion $versions[0]
}

function Set-SingleVersionPattern {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$ContractName
    )

    $regex = [regex]::new($Pattern)
    if ($regex.Matches($Text).Count -ne 1) {
        throw "$ContractName does not match the expected release version contract."
    }

    return $regex.Replace($Text, ('${1}' + $Target + '${2}'))
}

function Set-SourceVersion {
    param(
        [Parameter(Mandatory)][string]$DirectoryBuildPropsPath,
        [Parameter(Mandatory)][string]$PackageJsonPath,
        [Parameter(Mandatory)][string]$PackageLockPath,
        [Parameter(Mandatory)][Version]$TargetVersion
    )

    $target = $TargetVersion.ToString(3)
    $propsText = [IO.File]::ReadAllText($DirectoryBuildPropsPath)
    foreach ($replacement in @(
        @{ Pattern = '(<Version>)[0-9]+\.[0-9]+\.[0-9]+(</Version>)'; Value = $target },
        @{ Pattern = '(<AssemblyVersion>)[0-9]+\.[0-9]+\.[0-9]+\.0(</AssemblyVersion>)'; Value = "$target.0" },
        @{ Pattern = '(<FileVersion>)[0-9]+\.[0-9]+\.[0-9]+\.0(</FileVersion>)'; Value = "$target.0" }
    )) {
        $propsText = Set-SingleVersionPattern $propsText $replacement.Pattern $replacement.Value "Directory.Build.props"
    }

    $packageText = [IO.File]::ReadAllText($PackageJsonPath)
    $packageText = Set-SingleVersionPattern $packageText '(?m)^(\s{2}"version"\s*:\s*")[^"]+("\s*,?)$' $target "package.json"

    $lockText = [IO.File]::ReadAllText($PackageLockPath)
    $lockText = Set-SingleVersionPattern $lockText '(?m)^(\s{2}"version"\s*:\s*")[^"]+("\s*,?)$' $target "package-lock.json root"
    $lockText = Set-SingleVersionPattern $lockText '(?ms)("packages"\s*:\s*\{\s*""\s*:\s*\{\s*"name"\s*:\s*"[^"]+"\s*,\s*"version"\s*:\s*")[^"]+("\s*,?)' $target "package-lock.json package root"

    [IO.File]::WriteAllText($DirectoryBuildPropsPath, $propsText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($PackageJsonPath, $packageText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($PackageLockPath, $lockText, [Text.UTF8Encoding]::new($false))
}

function Assert-ReleaseDiff {
    $actual = @((Invoke-Git -Arguments @("diff", "--name-only")).Output | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_)
    } | ForEach-Object { ([string]$_).Trim().Replace('\', '/') } | Sort-Object)
    $expected = @(
        "Directory.Build.props",
        "src/WordSearchBook.Desktop/Frontend/package-lock.json",
        "src/WordSearchBook.Desktop/Frontend/package.json"
    ) | Sort-Object

    if (Compare-Object $actual $expected) {
        throw "Release must change exactly the three version files. Changed: $($actual -join ', ')"
    }

    Invoke-Git -Arguments @("diff", "--check") | Out-Null
}

function New-ReleaseCommitAndTag {
    param([Parameter(Mandatory)][Version]$TargetVersion)

    $target = $TargetVersion.ToString(3)
    $tag = "v$target"
    Invoke-Git -Arguments @(
        "add", "--", "Directory.Build.props",
        "src/WordSearchBook.Desktop/Frontend/package.json",
        "src/WordSearchBook.Desktop/Frontend/package-lock.json") | Out-Null
    Invoke-Git -Arguments @("commit", "-m", "chore: release $tag") | Out-Null
    $releaseSha = ((Invoke-Git -Arguments @("rev-parse", "HEAD")).Output -join "").Trim()
    Invoke-Git -Arguments @("tag", $tag, $releaseSha) | Out-Null
    return @{ Tag = $tag; Sha = $releaseSha }
}

function Push-ReleaseAtomically {
    param([Parameter(Mandatory)][string]$TagName)

    Invoke-Git -Arguments @("push", "--atomic", "origin", "main", "refs/tags/$TagName`:refs/tags/$TagName") | Out-Null
}

function Restore-LocalPrePushState {
    param(
        [Parameter(Mandatory)][string]$OriginalSha,
        [string]$LocalTag = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($LocalTag)) {
        Invoke-Git -Arguments @("tag", "-d", $LocalTag) -AllowFailure | Out-Null
    }

    Invoke-Git -Arguments @("reset", "--hard", $OriginalSha) | Out-Null
}

function Find-ReleaseWorkflowRun {
    param([Parameter(Mandatory)][string]$ReleaseSha)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $result = Invoke-Gh -Arguments @("run", "list", "--workflow", "release.yml", "--limit", "20", "--json", "databaseId,headSha,status,conclusion,url,createdAt")
        $runs = @(($result.Output -join [Environment]::NewLine) | ConvertFrom-Json)
        $run = $runs | Where-Object { $_.headSha -eq $ReleaseSha } | Sort-Object createdAt -Descending | Select-Object -First 1
        if ($null -ne $run) {
            return $run
        }

        Start-Sleep -Seconds 5
    }

    throw "Could not find Publish release workflow for SHA $ReleaseSha."
}

function Wait-ReleaseWorkflow {
    param([Parameter(Mandatory)][long]$RunId)

    $watch = Invoke-Gh -Arguments @("run", "watch", "$RunId", "--exit-status") -AllowFailure
    if ($watch.ExitCode -eq 0) {
        return
    }

    Write-Host "[WARN] Publish release workflow failed once; rerunning same tag."
    Invoke-Gh -Arguments @("run", "rerun", "$RunId") | Out-Null
    $retry = Invoke-Gh -Arguments @("run", "watch", "$RunId", "--exit-status") -AllowFailure
    if ($retry.ExitCode -ne 0) {
        throw "Publish release workflow failed after one automatic retry. The tag remains fixed; rerun release.ps1 to resume this same release after fixing the cause."
    }
}

function Assert-PublishedRelease {
    param(
        [Parameter(Mandatory)][Version]$Version,
        [psobject]$Release = $null
    )

    $tag = "v$($Version.ToString(3))"
    if ($null -eq $Release) {
        $result = Invoke-Gh -Arguments @("release", "view", $tag, "--json", "tagName,isDraft,isPrerelease,url,assets")
        $Release = ($result.Output -join [Environment]::NewLine) | ConvertFrom-Json
    }

    if ($Release.tagName -ne $tag) { throw "Published release tag mismatch." }
    if ($Release.isDraft) { throw "Published release '$tag' is still a draft." }
    if ($Release.isPrerelease) { throw "Published release '$tag' must be stable." }

    $expected = @("WordSearchBook.exe", "WordSearchBook.exe.sha256") | Sort-Object
    $actual = @($Release.assets | ForEach-Object { $_.name } | Sort-Object)
    if (Compare-Object $actual $expected) {
        throw "Published release '$tag' does not contain exactly the two expected assets."
    }

    return $Release
}

function Try-GetPublishedRelease {
    param([Parameter(Mandatory)][string]$TagName)

    $result = Invoke-Gh -Arguments @("release", "view", $TagName, "--json", "tagName,isDraft,isPrerelease,url,assets") -AllowFailure
    if ($result.ExitCode -ne 0) {
        $errorText = $result.Output -join [Environment]::NewLine
        if ($errorText -match '(?i)not found') {
            return $null
        }

        throw "Could not inspect existing GitHub Release '$TagName': $errorText"
    }

    return ($result.Output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Invoke-WordSearchBookRelease {
    param([string]$RequestedVersion = "")

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    Set-Location $repoRoot
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    $packagePath = Join-Path $repoRoot "src/WordSearchBook.Desktop/Frontend/package.json"
    $lockPath = Join-Path $repoRoot "src/WordSearchBook.Desktop/Frontend/package-lock.json"

    Assert-MainBranch
    Assert-CleanWorkingTree
    $originalSha = Sync-Main
    $ci = Assert-GreenMainCi -HeadSha $originalSha
    $currentVersion = Get-SourceVersion $propsPath $packagePath $lockPath
    $currentTag = "v$($currentVersion.ToString(3))"
    $currentRemoteTag = Get-RemoteTagSha $currentTag

    if ($null -ne $currentRemoteTag -and $currentRemoteTag -eq $originalSha) {
        $existingRelease = Try-GetPublishedRelease -TagName $currentTag
        if ($null -eq $existingRelease) {
            Write-Host "Resuming publication for existing tag $currentTag."
            $run = Find-ReleaseWorkflowRun -ReleaseSha $originalSha
            Wait-ReleaseWorkflow -RunId ([long]$run.databaseId)
            $published = Assert-PublishedRelease -Version $currentVersion
            Write-Host "Released $currentTag"
            Write-Host $published.url
            return
        }

        Assert-PublishedRelease -Version $currentVersion -Release $existingRelease | Out-Null
    }

    $targetVersion = Resolve-TargetVersion -CurrentVersion $currentVersion -RequestedVersion $RequestedVersion
    $tag = "v$($targetVersion.ToString(3))"
    if ($null -ne (Get-RemoteTagSha $tag)) {
        throw "Remote tag '$tag' already exists."
    }

    Write-Host "WordSearchBook Release"
    Write-Host "Current version: $($currentVersion.ToString(3))"
    Write-Host "Target version:  $($targetVersion.ToString(3))"
    Write-Host "[PASS] main CI $($ci.url)"

    $remotePushSucceeded = $false
    $createdTag = ""
    try {
        Set-SourceVersion $propsPath $packagePath $lockPath $targetVersion
        Assert-ReleaseDiff
        $release = New-ReleaseCommitAndTag -TargetVersion $targetVersion
        $createdTag = $release.Tag
        Push-ReleaseAtomically -TagName $createdTag
        $remotePushSucceeded = $true

        $run = Find-ReleaseWorkflowRun -ReleaseSha $release.Sha
        Write-Host "[PASS] atomic push"
        Write-Host "Publish workflow: $($run.url)"
        Wait-ReleaseWorkflow -RunId ([long]$run.databaseId)
        $published = Assert-PublishedRelease -Version $targetVersion
        Write-Host "[PASS] Publish release workflow"
        Write-Host "[PASS] GitHub Release $createdTag"
        Write-Host ""
        Write-Host "Released $createdTag"
        Write-Host $published.url
    }
    catch {
        if (-not $remotePushSucceeded) {
            Restore-LocalPrePushState -OriginalSha $originalSha -LocalTag $createdTag
        }

        throw
    }
}

if ($MyInvocation.InvocationName -ne ".") {
    Invoke-WordSearchBookRelease -RequestedVersion $Version
}
