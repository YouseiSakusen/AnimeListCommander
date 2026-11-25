# AnimeListCommander - Release発行スクリプト
# 使用例: .\Publish.ps1 -OutputDirectory 'H:\AnimeListCommander-Test'

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'AnimeListCommander\AnimeListCommander.csproj'

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
	throw "プロジェクトが見つかりません: $project"
}

$target = [System.IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot = [System.IO.Path]::GetFullPath($repoRoot)

if ($target.TrimEnd('\', '/') -eq $sourceRoot.TrimEnd('\', '/')) {
	throw '発行先にリポジトリのルートは指定できません。'
}

New-Item -ItemType Directory -Path $target -Force | Out-Null

Write-Host "AnimeListCommanderを発行します: $target"

& dotnet publish $project `
	-c Release `
	-r win-x64 `
	--self-contained false `
	-o $target `
	-p:PublishSingleFile=false `
	-p:DebugSymbols=true `
	-p:DebugType=portable

if ($LASTEXITCODE -ne 0) {
	throw "発行に失敗しました。終了コード: $LASTEXITCODE"
}

$pdbPath = Join-Path $target 'AnimeListCommander.pdb'

if (-not (Test-Path -LiteralPath $pdbPath -PathType Leaf)) {
	throw "PDBファイルが出力されていません: $pdbPath"
}

Write-Host ''
Write-Host "発行完了: $target"
