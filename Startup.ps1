param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Quota Glass.lnk'
if ($Remove) {
    if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
    Write-Output 'Startup disabled.'
    return
}
$exePath = Join-Path $PSScriptRoot 'CodexQuotaGlass.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Run Startup.ps1 from the extracted portable package.' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.Arguments = '--background'
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Codex Quota Glass'
$shortcut.Save()
Write-Output 'Startup enabled for the current user. Keep this folder in its current location.'
