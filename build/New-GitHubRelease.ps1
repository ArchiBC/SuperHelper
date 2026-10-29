param(
    [Parameter(Mandatory)][string]$ReleaseTag,
    [Parameter(Mandatory)][string]$TargetCommit,
    [string]$Repository = 'ArchiBC/SuperHelper'
)
$ErrorActionPreference = 'Stop'
if ($Repository -ne 'ArchiBC/SuperHelper') { throw 'Publishing is configured only for ArchiBC/SuperHelper.' }
if ($ReleaseTag -notmatch '^v\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Expected a stable numeric release tag.' }
if ($TargetCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'A full merged commit SHA is required.' }
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GH_TOKEN is required to create a release.' }
$headers = @{
    Authorization = 'Bearer ' + $env:GH_TOKEN
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'SuperHelper-Release'
    'X-GitHub-Api-Version' = '2026-03-10'
}
function Invoke-ReleaseApi([string]$Resource, [string]$Method = 'Get', $Body, [switch]$AllowNotFound) {
    $request = @{
        Uri = 'https://api.github.com/repos/' + $Repository + '/' + $Resource
        Method = $Method
        Headers = $headers
        TimeoutSec = 30
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'responseStatus'
    }
    if ($null -ne $Body) {
        $request.Body = $Body | ConvertTo-Json -Depth 5
        $request.ContentType = 'application/json'
    }
    $result = Invoke-RestMethod @request
    if ($responseStatus -eq 404 -and $AllowNotFound) { return $null }
    if ($responseStatus -notin @(200,201)) { throw "GitHub $Method $Resource failed with HTTP $responseStatus." }
    return $result
}
function Get-ReleaseTagCommit {
    $tag = Invoke-ReleaseApi -Resource ('git/ref/tags/' + $ReleaseTag) -AllowNotFound
    if ($null -eq $tag) { return $null }
    $target = $tag.object
    for ($depth = 0; $target.type -eq 'tag' -and $depth -lt 5; $depth++) {
        $target = (Invoke-ReleaseApi -Resource ('git/tags/' + $target.sha)).object
    }
    if ($target.type -ne 'commit') { throw 'The release tag must resolve to a commit.' }
    return $target.sha
}
$tagCommit = Get-ReleaseTagCommit
if ($tagCommit -and $tagCommit -ne $TargetCommit) {
    throw "Tag $ReleaseTag already points to another commit. Increase the project and GH assembly versions before merging develop into main."
}
$release = Invoke-ReleaseApi -Resource ('releases/tags/' + $ReleaseTag) -AllowNotFound
if ($null -ne $release) {
    if ($release.draft -or $release.prerelease) { throw 'An existing draft or prerelease cannot be reused for a stable Yak release.' }
    if (-not $tagCommit) { throw 'The existing release is missing its tag.' }
    Write-Host ('Reusing the release for the same merged commit: ' + $ReleaseTag)
    return $release
}
$release = Invoke-ReleaseApi -Resource 'releases' -Method Post -Body @{
    tag_name = $ReleaseTag
    target_commitish = $TargetCommit
    name = 'SuperHelper R9 ' + $ReleaseTag.Substring(1)
    draft = $false
    prerelease = $false
    generate_release_notes = $true
    make_latest = 'true'
}
if ((Get-ReleaseTagCommit) -ne $TargetCommit) { throw 'The created release tag does not match the merged commit.' }
Write-Host ('Created release: ' + $release.html_url)
return $release
