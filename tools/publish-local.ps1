<#
.SYNOPSIS
    Build and package the IC10 VS Code extension with its Rust language server.

.DESCRIPTION
    This script automates the complete build and packaging process for the IC10 Language
    Extension. It handles building the Rust language server (ic10lsp), bundling the
    TypeScript extension, copying binaries, and creating a VSIX package.
    
    Optionally, it can also publish the extension to the Visual Studio Marketplace.

.PARAMETER PackageOnly
    Only create the VSIX package without publishing to marketplace.

.PARAMETER Publish
    Build, package, and publish to the Visual Studio Marketplace.
    Requires a Personal Access Token (PAT) from Azure DevOps.

.PARAMETER PAT
    Personal Access Token for publishing to the VS Marketplace.
    Can also be set via the VSCE_PAT environment variable.
    Keep this secure - never commit it to source control!

.PARAMETER Publisher
    Publisher ID for the VS Marketplace.
    Can also be set via the VSCE_PUBLISHER environment variable.

.EXAMPLE
    # Package only (creates a .vsix file locally)
    .\tools\publish-local.ps1 -PackageOnly

.EXAMPLE
    # Package and publish (securely pass PAT)
    $env:VSCE_PAT = "your-personal-access-token"
    .\tools\publish-local.ps1 -Publish

.EXAMPLE
    # Package and publish with inline parameters (less secure)
    .\tools\publish-local.ps1 -Publish -PAT 'your-pat' -Publisher 'your-publisher-id'

.NOTES
    Requirements:
    - Rust toolchain (cargo)
    - Node.js (npm)
    - Git (for versioning)
    
    The script will:
    1. Auto-discover the ic10lsp and extension folders in the repository
    2. Build ic10lsp in release mode (falls back to debug if release fails)
    3. Copy the compiled binary to the extension's bin/ folder
    4. Install npm dependencies if needed
    5. Run esbuild to bundle the TypeScript code
    6. Package everything into a .vsix file
    7. Optionally publish to the marketplace

.LINK
    https://github.com/FlorpyDorpinator/IC10-Code-Extension

#>

param(
    [switch]$Publish,
    [switch]$PackageOnly,
    [string]$PAT = $env:VSCE_PAT,
    [string]$Publisher = $env:VSCE_PUBLISHER
)

# Enable strict mode for better error catching
Set-StrictMode -Version Latest

# ============================================================================
# Path Discovery
# ============================================================================
# Find the root directory and locate ic10lsp and extension folders

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path "$root\.." | Select-Object -ExpandProperty Path

# Initialize path variables
$lspPath = $null
$extensionPath = $null

# Try known default locations first (fastest path)
$knownLspCandidates = @(
    (Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\FlorpyDorp IC10\ic10lsp'),
    (Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\ic10lsp')
)
$knownExt = Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support'

foreach ($cand in $knownLspCandidates) { 
    if (-not $lspPath -and (Test-Path $cand)) { 
        $lspPath = $cand 
    } 
}
if (Test-Path $knownExt) { 
    $extensionPath = $knownExt 
}

# ============================================================================
# Auto-Discovery (fallback if known locations don't exist)
# ============================================================================

if (-not $lspPath) {
    Write-Host "Known LSP paths not found, scanning repository..."
    $lspCandidate = Get-ChildItem -Path $repoRoot -Recurse -Directory -Filter ic10lsp -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'Cargo.toml') } |
        Select-Object -First 1
    if ($lspCandidate) { 
        $lspPath = $lspCandidate.FullName 
    }
}

if (-not $extensionPath) {
    Write-Host "Known extension path not found, scanning repository..."
    # Prefer the folder literally named "FlorpyDorp Language Support"
    $extCandidate = Get-ChildItem -Path $repoRoot -Recurse -Directory -Filter 'FlorpyDorp Language Support' -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'package.json') } |
        Select-Object -First 1

    if (-not $extCandidate) {
        # Fallback: any folder containing a package.json with name "ic10-language-support"
        $pkgFiles = Get-ChildItem -Path $repoRoot -Recurse -Filter package.json -ErrorAction SilentlyContinue
        foreach ($pkg in $pkgFiles) {
            try {
                $json = Get-Content -Raw -Path $pkg.FullName | ConvertFrom-Json
                if ($json.name -eq 'ic10-language-support') {
                    $extCandidate = Get-Item (Split-Path -Parent $pkg.FullName)
                    break
                }
            } catch { 
                # Ignore JSON parse errors
            }
        }
    }

    if ($extCandidate) { 
        $extensionPath = $extCandidate.FullName 
    }
}

# ============================================================================
# Validation
# ============================================================================
# Ensure we found both required directories

if (-not $lspPath) {
    Write-Error "ic10lsp folder not found. Tried default path and auto-discovery. Please verify the repository contains the 'ic10lsp' crate."
    exit 1
}

if (-not $extensionPath) {
    Write-Error "Extension folder not found. Tried default path and auto-discovery. Please verify the extension folder (containing package.json)."
    exit 1
}

$binPath = Join-Path $extensionPath 'bin'

Write-Host "Repository root: $repoRoot"
Write-Host "LSP path: $lspPath"
Write-Host "Extension path: $extensionPath"

# ============================================================================
# Step 1: Build the Rust Language Server
# ============================================================================
# Build ic10lsp in release mode for optimal performance. Falls back to debug
# mode if release build fails.

Push-Location $lspPath
Write-Host "Building ic10lsp (release) with cargo..."
cargo build --release
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Release build failed (exit $LASTEXITCODE). Attempting debug build instead..."
    cargo build
    if ($LASTEXITCODE -ne 0) { Write-Error "Debug build also failed"; Pop-Location; exit $LASTEXITCODE }
}
Pop-Location

# ============================================================================
# Step 2: Copy the LSP Binary
# ============================================================================
# Copy the compiled ic10lsp binary to the extension's bin/ folder so it can
# be packaged with the VSIX. Supports both Windows (.exe) and Unix binaries.

New-Item -ItemType Directory -Force -Path $binPath | Out-Null

${exeSourceReleaseWin} = Join-Path $lspPath 'target\release\ic10lsp.exe'
${exeSourceReleaseUnix} = Join-Path $lspPath 'target/release/ic10lsp'
${exeSourceDebugWin} = Join-Path $lspPath 'target\debug\ic10lsp.exe'
${exeSourceDebugUnix} = Join-Path $lspPath 'target/debug/ic10lsp'

if (Test-Path ${exeSourceReleaseWin}) {
    Copy-Item -Path ${exeSourceReleaseWin} -Destination (Join-Path $binPath 'ic10lsp.exe') -Force
    Write-Host "Copied release Windows binary to $binPath"
} elseif (Test-Path ${exeSourceReleaseUnix}) {
    Copy-Item -Path ${exeSourceReleaseUnix} -Destination (Join-Path $binPath 'ic10lsp') -Force
    Write-Host "Copied release Unix binary to $binPath"
} elseif (Test-Path ${exeSourceDebugWin}) {
    Copy-Item -Path ${exeSourceDebugWin} -Destination (Join-Path $binPath 'ic10lsp.exe') -Force
    Write-Host "Release not found; copied debug Windows binary to $binPath"
} elseif (Test-Path ${exeSourceDebugUnix}) {
    Copy-Item -Path ${exeSourceDebugUnix} -Destination (Join-Path $binPath 'ic10lsp') -Force
    Write-Host "Release not found; copied debug Unix binary to $binPath"
} else {
    Write-Warning "No ic10lsp binary found (release or debug). Check cargo build output for errors."
}

# ============================================================================
# Step 3: Build the TypeScript Extension
# ============================================================================
# Install npm dependencies and bundle the extension using esbuild

Push-Location $extensionPath
Write-Host "Installing npm dependencies and building extension..."
if (-not (Test-Path node_modules)) { npm install }
npm run esbuild
if ($LASTEXITCODE -ne 0) { Write-Error "esbuild failed"; exit $LASTEXITCODE }

# ============================================================================
# Step 4: Package the VSIX
# ============================================================================
# Create a .vsix file that can be installed in VS Code or published to the
# Marketplace. The VSIX includes the bundled extension code and LSP binary.

Write-Host "Packaging VSIX..."
npx -y @vscode/vsce@latest package
if ($LASTEXITCODE -ne 0) { Write-Error "VSIX packaging failed"; exit $LASTEXITCODE }

# ============================================================================
# Step 5: Publish to Marketplace (Optional)
# ============================================================================
# If -Publish flag is set and a PAT is provided, publish to the Visual Studio
# Marketplace. This requires a Personal Access Token from Azure DevOps.

if ($Publish -and $PAT) {
    Write-Host "Publishing VSIX to Visual Studio Marketplace..."
    if ($Publisher) {
        Write-Host "Publisher: $Publisher"
    }
    npx -y @vscode/vsce@latest publish --pat $PAT
    if ($LASTEXITCODE -ne 0) { Write-Error "VSCE publish failed"; exit $LASTEXITCODE }
    Write-Host "Publish step completed. Check the Marketplace for the new version."
} elseif ($Publish -and -not $PAT) {
    Write-Warning "Publish requested but no PAT provided. Set -PAT or the VSCE_PAT environment variable. Skipping publish."
}

Write-Host "Done. The VSIX file is in: $extensionPath"
Pop-Location
