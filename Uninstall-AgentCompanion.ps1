[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $bytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($installDirectory.ToUpperInvariant()))
}
finally {
    $sha256.Dispose()
}
$installId = -join ($bytes[0..7] | ForEach-Object { $_.ToString('X2') })
$entryNames = @("AgentCompanion-$installId", "AgentPet-$installId")
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$executablePath = Join-Path $installDirectory 'AgentCompanion.exe'

$running = Get-CimInstance Win32_Process -Filter "Name = 'AgentCompanion.exe'" |
    Where-Object { $_.ExecutablePath -and [System.IO.Path]::GetFullPath($_.ExecutablePath) -eq $executablePath }
if ($running) {
    throw 'Exit AgentCompanion from the tray before uninstalling.'
}

foreach ($entryName in $entryNames) {
    Remove-ItemProperty -Path $runKeyPath -Name $entryName -ErrorAction SilentlyContinue
}

if ($RemoveData) {
    $instanceId = $installId.ToLowerInvariant()
    $dataDirectory = Join-Path $env:LOCALAPPDATA "AgentCompanion\instances\$instanceId"
    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
    }
}

Write-Output 'Startup registration was removed.'
Write-Output 'Delete the installation folder after this script exits.'
