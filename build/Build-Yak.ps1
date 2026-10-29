param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$YakExecutable,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$ReleaseTag
)
$ErrorActionPreference = 'Stop'
function Normalize-Version([string]$Value) {
    if ($Value -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw "Unsupported release version: $Value" }
    $v = [version]$Value
    if ($v.Revision -gt 0) { return "$($v.Major).$($v.Minor).$($v.Build).$($v.Revision)" }
    return "$($v.Major).$($v.Minor).$($v.Build)"
}
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$YakExecutable = [IO.Path]::GetFullPath($YakExecutable)
$projectPath = Join-Path $RepositoryRoot 'SuperHelper\SuperHelper.csproj'
[xml]$project = [IO.File]::ReadAllText($projectPath)
if ($project.SelectSingleNode('//TargetFramework').InnerText -ne 'net10.0') { throw 'This pipeline supports the Rhino 9 net10.0 project only.' }
$refs = @($project.SelectNodes('//PackageReference') | Where-Object { $_.Include -in @('RhinoCommon','Grasshopper') })
if ($refs.Count -ne 2 -or @($refs | Where-Object { $_.Version -notmatch '^9\.\d+\.\d+\.\d+$' }).Count -ne 0 -or $refs[0].Version -ne $refs[1].Version) { throw 'Matching stable RhinoCommon/Grasshopper 9 versions are required.' }
$projectVersion = $project.SelectSingleNode('//Version').InnerText
$version = Normalize-Version $projectVersion
if ($ReleaseTag) {
    if ($ReleaseTag -notmatch '^v?(\d+\.\d+\.\d+(?:\.\d+)?)$') { throw 'Use a numeric release tag such as v2.1.1.' }
    $tagVersion = Normalize-Version $Matches[1]
    if ($tagVersion -ne $version) { throw "Release tag $ReleaseTag does not match project version $projectVersion." }
}
$infoFile = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'SuperHelper') -Filter 'SuperHelperInfo.cs' -Recurse -File)
if ($infoFile.Count -ne 1) { throw 'Expected a single GH assembly metadata source.' }
$infoText = [IO.File]::ReadAllText($infoFile[0].FullName)
if ($infoText -match 'override\s+string\s+Version\s*=>\s*"([^"]+)"' -and (Normalize-Version $Matches[1]) -ne $version) { throw 'The hard-coded GH assembly version does not match the project.' }
$buildRoot = Join-Path $OutputRoot 'build'
$packageRoot = Join-Path $OutputRoot 'package'
if ((Test-Path -LiteralPath $packageRoot) -and @(Get-ChildItem -LiteralPath $packageRoot -Force).Count -gt 0) { throw 'Use a fresh package staging directory.' }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Push-Location $RepositoryRoot
try {
    dotnet restore $projectPath --source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    dotnet build $projectPath -c Release --no-restore -o $buildRoot -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
} finally { Pop-Location }
$assemblyPath = Join-Path $buildRoot 'SuperHelper.gha'
if ((Normalize-Version ([Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString())) -ne $version) { throw 'Unexpected compiled assembly version.' }
Copy-Item -LiteralPath $assemblyPath -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $buildRoot 'SuperHelper.deps.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'SuperHelper\Resources\SuperHelperIcon_128.png') -Destination (Join-Path $packageRoot 'icon.png')
# Keep common package metadata separate from historical release folders.
$manifest = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'YAK\manifest-rh9.yml'))
$manifest = [regex]::Replace($manifest, '(?m)^version:.*$', 'version: ' + $projectVersion)
$manifest = [regex]::Replace($manifest, '(?m)^icon_url:.*$', 'icon: icon.png')
[IO.File]::WriteAllText((Join-Path $packageRoot 'manifest.yml'),$manifest,[Text.UTF8Encoding]::new($false))
Push-Location $packageRoot
try {
    & $YakExecutable build --platform win
    if ($LASTEXITCODE -ne 0) { throw 'Yak build failed.' }
} finally { Pop-Location }
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.yak' -File)
if ($packages.Count -ne 1 -or $packages[0].Name -notmatch '^superhelperr9-.*-rh9(?:_\d+)?-win\.yak$') { throw 'Expected exactly one Windows Rhino 9 Yak package.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    if ($archive.Entries | Where-Object { $_.FullName -match '(^|/)(RhinoCommon|Grasshopper|GH_IO|Rhino\.UI|Eto|Eto\.Serialization\.Xaml|Portable\.Xaml)\.dll$' }) { throw 'Do not package host-provided SDK DLLs.' }
    $entry = $archive.GetEntry('SuperHelper.gha')
    if ($null -eq $entry) { throw 'Missing GHA in Yak archive.' }
    $stream = $entry.Open()
    try { $insideHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
    if ($insideHash -ne (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash) { throw 'The package GHA does not match this build.' }
    $entries = @($archive.Entries.FullName)
} finally { $archive.Dispose() }
$result = [pscustomobject]@{ProjectVersion=$projectVersion;PackageFile=$packages[0].Name;Package=$packages[0].FullName;Version=$version;SdkVersion=$refs[0].Version;Platform='win';GhaSHA256=$insideHash;PackageSHA256=(Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash;Entries=$entries;Published=$false}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'package-result.json') -Encoding utf8
if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('release_tag=v' + $version) -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('package_path=' + $packages[0].FullName) -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('metadata_path=' + (Join-Path $OutputRoot 'package-result.json')) -Encoding utf8
}
$result | ConvertTo-Json -Depth 4
