# Updates

Installed copies of Panelith keep themselves up to date, using [NetSparkle](https://github.com/NetSparkleUpdater/NetSparkle), the Windows counterpart of the Sparkle updater used by many Mac apps.

## What happens

1. A minute after start-up, and every six hours after that, Panelith downloads a small file listing the newest version (the *appcast*) from the latest GitHub release.
2. If a newer version exists, a notice appears at the top of the window, and **Settings > Updates** offers **Install … and restart**.
3. When you choose it, Panelith downloads the installer and checks its signature, then Windows asks for permission, as for any installation in Program Files. Approve that and Panelith closes, installs the update without the setup wizard, and starts again. Decline it and Panelith keeps running on the old version; choosing **Install** again asks once more.

Nothing is downloaded or installed without you choosing to. Your screens, profiles and settings are kept.

To check by hand, choose **Check now**. To stop background checks, turn off the switch in **Settings > Updates**.

## How updates are kept safe

Every release is signed twice:

- The **update feed and the installer** are signed with an Ed25519 key that only the publisher holds. Panelith contains the matching public key and refuses a feed or installer whose signature does not match. This holds even if the download server or the connection were tampered with.
- The **installer and Panelith's program files** are Authenticode-signed by **Bharath Vadlamannati**, with a timestamp. See [Installing](installing.md#windows-says-the-publisher-is-unknown) about the certificate.

Updates are fetched over HTTPS from `github.com/panelithapp/panelith`.

## Which copies update

Only copies installed with the Panelith installer update themselves. A copy built from source or run from another folder says so in **Settings > Updates** and never replaces itself.

Panelith 0.1.0 is the first version with in-app updates. Copies installed before it, including RigGlass, need the 0.1.0 installer once; after that they update by themselves.

## If an update fails

Panelith keeps running the version you have. The reason is shown in **Settings > Updates** and in the log. You can always download the newest installer from the [releases page](https://github.com/panelithapp/panelith/releases/latest) and run it over your installation.
