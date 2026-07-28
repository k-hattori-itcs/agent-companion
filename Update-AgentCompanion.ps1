[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumPath,

    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
$executablePath = Join-Path $installDirectory 'AgentCompanion.exe'
$archive = Get-Item -LiteralPath $ArchivePath -ErrorAction Stop
$checksumFile = Get-Item -LiteralPath $ChecksumPath -ErrorAction Stop
$requiredFiles = @(
    'AgentCompanion.exe',
    'LICENSE',
    'NOTICE',
    'THIRD_PARTY_NOTICES.md',
    'INSTALL.md',
    'Update-AgentCompanion.ps1',
    'Uninstall-AgentCompanion.ps1'
)

function Get-ExpectedHash([string]$path, [string]$fileName) {
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        if ($line -match '^\s*([A-Fa-f0-9]{64})\s+\*?(.+?)\s*$' -and $Matches[2] -eq $fileName) {
            return $Matches[1].ToLowerInvariant()
        }
    }

    throw "No SHA256 entry for $fileName was found in $path."
}

function Assert-NotRunning([string]$path) {
    $running = Get-CimInstance Win32_Process -Filter "Name = 'AgentCompanion.exe'" |
        Where-Object { $_.ExecutablePath -and [System.IO.Path]::GetFullPath($_.ExecutablePath) -eq $path }
    if ($running) {
        throw 'Exit AgentCompanion from the tray before updating.'
    }
}

$expectedHash = Get-ExpectedHash $checksumFile.FullName $archive.Name
$actualHash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw 'The release archive SHA256 does not match SHA256SUMS.txt.'
}

Assert-NotRunning $executablePath
$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AgentCompanion.Update." + [Guid]::NewGuid().ToString('N'))
$backupPath = $executablePath + '.previous'
$backupCreatedByThisUpdate = $false

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive.FullName)
    try {
        $entries = @($zip.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
        $entryNames = @($entries | ForEach-Object { $_.FullName })
        if ($entryNames.Count -ne $requiredFiles.Count -or @($entryNames | Where-Object { $_ -notin $requiredFiles }).Count -gt 0) {
            throw 'The release archive contains unexpected files.'
        }
        foreach ($requiredFile in $requiredFiles) {
            if ($entryNames -notcontains $requiredFile) {
                throw "The release archive is missing $requiredFile."
            }
        }

        New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
        foreach ($entry in $entries) {
            $destination = Join-Path $stagingDirectory $entry.Name
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        }
    }
    finally {
        $zip.Dispose()
    }

    $newExecutable = Join-Path $stagingDirectory 'AgentCompanion.exe'

    if (Test-Path -LiteralPath $executablePath) {
        Copy-Item -LiteralPath $executablePath -Destination $backupPath -Force
        $backupCreatedByThisUpdate = $true
    }

    $newExecutablePath = $executablePath + '.new'
    Copy-Item -LiteralPath $newExecutable -Destination $newExecutablePath -Force
    if (Test-Path -LiteralPath $executablePath) {
        Remove-Item -LiteralPath $executablePath -Force
    }
    Move-Item -LiteralPath $newExecutablePath -Destination $executablePath

    foreach ($requiredFile in ($requiredFiles | Where-Object { $_ -ne 'AgentCompanion.exe' })) {
        Copy-Item -LiteralPath (Join-Path $stagingDirectory $requiredFile) -Destination (Join-Path $installDirectory $requiredFile) -Force
    }

    Write-Output 'AgentCompanion was updated successfully.'
    if ($Restart) {
        Start-Process -FilePath $executablePath
    }
}
catch {
    if ($backupCreatedByThisUpdate -and (Test-Path -LiteralPath $backupPath)) {
        Copy-Item -LiteralPath $backupPath -Destination $executablePath -Force
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
