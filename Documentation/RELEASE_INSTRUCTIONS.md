# 🚀 Release Instructions for IC10 Extension

Quick guide for releasing new versions of the FlorpyDorp IC10 Language Support extension.

---

## 📋 Before You Release

Make sure you have:
- [ ] Tested the new features locally
- [ ] Updated `CHANGELOG.md` with the new version and changes
- [ ] Updated `README.md` if adding new features/commands
- [ ] Bumped version in `package.json`
- [ ] All changes committed to `main` branch

---

## 🎯 Quick Release (Automated)

The easiest way! Just create and push a version tag:

```bash
# 1. Make sure you're on main and up to date
git checkout main
git pull

# 2. Create a version tag (e.g., v1.2.11)
git tag v1.2.11

# 3. Push the tag to GitHub
git push origin v1.2.11
```

**That's it!** The GitHub Actions workflow will automatically:
- ✅ Build LSP binaries for all platforms (Windows, Linux, macOS Intel, macOS ARM)
- ✅ Package the extension with all binaries into a `.vsix` file
- ✅ Create a GitHub Release with the `.vsix` attached
- ✅ Publish to VS Code Marketplace (if token is configured)

**Monitor progress:**
- Workflow: https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/actions
- Releases: https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/releases

**Time:** ~10-15 minutes for complete build and release

---

## 🔧 Manual Release (If Needed)

If you prefer to do it manually or the automation fails:

### Step 1: Download LSP Binaries
1. Go to https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/actions
2. Find the latest "Build and Release Extension" or "Build IC10 LSP Cross-Platform" run
3. Scroll to **Artifacts** section
4. Download all 4 binary artifacts:
   - `lsp-x86_64-pc-windows-msvc`
   - `lsp-x86_64-unknown-linux-gnu`
   - `lsp-x86_64-apple-darwin`
   - `lsp-aarch64-apple-darwin`

### Step 2: Extract and Copy Binaries
Extract each zip and copy to the extension's `bin` folder with these names:
```
Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/bin/
├── ic10lsp-win32.exe       (from Windows artifact)
├── ic10lsp-linux           (from Linux artifact)
├── ic10lsp-darwin          (from macOS Intel artifact)
└── ic10lsp-darwin-arm64    (from macOS ARM artifact)
```

### Step 3: Set Execute Permissions (Linux/macOS)
```bash
cd "Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support/bin"
chmod +x ic10lsp-linux
chmod +x ic10lsp-darwin
chmod +x ic10lsp-darwin-arm64
```

### Step 4: Build and Package Extension
```bash
cd "Stationeers-ic10-main/FlorpyDorp IC10/FlorpyDorp Language Support"

# Install dependencies (if needed)
npm ci

# Build the extension
npm run vscode:prepublish

# Package into .vsix
npx vsce package
```

This creates `florpydorp-ic10-language-support-X.X.X.vsix`

### Step 5: Test Locally
```bash
# Install the .vsix file in VS Code
code --install-extension florpydorp-ic10-language-support-X.X.X.vsix
```

### Step 6: Publish to Marketplace
```bash
# Publish (requires Personal Access Token)
npx vsce publish
```

Or manually upload at: https://marketplace.visualstudio.com/manage/publishers/FlorpyDorp

---

## 🔑 VS Code Marketplace Setup (One-Time)

To enable automatic publishing, you need a Personal Access Token (PAT):

### 1. Create a PAT
- Go to https://dev.azure.com/
- Click your profile → **Security** → **Personal Access Tokens**
- Click **+ New Token**
- **Name:** `VS Code Marketplace`
- **Organization:** All accessible organizations
- **Expiration:** 90+ days (or custom)
- **Scopes:** Check **Marketplace** → **Manage**
- Click **Create**
- **COPY THE TOKEN IMMEDIATELY** (you won't see it again!)

### 2. Add Token to GitHub Secrets
- Go to https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/settings/secrets/actions
- Click **New repository secret**
- **Name:** `VSCE_PAT`
- **Value:** Paste your PAT
- Click **Add secret**

### 3. Done!
The next time you push a version tag, it will automatically publish to the marketplace.

---

## 📦 What Gets Released

Each release includes:
- **Extension Package (`.vsix`)**: Complete extension ready to install
- **All Platform Binaries**: Windows, Linux, macOS (Intel + ARM)
- **Release Notes**: Auto-generated from commits + CHANGELOG
- **Marketplace Listing**: Automatically updated (if configured)

---

## 🐛 Troubleshooting

### "Workflow failed" - Check the logs
1. Go to https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/actions
2. Click the failed run
3. Click the failing job to see error details
4. Common fixes:
   - Re-run the workflow (often fixes cache issues)
   - Check that version in `package.json` matches the tag
   - Ensure all files are committed

### "VSCE_PAT not found" warning
This is normal if you haven't set up marketplace publishing yet. The extension will still be packaged and released on GitHub, just not published to the marketplace.

### Binary missing or wrong platform
Make sure the `build-lsp.yml` workflow ran successfully for all platforms. You can manually trigger it from the Actions tab.

---

## 📚 Version Numbering

Follow semantic versioning: `MAJOR.MINOR.PATCH`

- **MAJOR** (1.x.x): Breaking changes
- **MINOR** (x.2.x): New features, backwards compatible
- **PATCH** (x.x.10): Bug fixes, backwards compatible

Examples:
- `v1.2.10` → `v1.2.11` (bug fix)
- `v1.2.11` → `v1.3.0` (new feature)
- `v1.3.0` → `v2.0.0` (breaking change)

---

## ✅ Release Checklist

Use this checklist for each release:

```
[ ] Update version in package.json
[ ] Update CHANGELOG.md with changes
[ ] Update README.md if needed
[ ] Commit all changes
[ ] Test locally (press F5 in VS Code)
[ ] Push to main branch
[ ] Create version tag: git tag vX.X.X
[ ] Push tag: git push origin vX.X.X
[ ] Wait for workflow to complete (~15 min)
[ ] Verify GitHub Release created
[ ] Verify marketplace updated (if configured)
[ ] Test install from marketplace
[ ] Announce on Discord/community (optional)
```

---

## 🎉 Quick Commands Reference

```bash
# Full release in 3 commands
git tag v1.2.11
git push origin v1.2.11
# Wait for automation...

# Check workflow status
gh run list --workflow=release.yml

# Download latest release
gh release download --pattern '*.vsix'

# Delete a tag (if you need to redo)
git tag -d v1.2.11
git push origin :refs/tags/v1.2.11
```

---

**Need help?** Check the workflow logs at:
https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/actions

**Questions?** Open an issue at:
https://github.com/FlorpyDorpinator/FlorpyDorp-IC10-Language-Support/issues
