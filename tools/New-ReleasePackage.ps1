[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishPath = [System.IO.Path]::GetFullPath($PublishDirectory)
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$normalizedVersion = $Version.TrimStart('v')

if ([string]::IsNullOrWhiteSpace($normalizedVersion)) {
    throw 'Version must not be empty.'
}

$requiredFiles = @(
    @{ Source = (Join-Path $publishPath 'AgentCompanion.exe'); Name = 'AgentCompanion.exe' },
    @{ Source = (Join-Path $repositoryRoot 'LICENSE'); Name = 'LICENSE' },
    @{ Source = (Join-Path $repositoryRoot 'NOTICE'); Name = 'NOTICE' },
    @{ Source = (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md'); Name = 'THIRD_PARTY_NOTICES.md' },
    @{ Source = (Join-Path $repositoryRoot 'INSTALL.md'); Name = 'INSTALL.md' },
    @{ Source = (Join-Path $repositoryRoot 'Update-AgentCompanion.ps1'); Name = 'Update-AgentCompanion.ps1' },
    @{ Source = (Join-Path $repositoryRoot 'Uninstall-AgentCompanion.ps1'); Name = 'Uninstall-AgentCompanion.ps1' }
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "Required release file is missing: $($file.Source)"
    }
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$stagingPath = Join-Path $outputPath 'package'
if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingPath | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $stagingPath $file.Name)
}

$archiveName = "AgentCompanion-v$normalizedVersion-win-x64.zip"
$archivePath = Join-Path $outputPath $archiveName
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stagingPath -File | Select-Object -ExpandProperty FullName) -DestinationPath $archivePath
Remove-Item -LiteralPath $stagingPath -Recurse -Force

$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash *$archiveName" | Set-Content -LiteralPath (Join-Path $outputPath 'SHA256SUMS.txt') -Encoding ascii

Write-Output "Created release archive: $archivePath"
