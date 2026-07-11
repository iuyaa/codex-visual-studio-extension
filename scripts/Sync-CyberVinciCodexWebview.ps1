[CmdletBinding()]
param(
    [string] $SourceRoot = 'C:\Users\Rodrigo\Desktop\CyberVinci\Modificacoes\Codex\packages\codex\resources'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$destinationRoot = Join-Path $repositoryRoot 'CodexVsix\UI\CodexWebview'
$sourceWebview = Join-Path $SourceRoot 'webview'
$sourceShim = Join-Path $SourceRoot 'codex-acquire-vscode-api-shim.js'

if (-not (Test-Path -LiteralPath (Join-Path $sourceWebview 'index.html'))) {
    throw "Codex webview index was not found under '$sourceWebview'."
}

if (-not (Test-Path -LiteralPath $sourceShim)) {
    throw "Codex VS Code API shim was not found at '$sourceShim'."
}

New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
Copy-Item -Recurse -Force -LiteralPath $sourceWebview -Destination $destinationRoot
Copy-Item -Force -LiteralPath $sourceShim -Destination $destinationRoot

$files = Get-ChildItem -Recurse -File -LiteralPath $destinationRoot
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "Synchronized $($files.Count) Codex webview files ($bytes bytes) into '$destinationRoot'."
