# Security policy

## Supported versions

Only the latest release receives fixes. Installed copies update themselves unless automatic checks are turned off in **Settings > Updates**.

## Reporting a vulnerability

Please do not open a public issue. Report it privately through [GitHub's private vulnerability reporting](https://github.com/panelithapp/panelith/security/advisories/new), or by email to bharry29@gmail.com with "Panelith security" in the subject.

Include the Panelith version, what an attacker could do, and steps to reproduce. You can expect a reply within a week. Once a fix is released, you will be credited in the release notes unless you prefer not to be.

## How releases are protected

- The installer and app are Authenticode-signed. The signing certificate is published with each release as `Panelith-code-signing.cer`, and `SHA256SUMS.txt` lists the hash of every file.
- The update feed and every update are signed with an Ed25519 key that never leaves the maintainer's machine. The app refuses an update whose signature does not match the public key built into it, even if the download site were compromised. The signature is checked again on the downloaded file immediately before Panelith asks Windows for permission to run it, and the file is held open against changes until it starts, so the bytes that are checked are the bytes that run.
- Release builds stop if any NuGet dependency, direct or transitive, has a known vulnerability, and the bundled tools are checked against pinned SHA-256 hashes.
- Every project has a committed `packages.lock.json` recording the exact version and hash of each dependency, so a build cannot quietly resolve to something other than what was reviewed. Workflow actions are pinned by commit rather than by tag.

## Scope

Panelith runs with administrator rights when you allow it, so it can read hardware sensors. Problems that let another program or user gain those rights through Panelith are in scope, as are problems in the update process, the installer, and how Panelith handles screen files and videos from other people.

## Known trade-offs

These are deliberate, so there is no need to report them; a way to exploit one of them is still worth reporting.

- **yt-dlp updates itself.** Downloading a video uses `yt-dlp`, which is fetched at a pinned SHA-256 hash but then keeps itself up to date, and is not Authenticode-signed, so later versions cannot be verified the same way. Panelith never runs it with administrator rights: the download button is unavailable when Panelith is elevated, which is the guard that matters. If you would rather not rely on that, do not use the video download feature.
- **Screen files and videos are decoded in the same process as the app.** A flaw in an image or video decoder would therefore run with Panelith's own rights. Only add screens and videos from people you trust. Themes are confined to their own folder and are limited in size, nesting, widget count and image dimensions, but that limits the damage rather than preventing it.
- **The signing certificate is self-signed.** Windows will say the publisher is unknown until you check the thumbprint against the certificate published with the release.
