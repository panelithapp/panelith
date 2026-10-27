# Releasing

Releases are built and published from the maintainer's PC with one script, [`build/release.ps1`](../build/release.ps1). Installed copies pick up a release as soon as it is published, so treat every release as live.

## One-time setup

1. **.NET 10 SDK**, **Inno Setup 6**, and the **GitHub CLI** (`gh`), signed in with `gh auth login`. Instead of `gh auth login`, the script can use `GH_TOKEN` or the github.com sign-in Git Credential Manager already has for pushing.
2. **An Authenticode code-signing certificate.** Panelith currently uses a self-signed certificate, `CN=Bharath Vadlamannati, O=Panelith`, in the maintainer's `Cert:\CurrentUser\My`. A certificate from a public authority (or Azure Artifact Signing) removes the SmartScreen warning; set the variable to its thumbprint instead and nothing else changes.

   ```powershell
   $env:PANELITH_SIGN_THUMBPRINT = '<thumbprint>'
   ```

   A `.pfx` works too: set `PANELITH_SIGN_PFX` and `PANELITH_SIGN_PASSWORD`. See [`build/sign-file.ps1`](../build/sign-file.ps1).
3. **The Ed25519 update key**, in `%LOCALAPPDATA%\Panelith Release\update-keys` (or the folder in `PANELITH_UPDATE_KEYS`): `NetSparkle_Ed25519.priv` and `NetSparkle_Ed25519.pub`. The public key is compiled into the app in [`UpdateService.cs`](../src/Panelith.App/Services/UpdateService.cs).

### Keep the keys safe

- **Back up the update key** somewhere offline. If it is lost, installed copies can never verify another update, and every user has to reinstall by hand.
- **Never commit either key.** `.gitignore` excludes `*.priv`, `*.pfx` and `NetSparkle_Ed25519.*`.
- If the update key leaks, generate a new pair, ship a release signed with the *old* key that contains the *new* public key, then retire the old key.
- Back up the code-signing certificate with its private key: `certmgr.msc` > Personal > Certificates > the Panelith certificate > All Tasks > Export, with a strong password. A new certificate also works, but the thumbprint in the docs and release notes changes.

## Making a release

1. Bump `VersionPrefix` in [`Directory.Build.props`](../Directory.Build.props).
2. Regenerate the dependency lock files, because they record the version of each
   project too:

   ```powershell
   dotnet restore Panelith.slnx --force-evaluate
   ```

3. Add a `## [x.y.z] - YYYY-MM-DD` section to [`CHANGELOG.md`](../CHANGELOG.md). It becomes the release notes.
4. Commit and push to `main`, including the regenerated `packages.lock.json` files.
5. Run:

   ```powershell
   $env:PANELITH_SIGN_THUMBPRINT = '<thumbprint>'
   ./build/release.ps1
   ```

The script stops at the first problem. It checks that `main` is clean, pushed and the tag is new, runs every test, and builds and signs the app and installer. It then writes and verifies the signed update feed, tags the commit, and publishes the GitHub release with:

| Asset | Purpose |
| --- | --- |
| `Panelith-x.y.z-setup.exe` | The signed installer |
| `appcast.xml`, `appcast.xml.signature` | The update feed installed copies read, and its Ed25519 signature |
| `SHA256SUMS.txt` | Hashes of every asset |
| `Panelith-code-signing.cer` | The public signing certificate, for people who want to check or trust it |

Afterwards, run `./build/publish-wiki.ps1` so the [wiki](https://github.com/panelithapp/panelith/wiki) matches `docs/`. The wiki is generated, so edit `docs/` rather than the wiki.

Use `-Draft` to publish a draft first. Installed copies read `releases/latest`, which ignores drafts and pre-releases, so a draft is safe to inspect; publishing it makes it live.

## How installed copies find a release

The app reads `https://github.com/panelithapp/panelith/releases/latest/download/appcast.xml`, verifies `appcast.xml.signature` with the built-in public key, and offers the version in it if it is newer. The installer download is verified against the Ed25519 signature in the feed before it runs. It is started with `/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1`, and `/RELAUNCH=1` makes the installer start Panelith again afterwards, through the elevated start-up task when there is one.
