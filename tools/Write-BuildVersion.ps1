param([string]$RepositoryRoot, [string]$OutputPath)
$ErrorActionPreference = 'Stop'

function Resolve-BuildVersion {
    param([string]$Commit, [bool]$InActions = $false, [string]$EventName, [string]$ReleaseTag)
    if ($Commit -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') { throw 'A full Git commit hash is required.' }
    $commitId = $Commit.ToLowerInvariant()
    if ($InActions -and $EventName -in @('release', 'push', 'workflow_dispatch') -and ![string]::IsNullOrWhiteSpace($ReleaseTag)) {
        return [pscustomobject]@{ Version = $ReleaseTag; Identity = 'release:' + $ReleaseTag }
    }
    return [pscustomobject]@{ Version = $commitId; Identity = 'commit:' + $commitId }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (!$RepositoryRoot -or !$OutputPath) { throw 'RepositoryRoot and OutputPath are required.' }
    $commitHash = & git -C $RepositoryRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the Git commit for this build.' }
    $version = Resolve-BuildVersion -Commit $commitHash -InActions ($env:GITHUB_ACTIONS -eq 'true') -EventName $env:GITHUB_EVENT_NAME -ReleaseTag $env:LSWITCHER_RELEASE_TAG
    if ($version.Identity.StartsWith('release:')) {
        $tagCommit = & git -C $RepositoryRoot rev-parse --verify ('refs/tags/' + $version.Version + '^{commit}')
        if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $commitHash) { throw 'The release tag does not identify the checked-out commit.' }
    }
    # This script never calls GitHub, gh, or any network endpoint, even locally on a tagged commit.
    $escapedVersion = $version.Version.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n')
    $escapedIdentity = $version.Identity.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n')
    $source = @"
// Generated from build context. Do not edit or commit this file.
[assembly: System.Reflection.AssemblyInformationalVersion("$escapedVersion")]
namespace LSwitcher { internal static class BuildVersion { internal const string Identity = "$escapedIdentity"; } }
"@
    $target = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
    if (!(Test-Path -LiteralPath $target) -or [System.IO.File]::ReadAllText($target) -ne $source) { [System.IO.File]::WriteAllText($target, $source, [System.Text.UTF8Encoding]::new($false)) }
    Write-Host ('Build version: ' + $version.Version)
}
