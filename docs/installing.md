# Installing Panelith

## Requirements

- Windows 10 version 1809 or later, or Windows 11, 64-bit.
- A supported USB panel (see [Device support](device-support.md)). Without one, Panelith still runs and shows the screen in its window, which is handy for designing.
- About 150 MB of disk space.

Windows "N" editions lack the Media Foundation video components. The installer adds the free Media Feature Pack for you when it is missing; video screens need it.

## Download and install

1. Open the [latest release](https://github.com/panelithapp/panelith/releases/latest) and download `Panelith-<version>-setup.exe`.
2. Quit Thermalright TRCC, or whatever software came with your panel, including from the notification area. Only one program can use the panel at a time.
3. Run the installer and accept the Windows permission prompt. Panelith installs to `C:\Program Files\Panelith`.
4. On the **Select additional tasks** page you can:
   - add a desktop shortcut;
   - install the **PawnIO** driver, which most CPUs need before Panelith can read CPU temperature, clock and power. It is signed, comes from its official release, and is only installed if you tick it. Some anti-cheat systems are strict about hardware-access drivers, so check your games' rules first.
5. Finish, and Panelith starts.

## Windows says the publisher is unknown

Panelith's installer and program files are code-signed by **Bharath Vadlamannati**, and every signature carries a timestamp. Panelith currently uses its own (self-signed) certificate rather than one bought from a certificate authority, so Windows SmartScreen does not yet recognise it and may show **"Windows protected your PC"**.

To continue, choose **More info**, check the app name, then choose **Run anyway**.

Before you do, you can confirm the download is genuine:

1. Right-click the installer, choose **Properties**, then the **Digital Signatures** tab. The signer should be **Bharath Vadlamannati**. Select it and choose **Details** to see the certificate thumbprint, and compare it with the one in the release notes.
2. Or compare its SHA-256 hash with `SHA256SUMS.txt` from the same release. In PowerShell:

   ```powershell
   Get-FileHash .\Panelith-0.1.0-setup.exe -Algorithm SHA256
   ```

### Optional: trust the Panelith certificate on your PC

Each release includes `Panelith-code-signing.cer`, the public half of the signing certificate. If you add it to **Trusted Publishers** and **Trusted Root Certification Authorities**, Windows shows the signature as valid on that PC. Only do this if you are comfortable trusting the publisher: a trusted root can vouch for any program signed with that certificate.

Run in an elevated PowerShell, in the folder where you saved the certificate:

```powershell
Import-Certificate -FilePath .\Panelith-code-signing.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\Panelith-code-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

To undo it, delete the certificate named **Bharath Vadlamannati** from both stores in `certlm.msc`.

You do not need to do this to use Panelith, and in-app updates do not depend on it: they are checked against a separate update signature (see [Updates](updates.md)).

## Updating

Panelith checks for new versions in the background and offers them in the app. See [Updates](updates.md). You can also run a newer installer over an existing installation at any time; your screens and settings are kept.

## Upgrading from RigGlass

Panelith used to be called RigGlass. The Panelith installer upgrades a RigGlass installation in place: it removes the old program folder and shortcuts, moves start with Windows across, and on first launch Panelith moves `%APPDATA%\RigGlass` to `%APPDATA%\Panelith`.

## Uninstalling

Use **Settings > Apps > Installed apps > Panelith > Uninstall**. This removes the program, the start-up entry and the downloader Panelith keeps for YouTube. Your screens, settings and downloaded videos stay, in case you reinstall; delete these folders yourself to remove them:

- `%APPDATA%\Panelith` (settings, profiles, your screens and logs)
- `%USERPROFILE%\Videos\Panelith` (downloaded videos and the MP4 copy of each video screen)

PawnIO, if you installed it, is a separate program and has its own uninstaller.
