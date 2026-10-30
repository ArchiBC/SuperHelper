param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$YakExecutable,
    [ValidateSet('win', 'mac')][string]$Platform = $(if ($IsMacOS) { 'mac' } else { 'win' }),
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not $YakExecutable) { $YakExecutable = if ($IsMacOS) { '/Applications/Rhino 9.app/Contents/Resources/bin/yak' } else { 'C:\Program Files\Rhino 9\System\yak.exe' } }
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepositoryRoot ('artifacts/gh2-' + $Platform + '-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$projectPath = Join-Path $RepositoryRoot 'SuperHelper.GH2/SuperHelper.GH2.csproj'
[xml]$project = [IO.File]::ReadAllText($projectPath)
$sdk = @($project.SelectNodes('//PackageReference') | Where-Object { $_.Include -in @('Grasshopper2', 'RhinoCommon') })
if ($sdk.Count -ne 2 -or $sdk[0].Version -ne $sdk[1].Version) { throw 'GH2 and RhinoCommon SDK versions must match.' }
$productVersion = [version]$project.SelectSingleNode('//Version').InnerText
$version = if ($productVersion.Revision -gt 0) { $productVersion.ToString() } else { $productVersion.ToString(3) }
$buildRoot = Join-Path $OutputRoot 'build'
$packageRoot = Join-Path $OutputRoot 'package'
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a fresh GH2 output directory.' }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
dotnet restore $projectPath --source 'https://api.nuget.org/v3/index.json'
if ($LASTEXITCODE -ne 0) { throw 'GH2 restore failed.' }
dotnet build $projectPath -c Release --no-restore -o $buildRoot -v minimal
if ($LASTEXITCODE -ne 0) { throw 'GH2 Release build failed.' }
$assembly = Join-Path $buildRoot 'SuperHelper.GH2.rhp'
if ([Reflection.AssemblyName]::GetAssemblyName($assembly).Version -ne $productVersion) { throw 'GH2 assembly version mismatch.' }
$payloadRoot = $packageRoot
$loaderHash = $null
if ($Platform -eq 'mac') {
    $loaderProject = Join-Path $RepositoryRoot 'SuperHelper.GH2.MacLoader/SuperHelper.GH2.MacLoader.csproj'
    $loaderBuild = Join-Path $OutputRoot 'loader'
    dotnet build $loaderProject -c Release -o $loaderBuild -v minimal --source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw 'GH2 Mac loader build failed.' }
    $loaderAssembly = Join-Path $loaderBuild 'SuperHelper.GH2.MacLoader.rhp'
    if ([Reflection.AssemblyName]::GetAssemblyName($loaderAssembly).Version -ne $productVersion) { throw 'GH2 Mac loader version mismatch.' }
    $loaderHash = (Get-FileHash -LiteralPath $loaderAssembly -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $loaderAssembly -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $loaderBuild 'SuperHelper.GH2.MacLoader.deps.json') -Destination $packageRoot
    $payloadRoot = Join-Path $packageRoot 'gh2'
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
}
Copy-Item -LiteralPath $assembly -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $buildRoot 'SuperHelper.GH2.deps.json') -Destination $payloadRoot
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'SuperHelper/Resources/SuperHelperIcon_128.png') -Destination (Join-Path $packageRoot 'icon.png')
$manifest = [IO.File]::ReadAllText((Join-Path $RepositoryRoot ('YAK/manifest-gh2-' + $Platform + '.yml'))).Replace('$version', $productVersion.ToString())
[IO.File]::WriteAllText((Join-Path $packageRoot 'manifest.yml'), $manifest, [Text.UTF8Encoding]::new($false))
Push-Location $packageRoot
try {
    & $YakExecutable build --platform $Platform
    if ($LASTEXITCODE -ne 0) { throw 'GH2 Yak build failed.' }
} finally { Pop-Location }
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.yak' -File)
if ($packages.Count -ne 1 -or $packages[0].Name -notmatch ('^superhelpergh2-.*-rh9(?:_\d+)?-' + $Platform + '\.yak$')) { throw "Expected one GH2 $Platform package targeting Rhino 9." }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    if ($archive.Entries | Where-Object { $_.FullName -match '\.(dll|gha)$' }) { throw 'The GH2 package must not bundle host SDK assemblies or the GH1 extension.' }
    $payloadEntry = if ($Platform -eq 'mac') { 'gh2/SuperHelper.GH2.rhp' } else { 'SuperHelper.GH2.rhp' }
    $entry = $archive.GetEntry($payloadEntry)
    if ($null -eq $entry) { throw 'Missing GH2 plugin in package.' }
    if ($Platform -eq 'mac') {
        $loaderEntry = $archive.GetEntry('SuperHelper.GH2.MacLoader.rhp')
        if ($null -eq $loaderEntry) { throw 'Missing Mac loader in package.' }
        $loaderStream = $loaderEntry.Open()
        try { $insideLoaderHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($loaderStream)) } finally { $loaderStream.Dispose() }
        if ($insideLoaderHash -ne $loaderHash) { throw 'Packaged Mac loader differs from this build.' }
    }
    $stream = $entry.Open()
    try { $insideHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
    if ($insideHash -ne (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash) { throw 'Packaged GH2 plugin differs from this build.' }
    $entries = @($archive.Entries.FullName)
} finally { $archive.Dispose() }
$result = [pscustomobject]@{ Version = $version; SdkVersion = $sdk[0].Version; Platform = $Platform; Package = $packages[0].FullName; PluginSHA256 = $insideHash; LoaderSHA256 = $loaderHash; PackageSHA256 = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash; Entries = $entries; Published = $false; RhinoGuiValidated = $false }
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'package-result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 4
