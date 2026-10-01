param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ExpectedVersion = "",
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "release"))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "_desktop-publish"))
$project = Join-Path $repoRoot "src/WordSearchBook.Desktop/WordSearchBook.Desktop.csproj"
$solution = Join-Path $repoRoot "WordSearchBook.sln"
$frontendRoot = Join-Path $repoRoot "src/WordSearchBook.Desktop/Frontend"
$artifactBoundary = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

foreach ($path in @($releaseRoot, $publishRoot)) {
    if (-not $path.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean release path outside the repository artifacts directory: $path"
    }

    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

try {
    $version = (& dotnet msbuild $project -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read the Desktop project version."
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        $ExpectedVersion = $version
    }
    elseif ($version -ne $ExpectedVersion) {
        throw "Desktop version '$version' does not match expected version '$ExpectedVersion'."
    }

    if (-not $SkipVerification) {
        Push-Location $frontendRoot
        try {
            npm ci
            if ($LASTEXITCODE -ne 0) { throw "Frontend dependency installation failed." }
            npm run verify:css
            if ($LASTEXITCODE -ne 0) { throw "Frontend CSS verification failed." }
            npm test
            if ($LASTEXITCODE -ne 0) { throw "Frontend tests failed." }
        }
        finally {
            Pop-Location
        }

        dotnet restore $solution
        if ($LASTEXITCODE -ne 0) { throw "Solution restore failed." }
        dotnet build $solution --configuration $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }
        dotnet test $solution --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Solution tests failed." }
    }

    dotnet publish $project `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        -p:PublishProfile=WindowsX64 `
        --output $publishRoot
    if ($LASTEXITCODE -ne 0) { throw "Desktop single-file publish failed." }

    $publishedEntries = @(Get-ChildItem -LiteralPath $publishRoot)
    if ($publishedEntries.Count -ne 1 -or
        $publishedEntries[0].PSIsContainer -or
        $publishedEntries[0].Name -ne "WordSearchBook.exe") {
        throw "Single-file publish must contain exactly one root file named WordSearchBook.exe. Found: $($publishedEntries.Name -join ', ')"
    }

    $publishedExe = $publishedEntries[0].FullName
    $versionInfo = (Get-Item -LiteralPath $publishedExe).VersionInfo
    if ($versionInfo.FileVersion -ne "$version.0" -or $versionInfo.ProductVersion -ne $version) {
        throw "Published EXE version '$($versionInfo.FileVersion)/$($versionInfo.ProductVersion)' does not match '$version'."
    }

    Add-Type -AssemblyName System.Drawing
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($publishedExe)
    if ($null -eq $icon) {
        throw "Published EXE does not expose a readable application icon."
    }
    $icon.Dispose()

    $releaseExe = Join-Path $releaseRoot "WordSearchBook.exe"
    Copy-Item -LiteralPath $publishedExe -Destination $releaseExe
    $hash = Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256
    $hashPath = Join-Path $releaseRoot "WordSearchBook.exe.sha256"
    "$($hash.Hash.ToLowerInvariant())  WordSearchBook.exe" | Set-Content -LiteralPath $hashPath -Encoding ascii

    Write-Host "Version: $version"
    Write-Host "Executable: $releaseExe"
    Write-Host "SHA256: $hashPath"
}
finally {
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
}
