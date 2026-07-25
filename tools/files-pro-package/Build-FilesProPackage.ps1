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

function Resolve-SignToolExe {
	$kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
	if (-not (Test-Path -LiteralPath $kitsRoot)) {
		return $null
	}

	$candidate = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
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
if ($LASTEXITCODE -ne 0) {
	throw "Package build failed with exit code $LASTEXITCODE."
}

if (-not $Unsigned) {
	$certificate = Get-ChildItem -LiteralPath Cert:\CurrentUser\My |
		Where-Object { $_.Subject -eq "CN=Files" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
		Sort-Object NotAfter -Descending |
		Select-Object -First 1
	if (-not $certificate) {
		throw "A valid CN=Files certificate with a private key was not found in Cert:\CurrentUser\My. Use -Unsigned only for non-installable build artifacts."
	}

	$signTool = Resolve-SignToolExe
	if ([string]::IsNullOrWhiteSpace($signTool)) {
		throw "signtool.exe was not found in the Windows SDK."
	}

	$packages = Get-ChildItem -LiteralPath $output -Recurse -File |
		Where-Object { $_.Name -like "Files.App_*.msix" -or $_.Name -like "Files.App_*.appx" } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
	if (-not $packages) {
		throw "The Files Pro package was not found under $output."
	}

	foreach ($package in $packages) {
		Write-Host "[Files Pro package] Signing $($package.FullName)"
		& $signTool sign /sha1 $certificate.Thumbprint /fd SHA256 $package.FullName
		if ($LASTEXITCODE -ne 0) {
			throw "Package signing failed with exit code $LASTEXITCODE."
		}

		& $signTool verify /pa $package.FullName
		if ($LASTEXITCODE -ne 0) {
			throw "Package signature verification failed with exit code $LASTEXITCODE."
		}
	}
}

Write-Host "[Files Pro package] Output directory: $output"
Get-ChildItem -Path $output -Recurse -File | Select-Object FullName, Length, LastWriteTime
