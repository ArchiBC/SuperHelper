# Compatibility entry point: every build now produces the same all-hosts package.
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$YakExecutable,
    [ValidateSet('any', 'win', 'mac')][string]$Platform = 'any',
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
if (-not $YakExecutable) { $YakExecutable = if ($IsMacOS) { '/Applications/Rhino 9.app/Contents/Resources/bin/yak' } else { 'C:\Program Files\Rhino 9\System\yak.exe' } }
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepositoryRoot ('artifacts/all-hosts-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')) }
& (Join-Path $PSScriptRoot 'Build-Yak.ps1') -RepositoryRoot $RepositoryRoot -YakExecutable $YakExecutable -OutputRoot $OutputRoot
