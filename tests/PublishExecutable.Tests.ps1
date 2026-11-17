param([Parameter(Mandatory=$true)][string]$TestExecutable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\Publish-Executable.ps1')
$testBase = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($TestExecutable))
$folder = Join-Path $testBase ('publish tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
$source = Join-Path $folder 'candidate.exe'; $target = Join-Path $folder 'L-Switcher.exe'; $backups = Join-Path $folder 'backups'
$worker = $null; $lockStream = $null; $ready = Join-Path $folder 'ready'
try {
    [IO.File]::WriteAllText($source, 'first build')
    Publish-Executable $source $target $backups | Out-Null
    if ([IO.File]::ReadAllText($target) -ne 'first build') { throw 'First publication failed.' }
    [IO.File]::WriteAllText($source, 'second build'); Publish-Executable $source $target $backups | Out-Null
    if ([IO.File]::ReadAllText($target) -ne 'second build') { throw 'Stable executable was not replaced.' }
    $lockStream = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    [IO.File]::WriteAllText($source, 'locked replacement'); $failed = $false
    try { Publish-Executable $source $target $backups | Out-Null } catch { $failed = $true }
    if (!$failed -or [IO.File]::ReadAllText($target) -ne 'second build') { throw 'An unreplaceable executable must remain intact.' }
    $lockStream.Dispose(); $lockStream = $null
    if (@(Get-ChildItem -LiteralPath $folder -Filter '*.new-*').Count -ne 0) { throw 'A failed update left temporary files.' }
    [IO.File]::Copy([IO.Path]::GetFullPath($TestExecutable), $target, $true)
    $start = [Diagnostics.ProcessStartInfo]::new($target, '--instance-worker "Local\L-Switcher-Publish-Test-' + [Guid]::NewGuid().ToString('N') + '" "' + $ready + '"')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $worker = [Diagnostics.Process]::Start($start)
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $ready) -and !$worker.HasExited -and $elapsed.ElapsedMilliseconds -lt 10000) { Start-Sleep -Milliseconds 20 }
    if (!(Test-Path -LiteralPath $ready)) { throw 'Isolated running executable did not start.' }
    [IO.File]::WriteAllText($source, 'new build while old is running'); Publish-Executable $source $target $backups | Out-Null
    if ($worker.HasExited -or [IO.File]::ReadAllText($target) -ne 'new build while old is running') { throw 'Publishing must keep the old process alive and update the stable address.' }
    [IO.File]::WriteAllText($ready + '.exit', 'exit')
    if (!$worker.WaitForExit(5000) -or $worker.ExitCode -ne 0) { throw 'The original process did not finish normally after publication.' }
    Publish-Executable $source $target $backups | Out-Null
    if (@([IO.Directory]::GetFiles($backups, '*.previous.exe')).Count -ne 0) { throw 'Unlocked old builds were not cleaned up.' }
    Write-Host 'PASS: stable output publication, locked-file preservation, running-executable replacement and backup cleanup.'
} finally {
    if ($lockStream) { $lockStream.Dispose() }
    if ($worker) { if (!$worker.HasExited) { [IO.File]::WriteAllText($ready + '.exit', 'exit'); if (!$worker.WaitForExit(5000)) { $worker.Kill(); $worker.WaitForExit() } }; $worker.Dispose() }
    $resolvedFolder = [IO.Path]::GetFullPath($folder)
    if (!$resolvedFolder.StartsWith($testBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test cleanup directory.' }
    Remove-Item -LiteralPath $resolvedFolder -Recurse -Force
}
