# Privacy

Panelith has no accounts, no analytics, no telemetry and no advertising. It does not send your sensor readings, screens or settings anywhere.

## When Panelith goes online

| When | Where | What is sent |
| --- | --- | --- |
| Checking for updates (a minute after start-up and every six hours, unless turned off; or **Check now**) | `github.com/panelithapp/panelith` releases | An ordinary HTTPS request for the update feed. GitHub sees your IP address, as with any download. |
| Installing an update you chose | The same GitHub release | A download of the installer. |
| Making a screen from a YouTube link you entered | YouTube, through the bundled yt-dlp | The requests needed to download that one video. |
| **Update downloader**, when you choose it after a failed YouTube download | `github.com/yt-dlp/yt-dlp` | A download of the newest yt-dlp. |

Links you click (release notes, RivaTuner, PawnIO) open in your browser.

## What Panelith stores on your PC

Everything is in `%APPDATA%\Panelith` and `Videos\Panelith`; see [Configuration files](configuration.md). Log files are kept for 14 days and write your profile folder as `%USERPROFILE%`, so a log you share does not reveal your account name.

To check which programs are running for [profiles](profiles.md), Panelith reads process names on your PC every two seconds. This never leaves your PC.
