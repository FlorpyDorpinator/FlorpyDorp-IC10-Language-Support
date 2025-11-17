# Download and install cross-platform binaries from GitHub Actions
# Requires GitHub CLI (gh) to be installed: https://cli.github.com/

param(
    [string]$RunId,
    [switch]$Latest,
    [switch]$Help
)

$BinDir = Join-Path $PSScriptRoot "..\Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support\bin"
$TempDir = Join-Path $env:TEMP "ic10lsp-artifacts"

function Show-Help {
    Write-Host "Download IC10 LSP Cross-Platform Binaries"
    Write-Host "=========================================="
    Write-Host ""
    Write-Host "Usage: .\download-artifacts.ps1 [options]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Latest     Download artifacts from the latest successful workflow run"
    Write-Host "  -RunId      Specify a specific workflow run ID"
    Write-Host "  -Help       Show this help message"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\download-artifacts.ps1 -Latest"
    Write-Host "  .\download-artifacts.ps1 -RunId 1234567890"
    Write-Host ""
    Write-Host "Requires: GitHub CLI (gh) - Install from https://cli.github.com/"
}

# Check if gh is installed
if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "Error: GitHub CLI (gh) is not installed." -ForegroundColor Red
    Write-Host "Install from: https://cli.github.com/" -ForegroundColor Yellow
    Write-Host "Or use winget: winget install GitHub.cli" -ForegroundColor Yellow
    exit 1
}

if ($Help) {
    Show-Help
    exit 0
}

# Create temp directory
if (!(Test-Path $TempDir)) {
    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
}

Write-Host "IC10 LSP Cross-Platform Binary Installer" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Get the workflow run
if ($Latest) {
    Write-Host "Finding latest successful workflow run..." -ForegroundColor Yellow
    $run = gh run list --workflow="build-lsp.yml" --status=success --limit=1 --json databaseId,conclusion | ConvertFrom-Json
    if ($run.Count -eq 0) {
        Write-Host "Error: No successful workflow runs found." -ForegroundColor Red
        exit 1
    }
    $RunId = $run[0].databaseId
    Write-Host "Using workflow run: $RunId" -ForegroundColor Green
} elseif (!$RunId) {
    Write-Host "Error: Please specify -Latest or -RunId <id>" -ForegroundColor Red
    Show-Help
    exit 1
}

Write-Host ""
Write-Host "Downloading artifacts from workflow run $RunId..." -ForegroundColor Yellow

# Download all artifacts
try {
    Push-Location $TempDir
    gh run download $RunId --repo FlorpyDorpinator/IC10-Code-Extension
    Pop-Location
    Write-Host "[OK] Artifacts downloaded" -ForegroundColor Green
} catch {
    Write-Host "[FAIL] Failed to download artifacts: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Installing binaries..." -ForegroundColor Yellow

# Mapping of artifact folders to binary names
$artifacts = @{
    "ic10lsp-x86_64-pc-windows-msvc" = @{
        Source = "ic10lsp.exe"
        Dest = "ic10lsp-win32.exe"
    }
    "ic10lsp-x86_64-unknown-linux-gnu" = @{
        Source = "ic10lsp"
        Dest = "ic10lsp-linux"
    }
    "ic10lsp-x86_64-apple-darwin" = @{
        Source = "ic10lsp"
        Dest = "ic10lsp-darwin"
    }
    "ic10lsp-aarch64-apple-darwin" = @{
        Source = "ic10lsp"
        Dest = "ic10lsp-darwin-arm64"
    }
}

$successCount = 0
$totalCount = $artifacts.Count

foreach ($artifactName in $artifacts.Keys) {
    $config = $artifacts[$artifactName]
    $sourcePath = Join-Path $TempDir "$artifactName\$($config.Source)"
    $destPath = Join-Path $BinDir $config.Dest
    
    if (Test-Path $sourcePath) {
        try {
            # Ensure bin directory exists
            if (!(Test-Path $BinDir)) {
                New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
            }
            
            Copy-Item -Force $sourcePath -Destination $destPath
            $fileSize = (Get-Item $destPath).Length / 1MB
            $formattedSize = "{0:N2}" -f $fileSize
            Write-Host "  [OK] $($config.Dest) ($formattedSize MB)" -ForegroundColor Green
            $successCount++
        } catch {
            Write-Host "  [FAIL] Failed to copy $($config.Dest): $_" -ForegroundColor Red
        }
    } else {
        Write-Host "  [SKIP] $artifactName not found in download" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Installation Summary" -ForegroundColor Cyan
Write-Host "====================" -ForegroundColor Cyan
if ($successCount -eq $totalCount) {
    Write-Host "Successfully installed: $successCount / $totalCount binaries" -ForegroundColor Green
} else {
    Write-Host "Successfully installed: $successCount / $totalCount binaries" -ForegroundColor Yellow
}

if ($successCount -eq $totalCount) {
    Write-Host ""
    Write-Host "All binaries installed successfully!" -ForegroundColor Green
    Write-Host "Binaries are located in: $BinDir" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. cd 'Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support'" -ForegroundColor White
    Write-Host "  2. npm run esbuild" -ForegroundColor White
    Write-Host "  3. vsce package" -ForegroundColor White
    Write-Host "  4. vsce publish" -ForegroundColor White
}

# Cleanup temp directory
Write-Host ""
Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
Write-Host "Done!" -ForegroundColor Green
