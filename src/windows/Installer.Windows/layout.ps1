# Inputs
param (
	[Parameter(Mandatory)] $Configuration,
	[Parameter(Mandatory)] $Output,
	$Runtime,
	$SymbolOutput
)

Write-Host "Output: $Output"

# Directories
$THISDIR = $PSScriptRoot
$ROOT = (Get-Item $THISDIR).Parent.Parent.Parent.FullName
$SRC = "$ROOT\src"
$GCM_SRC = "$SRC\shared\Git-Credential-Manager"

# Perform pre-execution checks
$PAYLOAD = "$Output"
if ($SymbolOutput)
{
    $SYMBOLS = "$SymbolOutput"
}
else
{
    $SYMBOLS = "$PAYLOAD.sym"
}

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

Write-Host "Building for runtime: $Runtime"

# Clean up any old payload and symbols directories
if (Test-Path -Path $PAYLOAD)
{
    Write-Host "Cleaning old payload directory '$PAYLOAD'..."
    Remove-Item -Recurse "$PAYLOAD" -Force
}

if (Test-Path -Path $SYMBOLS)
{
    Write-Host "Cleaning old symbols directory '$SYMBOLS'..."
    Remove-Item -Recurse "$SYMBOLS" -Force
}

# Ensure payload and symbol directories exist
mkdir -p "$PAYLOAD","$SYMBOLS" | Out-Null

# Publish core application executables
Write-Host "Publishing core application..."
dotnet publish "$GCM_SRC" `
	--configuration "$Configuration" `
	--runtime $Runtime `
	--self-contained `
	--output "$PAYLOAD"

if ($LASTEXITCODE -ne 0) {
	Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
	exit $LASTEXITCODE
}

# Collect symbols
Write-Output "Collecting managed symbols..."
Move-Item -Path "$PAYLOAD/*.pdb" -Destination "$SYMBOLS"

Write-Output "Layout complete."
