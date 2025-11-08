<#
publish-local.ps1

Helper script to build the Rust language server, build the extension JS, package a VSIX and optionally publish to the Visual Studio Marketplace.

Usage examples:
  # Package only
  .\tools\publish-local.ps1 -PackageOnly

  # Package and publish (recommended to pass PAT securely)
  .\tools\publish-local.ps1 -Publish -PAT 'your-personal-access-token' -Publisher 'your-publisher-id'

Notes:
- Requires: Rust (cargo), Node.js (npm), and Git (for versioning if you bump the package.json).
- This script will build the `ic10lsp` release binary and copy it to the extension `bin` folder then run `npx @vscode/vsce` to package/publish.
- Do NOT pass your PAT on a shared machine or commit it into files; prefer interactive prompt or environment variable.
#>

param(
    [switch]$Publish,
    [switch]$PackageOnly,
    [string]$PAT = $env:VSCE_PAT,
    [string]$Publisher = $env:VSCE_PUBLISHER
)

Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path "$root\.." | Select-Object -ExpandProperty Path

# Adjust these relative paths if your repository layout differs
$lspPath = $null
$extensionPath = $null

# Try known default locations first
$knownLspCandidates = @(
    (Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\FlorpyDorp IC10\ic10lsp'),
    (Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\ic10lsp')
)
$knownExt = Join-Path $repoRoot 'Stationeers-ic10-main\Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support'

foreach ($cand in $knownLspCandidates) { if (-not $lspPath -and (Test-Path $cand)) { $lspPath = $cand } }
if (Test-Path $knownExt) { $extensionPath = $knownExt }

# Auto-discover if not found in known locations
if (-not $lspPath) {
    $lspCandidate = Get-ChildItem -Path $repoRoot -Recurse -Directory -Filter ic10lsp -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'Cargo.toml') } |
        Select-Object -First 1
    if ($lspCandidate) { $lspPath = $lspCandidate.FullName }
}

if (-not $extensionPath) {
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
            } catch { }
        }
    }

    if ($extCandidate) { $extensionPath = $extCandidate.FullName }
}

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

Push-Location $lspPath
Write-Host "Building ic10lsp (release) with cargo..."
cargo build --release
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Release build failed (exit $LASTEXITCODE). Attempting debug build instead..."
    cargo build
    if ($LASTEXITCODE -ne 0) { Write-Error "Debug build also failed"; Pop-Location; exit $LASTEXITCODE }
}
Pop-Location

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

Push-Location $extensionPath
Write-Host "Installing npm dependencies and building extension..."
if (-not (Test-Path node_modules)) { npm install }
npm run esbuild
if ($LASTEXITCODE -ne 0) { Write-Error "esbuild failed"; exit $LASTEXITCODE }

Write-Host "Packaging VSIX..."
npx -y @vscode/vsce@latest package
if ($LASTEXITCODE -ne 0) { Write-Error "VSIX packaging failed"; exit $LASTEXITCODE }

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
