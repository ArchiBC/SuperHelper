param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ReleaseTag,
    [string]$Repository = 'ArchiBC/SuperHelper',
    [string]$YakExecutable,
    [string]$TargetCommit,
    [switch]$CreateRelease,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
if ($Repository -ne 'ArchiBC/SuperHelper') { throw 'Publishing is configured only for ArchiBC/SuperHelper.' }
if ($ReleaseTag -notmatch '^v?(\d+\.\d+\.\d+(?:\.\d+)?)$') { throw 'Expected a numeric release tag.' }
$tagVersion = [version]$Matches[1]
$files = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.yak' -Recurse -File)
$metadataFiles = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'package-result.json' -Recurse -File)
if ($files.Count -ne 1 -or $metadataFiles.Count -ne 1) { throw 'Expected one Yak archive and its build metadata.' }
$package = $files[0]
$metadata = Get-Content -LiteralPath $metadataFiles[0].FullName -Raw | ConvertFrom-Json
$expectedVersion = [version]$metadata.ProjectVersion
if ($tagVersion.Major -ne $expectedVersion.Major -or $tagVersion.Minor -ne $expectedVersion.Minor -or $tagVersion.Build -ne $expectedVersion.Build -or [Math]::Max(0,$tagVersion.Revision) -ne [Math]::Max(0,$expectedVersion.Revision)) { throw 'Release tag does not match the package version.' }
if ($metadata.Platform -ne 'any' -or $package.Name -ne $metadata.PackageFile -or $package.Name -notmatch '^superhelperr9-.*-rh9(?:_\d+)?-any\.yak$') { throw 'Expected a cross-platform Rhino 9 package.' }
if ((Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $metadata.PackageSHA256) { throw 'Downloaded package does not match the build metadata.' }
function Get-PayloadHashes([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            if ($hashes.ContainsKey($entry.FullName)) { throw 'Duplicate archive entry.' }
            $stream = $entry.Open()
            try { $hashes.Add($entry.FullName,[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))) } finally { $stream.Dispose() }
        }
        return ,$hashes
    } finally { $archive.Dispose() }
}
$payload = Get-PayloadHashes $package.FullName
if (-not $payload.ContainsKey('SuperHelper.gha') -or $payload['SuperHelper.gha'] -ne $metadata.GhaSHA256) { throw 'Packaged GHA does not match the build.' }
Write-Output ('Validated package: ' + $package.Name)
if (-not $Publish) { Write-Output 'Validation only; GitHub and Yak were not contacted.'; return }
if ([string]::IsNullOrWhiteSpace($env:YAK_TOKEN) -or [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'Both YAK_TOKEN and GH_TOKEN are required for publishing.' }
if (-not (Test-Path -LiteralPath $YakExecutable)) { throw 'Standalone Yak executable is missing.' }
if ($CreateRelease) {
    $release = & (Join-Path $PSScriptRoot 'New-GitHubRelease.ps1') -ReleaseTag $ReleaseTag -TargetCommit $TargetCommit -Repository $Repository
} else {
    $releaseJson = & gh api ('repos/' + $Repository + '/releases/tags/' + $ReleaseTag)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to read the published GitHub release.' }
    $release = $releaseJson | ConvertFrom-Json
}
if ($release.draft -or $release.prerelease) { throw 'Only published stable releases can be sent to Yak.' }
$existing = @($release.assets | Where-Object name -EQ $package.Name)
$publishPath = $package.FullName
if ($existing.Count -gt 0) {
    # A retry reuses the original archive even if ZIP timestamps changed.
    $existingDirectory = Join-Path $PackageDirectory ('existing-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $existingDirectory | Out-Null
    & gh release download $ReleaseTag --repo $Repository --pattern $package.Name --dir $existingDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download the existing release asset.' }
    $publishPath = Join-Path $existingDirectory $package.Name
    $existingPayload = Get-PayloadHashes $publishPath
    if ($existingPayload.Count -ne $payload.Count -or @($payload.Keys | Where-Object { -not $existingPayload.ContainsKey($_) -or $existingPayload[$_] -ne $payload[$_] }).Count -gt 0) { throw 'This release already has a different package. Create a new version.' }
} else {
    & gh release upload $ReleaseTag $package.FullName --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw 'Failed to attach the package to the GitHub release.' }
}
& $YakExecutable push --source 'https://yak.rhino3d.com/' $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Yak publishing failed; the GitHub release asset is retained for retry.' }
