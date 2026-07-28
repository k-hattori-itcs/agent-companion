[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$archive = Get-Item -LiteralPath $ArchivePath -ErrorAction Stop
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AgentCompanion.ReleaseCheck." + [Guid]::NewGuid().ToString('N'))
$requiredFiles = @('AgentCompanion.exe', 'LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md', 'INSTALL.md', 'Update-AgentCompanion.ps1', 'Uninstall-AgentCompanion.ps1')

try {
    Expand-Archive -LiteralPath $archive.FullName -DestinationPath $temporaryDirectory
    $files = Get-ChildItem -LiteralPath $temporaryDirectory -Recurse -File
    $relativeNames = @($files | ForEach-Object {
        $_.FullName.Substring($temporaryDirectory.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    })

    foreach ($required in $requiredFiles) {
        if ($relativeNames -notcontains $required) {
            throw "Release archive is missing required file: $required"
        }
    }

    $unexpectedFiles = @($relativeNames | Where-Object { $_ -notin $requiredFiles })
    if ($unexpectedFiles.Count -gt 0) {
        throw "Release archive contains unexpected files: $($unexpectedFiles -join ', ')"
    }

    if ($relativeNames | Where-Object { $_ -match '(?i)\.pdb$' }) {
        throw 'Release archive must not contain PDB files.'
    }

    $exePath = Join-Path $temporaryDirectory 'AgentCompanion.exe'
    $binaryText = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($exePath))
    foreach ($marker in @('C:\Users\', 'codex-pet-limit-ring', 'k-hattori')) {
        if ($binaryText.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Release executable contains a local build marker: $marker"
        }
    }

    Write-Output "Release package check passed: $($archive.FullName)"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
