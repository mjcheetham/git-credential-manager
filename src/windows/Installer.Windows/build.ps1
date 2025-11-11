# Inputs
param (
	$Configuration = "Debug",
	$Runtime,
	[Parameter(Mandatory)] $ISCC
)

Write-Host "Building Installer.Windows..."

# Directories
$THISDIR = $PSScriptRoot
$ROOT = (Get-Item $THISDIR).Parent.Parent.Parent.FullName
$SRC = "$ROOT\src"
$OUT = "$ROOT\out"
$INSTALLER_SRC = "$SRC\windows\Installer.Windows"
$INSTALLER_OUT = "$OUT\windows\Installer.Windows"

# Determine the runtime if not provided
if (-not $Runtime)
{
	$arch = (Get-CimInstance Win32_Processor).Architecture
	switch ($arch) {
		0 {
			$Runtime = "win-x86"
			break
		}
		9 {
			$Runtime = "win-x64"
			break
		}
		12 {
			$Runtime = "win-arm64"
			break
		}
		Default {
			Write-Error "Unsupported architecture: $arch"
			exit 1
		}
	}
}

$OUTDIR = "$INSTALLER_OUT\pkg\$Configuration"
$PAYLOAD = "$OUTDIR\$Runtime"

# Layout and pack
& "$INSTALLER_SRC\layout.ps1" -Configuration "$CONFIGURATION" -Output "$PAYLOAD" -Runtime "$RUNTIME"
& "$INSTALLER_SRC\pack.ps1" -Payload "$PAYLOAD" -Runtime "$RUNTIME" -Output "$OUTDIR" -ISCC $ISCC

Write-Host "Build of Installer.Windows complete."
