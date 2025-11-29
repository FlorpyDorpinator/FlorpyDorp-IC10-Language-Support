# Publishing a New Version of the IC10 Extension

This guide walks you through publishing an updated version of your extension after making code changes.

## Prerequisites

Before you start, make sure you have:
- ✅ GitHub CLI installed (`gh`) - Already done!
- ✅ VS Code Extension Manager (`vsce`) - Already installed
- ✅ Node.js and npm - Already installed
- ✅ Rust toolchain - Already installed

## Step-by-Step Publishing Process

### Step 1: Make Your Changes

Edit any files you need to change:
- Extension code in `Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/src/`
- LSP code in `Stationeers-ic10-main/FlorpyDorp IC10/ic10lsp/src/`
- Themes in `Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/themes/`
- Grammar in `Stationeers-ic10-main/FlorpyDorp IC10/tree-sitter-ic10/grammar.js`

### Step 2: Update Version Number

1. Open `Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/package.json`
2. Find the line that says `"version": "1.2.6",` (around line 12)
3. Change it to the next version:
   - Bug fixes: `1.2.6` → `1.2.7` (patch)
   - New features: `1.2.6` → `1.3.0` (minor)
   - Breaking changes: `1.2.6` → `2.0.0` (major)

### Step 3: Update CHANGELOG

1. Open `Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/CHANGELOG.md`
2. Add a new section at the top describing your changes:

```markdown
## [1.2.7] - 2025-11-XX

### 🐛 Bug Fixes
- Fixed issue with...
- Corrected...

### ✨ New Features
- Added support for...
```

### Step 4: Commit Your Changes to GitHub

Open a PowerShell terminal in `C:\Users\marka\Desktop\VS IC10 Extension Repo`:

```powershell
# Add all changed files
git add -A

# Commit with a descriptive message
git commit -m "Your description of changes here"

# Push to GitHub (this triggers the build for Linux/macOS)
git push origin main
```

### Step 5: Wait for GitHub Actions to Build

1. Go to: https://github.com/FlorpyDorpinator/IC10-Code-Extension/actions
2. Wait for the "Build IC10 LSP Cross-Platform" workflow to complete (usually 5-10 minutes)
3. All 4 platform builds should show green checkmarks ✅

### Step 6: Download the Cross-Platform Binaries

Open PowerShell and run the download script:

```powershell
cd "C:\Users\marka\Desktop\VS IC10 Extension Repo\tools"
.\download-artifacts.ps1 -Latest
```

This will:
- Download all 4 platform binaries from GitHub Actions
- Copy them to the correct location with correct names
- Show you a success message when done

### Step 7: Build the Extension

Navigate to the extension folder and build it:

```powershell
cd "C:\Users\marka\Desktop\VS IC10 Extension Repo\Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support"
npm run esbuild
```

You should see output like:
```
out\main.js      789.2kb
Done in 21ms
```

### Step 8: Package the Extension

Create the `.vsix` file that contains everything:

```powershell
vsce package
```

You'll see:
- A warning about file count (ignore this - it's normal)
- A summary showing all included files
- Final message: `DONE Packaged: florpydorp-ic10-language-support-1.2.X.vsix`

### Step 9: Publish to Marketplace

Publish the new version:

```powershell
vsce publish
```

You should see:
- Same build and package steps running again
- `Publishing 'FlorpyDorp.florpydorp-ic10-language-support v1.2.X'...`
- `DONE Published FlorpyDorp.florpydorp-ic10-language-support v1.2.X.`

### Step 10: Verify It's Live

1. Wait 5-10 minutes for the marketplace to process it
2. Go to: https://marketplace.visualstudio.com/items?itemName=FlorpyDorp.florpydorp-ic10-language-support
3. Verify the new version number shows up
4. Check that the CHANGELOG is updated

## All Commands in One Place

For quick reference, here are all the commands in order:

```powershell
# 1. Update version in package.json (manually)
# 2. Update CHANGELOG.md (manually)

# 3. Commit and push
cd "C:\Users\marka\Desktop\VS IC10 Extension Repo"
git add -A
git commit -m "Version 1.2.X: Description of changes"
git push origin main

# 4. Wait for GitHub Actions (check in browser)

# 5. Download binaries
cd "C:\Users\marka\Desktop\VS IC10 Extension Repo\tools"
.\download-artifacts.ps1 -Latest

# 6. Build extension
cd "C:\Users\marka\Desktop\VS IC10 Extension Repo\Stationeers-ic10-main\FlorpyDorp IC10\FlorpyDorp Language Support"
npm run esbuild

# 7. Package
vsce package

# 8. Publish
vsce publish
```

## Troubleshooting

### "gh not found" Error
- Close and reopen PowerShell terminal
- Or run: `$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")`

### GitHub Actions Build Failed
- Check the Actions tab on GitHub for error logs
- Usually means there's a syntax error in the Rust or TypeScript code
- Fix the error, commit, and push again

### "No successful workflow runs found"
- Wait for the current build to finish
- Make sure the build completed successfully (green checkmark)
- Try specifying the run ID: `.\download-artifacts.ps1 -RunId <number>`

### Extension Not Updating in Marketplace
- Wait 10-15 minutes after publishing
- Clear your browser cache
- Try incognito/private window

## Version Numbering Guide

Use semantic versioning (MAJOR.MINOR.PATCH):

- **Patch** (1.2.6 → 1.2.7): Bug fixes, small tweaks, no new features
- **Minor** (1.2.6 → 1.3.0): New features, backward compatible
- **Major** (1.2.6 → 2.0.0): Breaking changes, significant redesign

## When to Skip Steps

- **If you only changed extension code** (TypeScript/themes/package.json):
  - Skip Step 4 (GitHub Actions) and Step 6 (downloading binaries)
  - Go straight from Step 3 to Step 7

- **If you changed LSP or tree-sitter code** (Rust):
  - You MUST do Steps 4-6 to rebuild the binaries for all platforms

## Notes

- The `.vsix` file is created in the extension folder
- Package size is ~19 MB because it includes binaries for 4 platforms
- Always test locally before publishing (press F5 in VS Code to launch debug instance)
- You can't unpublish a version, only deprecate it, so double-check everything!

## Quick Test Before Publishing

Before running `vsce publish`, you can test the `.vsix` file locally:

```powershell
# Install the packaged extension in VS Code
code --install-extension florpydorp-ic10-language-support-1.2.X.vsix
```

Open an `.ic10` file and verify everything works, then uninstall before publishing.
