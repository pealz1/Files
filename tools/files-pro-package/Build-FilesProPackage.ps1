param(
	[string]$Configuration = "Release",
	[string]$Platform = "x64",
	[string]$OutputDirectory = "artifacts\files-pro-package",
	[switch]$Unsigned
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\Files.App\Files.App.csproj"
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory
} else {
	Join-Path $repoRoot $OutputDirectory
}

function Resolve-MsPdbCmfExe {
	$vsRoot = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio"
	if (-not (Test-Path -LiteralPath $vsRoot)) {
		return $null
	}

	$candidate = Get-ChildItem -LiteralPath $vsRoot -Recurse -Filter "mspdbcmf.exe" -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -like "*\bin\Hostx64\x64\mspdbcmf.exe" } |
		Sort-Object FullName -Descending |
		Select-Object -First 1

	if ($candidate) {
		return [string]$candidate.FullName
	}

	return $null
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

$properties = @(
	"-p:Platform=$Platform",
	"-p:AppxBundle=Never",
	"-p:UapAppxPackageBuildMode=SideloadOnly",
	"-p:GenerateAppxPackageOnBuild=true",
	"-p:AppxPackageDir=$output\"
)

if ($Unsigned) {
	$properties += "-p:AppxPackageSigningEnabled=false"
}

$msPdbCmfExe = Resolve-MsPdbCmfExe
if (-not [string]::IsNullOrWhiteSpace($msPdbCmfExe)) {
	$properties += "-p:MsPdbCmfExeFullpath=$msPdbCmfExe"
	Write-Host "[Files Pro package] mspdbcmf.exe: $msPdbCmfExe"
}

Write-Host "[Files Pro package] Building $Configuration $Platform package"
dotnet build $project -c $Configuration @properties

Write-Host "[Files Pro package] Output directory: $output"
Get-ChildItem -Path $output -Recurse -File | Select-Object FullName, Length, LastWriteTime
