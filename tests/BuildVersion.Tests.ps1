$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\Write-BuildVersion.ps1')
$hash = '1234567890abcdef1234567890abcdef12345678'
$local = Resolve-BuildVersion -Commit $hash -ReleaseTag 'v9.9.9' -EventName release
if ($local.Version -ne $hash -or $local.Identity -ne ('commit:' + $hash)) { throw 'Local builds must always use the commit, ignoring release context.' }
$release = Resolve-BuildVersion -Commit $hash -InActions $true -EventName release -ReleaseTag 'v1.2.3-preview.1'
if ($release.Version -ne 'v1.2.3-preview.1' -or $release.Identity -ne 'release:v1.2.3-preview.1') { throw 'A GitHub release build must use its exact release tag.' }
foreach ($event in @('push', 'pull_request', 'workflow_dispatch')) {
    $ordinary = Resolve-BuildVersion -Commit $hash -InActions $true -EventName $event
    if ($ordinary.Version -ne $hash) { throw "A non-release $event build must use its commit." }
}
foreach ($event in @('push', 'workflow_dispatch')) {
    $tagged = Resolve-BuildVersion -Commit $hash -InActions $true -EventName $event -ReleaseTag 'v1.2.3'
    if ($tagged.Identity -ne 'release:v1.2.3') { throw 'Explicit version-tag builds must use the tag.' }
}
$pr = Resolve-BuildVersion -Commit $hash -InActions $true -EventName pull_request -ReleaseTag 'v1.2.3'
if ($pr.Version -ne $hash) { throw 'PR builds must ignore release tags.' }
$missing = Resolve-BuildVersion -Commit $hash -InActions $true -EventName release
if ($missing.Version -ne $hash) { throw 'An absent release identifier must fall back to the commit.' }
$rejected = $false
try { Resolve-BuildVersion -Commit '' | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'A missing commit must fail the build.' }
Write-Host 'PASS: build version rules (local, release, branch/PR/manual, missing tag and invalid commit).'
