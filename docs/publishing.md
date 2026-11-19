# Publishing BatTray (GitHub, winget, Microsoft Store)

Maintainer notes. Every release starts on GitHub; winget and the Store point to (or re-use) that installer.

## 1. GitHub release

1. Set the version in `Directory.Build.props` (`<Version>`) and in `installer/BatTray.Installer.wixproj` (`ProductVersion`).
2. Build the installer: `pwsh tools/build-installer.ps1` → `artifacts/BatTray-Setup-<version>.msi`.
3. Write `docs/release-notes/v<version>.md` with the SHA-256 (`Get-FileHash artifacts/BatTray-Setup-<version>.msi`).
4. Create the release with tag `v<version>` and attach **exactly** `BatTray-Setup-<version>.msi`.

The name matters: the in-app updater only installs an asset named `BatTray-Setup-<x.y.z>.msi` that is
served from `github.com/koratsolutions-code/BatTray/releases/download/…`, and only after checking its size and
the SHA-256 that GitHub publishes for every asset. Any other release falls back to opening the release page.

## 2. winget (`winget install KoratSolutions.BatTray`)

winget does not require a code signing certificate, and installing through winget avoids the SmartScreen
warning that browsers show for downloaded files.

1. After the GitHub release is published, generate the manifests from the **same** MSI you uploaded:
   `pwsh tools/new-winget-manifest.ps1 -Msi artifacts/BatTray-Setup-<version>.msi`
2. Check them: `winget validate --manifest artifacts/winget/<version>` and, optionally,
   `winget install --manifest artifacts/winget/<version>` (needs `winget settings --enable LocalManifestFiles`
   as administrator).
3. Submit them to <https://github.com/microsoft/winget-pkgs>: fork it, copy the three files to
   `manifests/k/KoratSolutions/BatTray/<version>/` and open a pull request. Microsoft's bots validate and
   install the package; a moderator merges it (usually within a few days).
   `wingetcreate submit artifacts/winget/<version>` does the fork and pull request for you.
4. For later versions: `wingetcreate update KoratSolutions.BatTray --version <version> --urls <msi url> --submit`.

## 3. Microsoft Store

- The developer account is free for individuals (<https://storedeveloper.microsoft.com>).
- **MSI/EXE submission** re-uses the GitHub MSI (the Store only links to it), but the installer must be
  Authenticode-signed with a certificate from a CA in the Microsoft Trusted Root Program; self-signed
  certificates are rejected. It must also install silently (BatTray's MSI does: `msiexec /i … /quiet`).
- **MSIX submission** needs no certificate: the Store signs the package. BatTray would need an MSIX package
  (start with Windows through a `startupTask` extension instead of the `Run` registry key). Not done yet.
- Options for a certificate: an OV code signing certificate from a CA (paid, yearly), Azure Artifact Signing
  (only for individuals in some countries), or SignPath Foundation, which signs qualifying open-source projects
  for free. A signed installer also removes most SmartScreen warnings once it builds reputation.
