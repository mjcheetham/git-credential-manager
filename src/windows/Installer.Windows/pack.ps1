# Inputs
param (
	[Parameter(Mandatory)] $Payload,
	[Parameter(Mandatory)] $Output,
	[Parameter(Mandatory)] $Runtime,
	[Parameter(Mandatory)] $ISCC
	# Version is computed from the GCM executable file itself by Inno Setup
)

# Directories
$THISDIR = $PSScriptRoot
$ROOT = (Get-Item $THISDIR).Parent.Parent.Parent.FullName
$SRC = "$ROOT\src"
$INSTALLER_SRC = "$SRC\windows\Installer.Windows"

# Perform pre-execution checks
if (-not (Test-Path $Payload)) {
    Write-Error "Could not find '$Payload'. Did you run layout.ps1 first?"
    exit 1
}

if (-not (Test-Path $ISCC)) {
    Write-Error "Could not find Inno Setup Compiler at '$ISCC'. Please install Inno Setup."
    exit 1
}

# Ensure the directory for the output exists
mkdir -p "$Output" | Out-Null

# Build installer packages
Write-Host "Building system installer package..."
& "$ISCC" `
	/O"$Output" `
	/DInstallTarget=system `
	/DPayloadDir="$Payload" `
	/DRuntime="$Runtime" `
	"$INSTALLER_SRC\Setup.iss"

if ($LASTEXITCODE -ne 0) {
	Write-Error "ISCC failed with exit code $LASTEXITCODE"
	exit $LASTEXITCODE
}

Write-Host "Building user installer package..."
& "$ISCC" `
	/O"$Output" `
	/DInstallTarget=user `
	/DPayloadDir="$Payload" `
	/DRuntime="$Runtime" `
	"$INSTALLER_SRC\Setup.iss"

if ($LASTEXITCODE -ne 0) {
	Write-Error "ISCC failed with exit code $LASTEXITCODE"
	exit $LASTEXITCODE
}

Write-Host "Pack complete."
