# Releasing and updating Plain Viewer

## What the installer is

`scripts/package.ps1` builds one file, `artifacts/installer/PlainViewer-Setup-<version>-x64.exe`, plus a `.sha256` file beside it.

- Installs for the current user only, without administrator rights, into `%LOCALAPPDATA%\Programs\Plain Viewer`. Windows 11, x64.
- Contains everything needed offline: the app and its worker with the .NET 10 runtime included, PDF.js, a trimmed LibreOffice 26.2.6 for Word and PowerPoint files, and Microsoft's offline WebView2 Runtime installer (DECISIONS.md D9). Windows 11 normally includes the WebView2 Runtime, but a clean Windows 11 may not; setup then offers the task "Install the Microsoft Edge WebView2 Runtime" (on by default) and runs Microsoft's installer. Without the runtime the app opens only text, CSV and Markdown files and explains why for the others. About 400 MB.
- Shows a terms page (`installer/terms.txt`) that users accept; installing the WebView2 Runtime makes it subject to Microsoft's own licence terms.
- Adds a Start menu entry, an uninstaller in Settings > Apps, and (optional, on by default) "Open with" entries for .pdf, .docx, .xlsx, .pptx, .csv, .txt, .md and .markdown. It never changes the user's default apps; Windows lets the user pick Plain Viewer as the default in Settings > Default apps.
- Runs `PlainViewer.exe --prepare-converter` at the end, which builds LibreOffice's private profile so the first Word or PowerPoint file opens in seconds.
- Uninstalling leaves the WebView2 Runtime (a shared Windows component) and removes the app, its private data in `%LOCALAPPDATA%\PlainViewer` (the document view's WebView2 data) and `%USERPROFILE%\AppData\LocalLow\PlainViewer` (converter profile, temporary work folders), and the firewall rules if they were added. It never touches the user's documents.

## One-time setup on a build PC

Everything lives under `.tools` (not in Git):

1. .NET SDK in `.tools\dotnet` (version per `global.json`).
2. Offline NuGet feed `.tools\feed`: the packages the projects use, plus the runtime packs `microsoft.netcore.app.runtime.win-x64` and `microsoft.windowsdesktop.app.runtime.win-x64` in the version the SDK bundles (10.0.12 for SDK 10.0.401). Download them from `https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.<version>.nupkg` and check them with `dotnet nuget verify --all <file>` (must report Microsoft Corporation and nuget.org signatures).
3. LibreOffice: `.\scripts\fetch-libreoffice.ps1` downloads the pinned version, checks its SHA-256 against download.documentfoundation.org, unpacks it and trims it (`trim-libreoffice.ps1`).
4. Inno Setup 7.1.0 in `.tools\innosetup-7.1.0`: download `innosetup-7.1.0-x64.exe` from the official GitHub release linked on https://jrsoftware.org/isdl.php, compare its SHA-256 with the digest on the release page, check its signature, then install it for the current user only:
   `innosetup-7.1.0-x64.exe /VERYSILENT /CURRENTUSER /NOICONS /MERGETASKS="!desktopicon,!fileassoc" /DIR="<repo>\.tools\innosetup-7.1.0"`
5. Node.js (for the test corpus generator and the security smoke test's request listener).
6. Microsoft's WebView2 "Evergreen Standalone Installer" (x64) in `.tools\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe`, from https://developer.microsoft.com/microsoft-edge/webview2 (the download requires accepting Microsoft's WebView2 Runtime licence terms; Zain accepted them and chose to bundle the runtime on 28 Sep 2026). `package.ps1` refuses it unless `Get-AuthenticodeSignature` reports a valid signature by Microsoft Corporation.

## Making a release

1. Change `<Version>` in `Directory.Build.props` (for example 0.1.0 → 0.1.1 for fixes and security updates, 0.2.0 for new features). The version appears in About, in the installer's name and in Settings > Apps.
2. Update bundled components if needed (next section).
3. Run `.\scripts\package.ps1`. It runs the build, core tests, Markdown tests, Office safety tests, smoke test and security smoke test, then publishes and builds the installer. It stops at the first failure.
4. Test the installer on a clean PC before publishing it: `.\scripts\sandbox-test.ps1` (needs the Windows Sandbox feature and about 8 GB free on C:, because the sandbox's disk lives there and a run uses about 7 GB until the sandbox closes; takes about 10 minutes). It installs the newest installer in a throwaway Windows 11 with networking off, first without the WebView2 task (checking the app's message), then again as an upgrade with the bundled WebView2 Runtime; it checks the converter profile, firewall rules and "Open with", opens and refuses the test files, checks keyboard use and high contrast, uninstalls and checks that nothing is left. Results are in `artifacts\sandbox-test\<run>\results`. A quicker check on the build PC itself:

       $setup = '.\artifacts\installer\PlainViewer-Setup-<version>-x64.exe'
       $dir = "$env:LOCALAPPDATA\PlainViewerInstallTest"
       Start-Process $setup -Wait -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NOICONS','/MERGETASKS="!openwith,!desktopicon"',"/DIR=`"$dir`""
       .\scripts\smoke-test.ps1 -App "$dir\PlainViewer.exe"
       .\scripts\security-smoke.ps1 -App "$dir\PlainViewer.exe"
       Start-Process "$dir\unins000.exe" -Wait -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES'

5. Sign the installer once a code-signing certificate exists (open decision below).
6. Publish the installer, its `.sha256` file and short release notes (add a section to `docs/RELEASE-NOTES.md`). Keep earlier installers. For the private beta, testers also get `docs/BETA.md` (guide) and `docs/BETA-FEEDBACK.md` (problem report form).

Never change `AppId` in `installer/PlainViewer.iss`; it is how Windows recognises the same app for upgrades.

## How users update

Users download and run the newer installer. It installs over the old version: it closes Plain Viewer if it is running, replaces the program files (the previous LibreOffice and page files are removed first so nothing stale remains) and keeps the "Open with" choices. If LibreOffice changed, the converter profile is rebuilt automatically. Nothing needs to be uninstalled first.

The app never checks for updates by itself, because it must not go online. If update notices are wanted later, the most that fits the specification is an About-box link that opens the download page in the browser after asking.

## When to release

Plain Viewer opens files from strangers, so ship an update whenever a bundled component fixes a security problem:

| Component | Where to watch | How to update |
|---|---|---|
| .NET runtime (monthly, on Patch Tuesday) | https://dotnet.microsoft.com/download/dotnet/10.0 | Install the new SDK into `.tools\dotnet`, put the matching runtime packs in `.tools\feed` |
| LibreOffice | https://www.libreoffice.org/about-us/security/advisories/ | `fetch-libreoffice.ps1 -Version <new>`, then `package.ps1 -LibreOfficeVersion <new>`; update THIRD-PARTY-NOTICES.md (version, checksum, source link) |
| PDF.js | https://github.com/mozilla/pdf.js/releases | Replace `src/PlainViewer.App/Assets/pdf/pdfjs`, keep the Liberation Sans 2.x fonts, update notices |
| Markdig, ExcelNumberFormat, WebView2 SDK | NuGet | Update the version in the project file and `.tools\feed`, update notices |
| WebView2 Runtime | Installed copies update themselves through Microsoft's update service | For each release, download the offline installer again into `.tools\webview2` (one-time setup, step 6) so new installs start from a current runtime |

## Signing and Windows warnings

Current installers are unsigned. What users see:

- **SmartScreen:** "Windows protected your PC" when the installer starts. They continue with "More info" > "Run anyway". The warning fades for a signed file as it builds reputation; for unsigned files it stays.
- **Smart App Control** (Windows 11, on some clean installs): when it is on, it can block unsigned programs outright, with no "Run anyway". `PlainViewer.exe` and `PlainViewer.Worker.exe` are unsigned; the .NET runtime files are signed by Microsoft and LibreOffice's by its publisher.
- Some antivirus products scan unsigned installers more strictly.

Steps once a certificate exists: sign `PlainViewer.exe` and `PlainViewer.Worker.exe` in `artifacts\publish\win-x64` after publishing, then let Inno Setup sign the installer and uninstaller by adding a `SignTool` directive and `SignedUninstaller=yes` to `installer\PlainViewer.iss`. Use SHA-256 file digests and an RFC 3161 timestamp (`signtool sign /fd SHA256 /tr <timestamp URL> /td SHA256 ...`) so signatures stay valid after the certificate expires. A self-signed certificate may be used for local testing only, never for distribution (specification).

## Open decisions for Zain

- **Code signing.** The specification says not to buy a certificate now; production signing is a later step (see "Signing" below). A certificate is a paid, yearly item; check current prices and eligibility when the time comes (for example an OV certificate from a certificate authority, or Microsoft's Azure-based signing service, whose eligibility rules for individuals need checking).
- **Inno Setup commercial licence.** Inno Setup's licence allows commercial use for free, but since 2025 its authors ask commercial users with annual revenue above USD 5,000 to buy a licence (Single User, Team 2–5 users, Enterprise; one-time payment with two years of updates; price shown at checkout). They state it is not strictly required.
- **Where to publish.** GitHub Releases (this repository is private, so users could not download from it; a public repository or another host is needed), your own website, winget (needs a public download link) or the Microsoft Store (not tested with LibreOffice inside).
- **Terms page wording** (`installer/terms.txt`): a draft; review it before the beta. Plain Viewer's own licence is chosen at the public release.
- **ARM64 installer:** needs the ARM64 LibreOffice build and ARM64 runtime packs (downloads).
