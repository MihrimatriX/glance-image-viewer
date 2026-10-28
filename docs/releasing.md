# Releasing

`.github/workflows/release.yml` does the building. A release is: bump, tag, then the store steps below.

## Every release

1. Set `<Version>` in `Directory.Build.props` (x.y.z) and merge to `main`.
2. Smoke-test the `release` workflow artifacts from that commit (Actions › release › Run workflow) on a clean
   Windows 10 1809 and Windows 11 machine: setup and portable zip, HEIC/AVIF/JXL/CR2 open, Explorer thumbnails,
   *Default apps* entry, uninstall leaves no file associations.
3. `git tag vX.Y.Z && git push origin vX.Y.Z`. CI then:
   - creates the GitHub Release with `Glance-X.Y.Z-{x64,arm64}-setup.exe` and `-portable.zip`;
   - uploads `winget-manifests` (artifact) pointing at those release assets;
   - uploads `store-msixbundle` (artifact): unsigned x64 + ARM64 bundle for Partner Center;
   - submits to winget automatically when `WINGET_AUTOSUBMIT` is enabled (see below).
4. Partner Center › Glance › new submission › Packages: upload `Glance-X.Y.Z.msixbundle`.

## One-time setup

### Microsoft Store (also removes the SmartScreen warning for Store installs)

Store packages are signed by Microsoft, so no certificate is needed for this channel.

1. Register as an individual developer at <https://storedeveloper.microsoft.com> (free for individuals).
2. Reserve the name **Glance** (or an alternative if taken) and open *Product management › Product identity*.
3. Add three repository **variables** (Settings › Secrets and variables › Actions › Variables):
   - `STORE_IDENTITY_NAME` = *Package/Identity/Name*
   - `STORE_PUBLISHER` = *Package/Identity/Publisher* (`CN=…`)
   - `STORE_PUBLISHER_DISPLAY_NAME` = *Package/Properties/PublisherDisplayName*
4. First submission needs a privacy policy URL (Glance collects nothing; the README *Privacy* section can be
   linked), screenshots, an age rating questionnaire, and the `runFullTrust` justification:
   "Desktop image viewer; full trust is needed for file-type associations and the Explorer thumbnail handler."

### winget

The first version goes in by hand; later versions can be automatic.

1. Download the `winget-manifests` artifact from the tagged run.
2. Validate and submit from Windows: `winget validate <dir>` then
   `wingetcreate submit --token <PAT> <dir>` (a classic PAT with `public_repo`), which opens a PR on
   `microsoft/winget-pkgs`.
3. After that PR merges, add secret `WINGET_TOKEN` (same PAT) and variable `WINGET_AUTOSUBMIT=true`; every later
   tag then runs `wingetcreate update --submit`.

### Code signing the installer (optional)

The GitHub/winget installer stays unsigned until a certificate exists, so SmartScreen warns until it builds
reputation. Free for open source: SignPath Foundation (<https://signpath.org>, apply with the repository).
Paid: Azure Artifact Signing. Either plugs into the `build` job before the zip and installer steps.
