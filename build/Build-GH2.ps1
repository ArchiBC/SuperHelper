param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$YakExecutable = 'C:\Program Files\Rhino 9\System\yak.exe',
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepositoryRoot ('artifacts/gh2-win-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')) }
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
Copy-Item -LiteralPath $assembly -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $buildRoot 'SuperHelper.GH2.deps.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'SuperHelper/Resources/SuperHelperIcon_128.png') -Destination (Join-Path $packageRoot 'icon.png')
$manifest = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'YAK/manifest-gh2-win.yml')).Replace('$version', $productVersion.ToString())
[IO.File]::WriteAllText((Join-Path $packageRoot 'manifest.yml'), $manifest, [Text.UTF8Encoding]::new($false))
Push-Location $packageRoot
try {
    & $YakExecutable build --platform win
    if ($LASTEXITCODE -ne 0) { throw 'GH2 Yak build failed.' }
} finally { Pop-Location }
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.yak' -File)
if ($packages.Count -ne 1 -or $packages[0].Name -notmatch '^superhelpergh2-.*-rh9(?:_\d+)?-win\.yak$') { throw 'Expected one GH2 Windows package targeting Rhino 9.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    if ($archive.Entries | Where-Object { $_.FullName -match '\.(dll|gha)$' }) { throw 'The GH2 package must not bundle host SDK assemblies or the GH1 extension.' }
    $entry = $archive.GetEntry('SuperHelper.GH2.rhp')
    if ($null -eq $entry) { throw 'Missing GH2 plugin in package.' }
    $stream = $entry.Open()
    try { $insideHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
    if ($insideHash -ne (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash) { throw 'Packaged GH2 plugin differs from this build.' }
    $entries = @($archive.Entries.FullName)
} finally { $archive.Dispose() }
$result = [pscustomobject]@{ Version = $version; SdkVersion = $sdk[0].Version; Platform = 'win'; Package = $packages[0].FullName; PluginSHA256 = $insideHash; PackageSHA256 = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash; Entries = $entries; Published = $false; RhinoGuiValidated = $false }
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'package-result.json') -Encoding utf8
$result | ConvertTo-Json -Depth 4
