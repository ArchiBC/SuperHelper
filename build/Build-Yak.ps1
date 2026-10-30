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
$gh2BuildRoot = Join-Path $OutputRoot 'gh2-build'
$loaderBuildRoot = Join-Path $OutputRoot 'loader-build'
$packageRoot = Join-Path $OutputRoot 'package'
if ((Test-Path -LiteralPath $packageRoot) -and @(Get-ChildItem -LiteralPath $packageRoot -Force).Count -gt 0) { throw 'Use a fresh package staging directory.' }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Push-Location $RepositoryRoot
try {
    dotnet restore $projectPath --source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    dotnet build $projectPath -c Release --no-restore -o $buildRoot -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    foreach ($extension in @(
        @{ Project = 'SuperHelper.GH2/SuperHelper.GH2.csproj'; Output = $gh2BuildRoot; Sdk = 'Grasshopper2' },
        @{ Project = 'SuperHelper.GH2.Loader/SuperHelper.GH2.Loader.csproj'; Output = $loaderBuildRoot; Sdk = 'RhinoCommon' }
    )) {
        $extensionPath = Join-Path $RepositoryRoot $extension.Project
        [xml]$extensionProject = [IO.File]::ReadAllText($extensionPath)
        if ($extensionProject.SelectSingleNode('//TargetFramework').InnerText -ne 'net10.0' -or
            [version]$extensionProject.SelectSingleNode('//Version').InnerText -ne [version]$projectVersion) { throw 'All host extensions and the loader must target net10.0 with the same product version.' }
        $extensionSdk = @($extensionProject.SelectNodes('//PackageReference') | Where-Object { $_.Include -in @('RhinoCommon', $extension.Sdk) })
        if ($extensionSdk.Count -eq 0 -or @($extensionSdk | Where-Object { $_.Version -ne $refs[0].Version }).Count -gt 0) { throw 'All extensions must use matching Rhino SDK versions.' }
        dotnet restore $extensionPath --source 'https://api.nuget.org/v3/index.json'
        if ($LASTEXITCODE -ne 0) { throw ('Restore failed: ' + $extension.Project) }
        dotnet build $extensionPath -c Release --no-restore -o $extension.Output -v minimal
        if ($LASTEXITCODE -ne 0) { throw ('Release build failed: ' + $extension.Project) }
    }
} finally { Pop-Location }
$assemblyPath = Join-Path $buildRoot 'SuperHelper.gha'
if ((Normalize-Version ([Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString())) -ne $version) { throw 'Unexpected compiled assembly version.' }
Copy-Item -LiteralPath $assemblyPath -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $buildRoot 'SuperHelper.deps.json') -Destination $packageRoot
$gh2AssemblyPath = Join-Path $gh2BuildRoot 'SuperHelper.GH2.rhp'
$loaderAssemblyPath = Join-Path $loaderBuildRoot 'SuperHelper.GH2.Loader.rhp'
foreach ($extensionAssembly in @($gh2AssemblyPath, $loaderAssemblyPath)) {
    if ((Normalize-Version ([Reflection.AssemblyName]::GetAssemblyName($extensionAssembly).Version.ToString())) -ne $version) { throw 'Unexpected compiled extension version.' }
}
Copy-Item -LiteralPath $loaderAssemblyPath -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $loaderBuildRoot 'SuperHelper.GH2.Loader.deps.json') -Destination $packageRoot
$gh2PackageRoot = Join-Path $packageRoot 'gh2'
New-Item -ItemType Directory -Path $gh2PackageRoot -Force | Out-Null
Copy-Item -LiteralPath $gh2AssemblyPath -Destination $gh2PackageRoot
Copy-Item -LiteralPath (Join-Path $gh2BuildRoot 'SuperHelper.GH2.deps.json') -Destination $gh2PackageRoot
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'SuperHelper\Resources\SuperHelperIcon_128.png') -Destination (Join-Path $packageRoot 'icon.png')
# Keep common package metadata separate from historical release folders.
$manifest = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'YAK\manifest-rh9.yml'))
$manifest = [regex]::Replace($manifest, '(?m)^version:.*$', 'version: ' + $projectVersion)
$manifest = [regex]::Replace($manifest, '(?m)^icon_url:.*$', 'icon: icon.png')
[IO.File]::WriteAllText((Join-Path $packageRoot 'manifest.yml'),$manifest,[Text.UTF8Encoding]::new($false))
Push-Location $packageRoot
try {
    & $YakExecutable build --platform any
    if ($LASTEXITCODE -ne 0) { throw 'Yak build failed.' }
} finally { Pop-Location }
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.yak' -File)
if ($packages.Count -ne 1 -or $packages[0].Name -notmatch '^superhelperr9-.*-rh9(?:_\d+)?-any\.yak$') { throw 'Expected exactly one cross-platform Rhino 9 Yak package.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    if ($archive.Entries | Where-Object { $_.FullName -match '\.dll$' }) { throw 'Do not package host-provided SDK DLLs.' }
    if ($archive.GetEntry('SuperHelper.GH2.rhp')) { throw 'GH2 must be nested so Rhino does not scan it before the GH2 resolver is ready.' }
    $payloadHashes = [ordered]@{}
    $expectedPayloads = [ordered]@{ 'SuperHelper.gha' = $assemblyPath; 'SuperHelper.GH2.Loader.rhp' = $loaderAssemblyPath; 'gh2/SuperHelper.GH2.rhp' = $gh2AssemblyPath }
    foreach ($name in $expectedPayloads.Keys) {
        $entry = $archive.GetEntry($name)
        if ($null -eq $entry) { throw ('Missing payload: ' + $name) }
        $stream = $entry.Open()
        try { $insideHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ($insideHash -ne (Get-FileHash -LiteralPath $expectedPayloads[$name] -Algorithm SHA256).Hash) { throw ('Packaged payload differs from this build: ' + $name) }
        $payloadHashes[$name] = $insideHash
    }
    foreach ($name in @('SuperHelper.deps.json', 'SuperHelper.GH2.Loader.deps.json', 'gh2/SuperHelper.GH2.deps.json')) {
        if ($null -eq $archive.GetEntry($name)) { throw ('Missing dependency metadata: ' + $name) }
    }
    $entries = @($archive.Entries.FullName)
} finally { $archive.Dispose() }
$result = [pscustomobject]@{ProjectVersion=$projectVersion;PackageFile=$packages[0].Name;Package=$packages[0].FullName;Version=$version;SdkVersion=$refs[0].Version;Platform='any';Hosts=@('GH1','GH2');GhaSHA256=$payloadHashes['SuperHelper.gha'];PayloadHashes=$payloadHashes;PackageSHA256=(Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash;Entries=$entries;Published=$false;MacRuntimeValidated=$false}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'package-result.json') -Encoding utf8
if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('release_tag=v' + $version) -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('package_path=' + $packages[0].FullName) -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('metadata_path=' + (Join-Path $OutputRoot 'package-result.json')) -Encoding utf8
}
$result | ConvertTo-Json -Depth 4
