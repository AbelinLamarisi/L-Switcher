function Remove-UnlockedExecutableBackups {
    param([string]$Directory)
    foreach ($old in [IO.Directory]::GetFiles($Directory, 'L-Switcher-*.previous.exe')) {
        try { [IO.File]::Delete($old) } catch [IO.IOException] { } catch [UnauthorizedAccessException] { }
    }
}

function Publish-Executable {
    param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination, [Parameter(Mandatory=$true)][string]$BackupDirectory)
    $ErrorActionPreference = 'Stop'
    $sourcePath = [IO.Path]::GetFullPath($Source)
    $targetPath = [IO.Path]::GetFullPath($Destination)
    $backupPath = [IO.Path]::GetFullPath($BackupDirectory)
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Built executable not found: $sourcePath" }
    if ($sourcePath.Equals($targetPath, [StringComparison]::OrdinalIgnoreCase)) { return $targetPath }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($targetPath)) | Out-Null
    [IO.Directory]::CreateDirectory($backupPath) | Out-Null
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $lockId = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($targetPath.ToLowerInvariant()))).Replace('-', '') } finally { $hasher.Dispose() }
    $mutex = [Threading.Mutex]::new($false, 'Local\L-Switcher-Publish-' + $lockId)
    $owned = $false; $temporary = $targetPath + '.new-' + [Guid]::NewGuid().ToString('N')
    try {
        try { $owned = $mutex.WaitOne(10000) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (!$owned) { throw 'Another executable update is in progress.' }
        Remove-UnlockedExecutableBackups $backupPath
        if ((Test-Path -LiteralPath $targetPath) -and (Get-FileHash -LiteralPath $sourcePath).Hash -eq (Get-FileHash -LiteralPath $targetPath).Hash) { return $targetPath }
        [IO.File]::Copy($sourcePath, $temporary)
        if (Test-Path -LiteralPath $targetPath) {
            $previous = Join-Path $backupPath ('L-Switcher-' + [Guid]::NewGuid().ToString('N') + '.previous.exe')
            try { [IO.File]::Replace($temporary, $targetPath, $previous) }
            catch {
                if (!(Test-Path -LiteralPath $temporary) -or !(Test-Path -LiteralPath $targetPath) -or (Test-Path -LiteralPath $previous)) { throw }
                # Windows can rename a running executable even when it cannot overwrite it.
                # Its mapped image stays alive; the next launch uses the new stable path.
                try { [IO.File]::Move($targetPath, $previous) }
                catch { throw "Cannot update $targetPath. Close the application or program holding the file, then rebuild. The existing executable was retained." }
                try { [IO.File]::Move($temporary, $targetPath) }
                catch { [IO.File]::Move($previous, $targetPath); throw }
            }
        } else { [IO.File]::Move($temporary, $targetPath) }
        # Keep only backups that Windows still needs for a running mapped image.
        Remove-UnlockedExecutableBackups $backupPath
        return $targetPath
    } finally {
        if (Test-Path -LiteralPath $temporary) { try { [IO.File]::Delete($temporary) } catch { } }
        if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose()
    }
}
