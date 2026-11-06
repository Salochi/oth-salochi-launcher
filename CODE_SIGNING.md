# Code signing policy

> **Status:** applying to the SignPath Foundation. Until the application is approved, releases of the launcher are
> unsigned, and Windows SmartScreen warns the first time you open it (see the README).

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

## What gets signed

Only `SalochiLauncher.exe`, the program you download and run. It's built by GitHub Actions
([release workflow](.github/workflows/release.yml)) from the source in this repository at a release tag, and signed
only after a team member approves the signing request. Builds from anywhere else are never signed.

The mod DLLs aren't signed. Windows never runs them directly: BepInEx loads them inside the game. Instead, the
launcher checks every download against a mod list signed with the Salochi release key
([docs/manifest.md](docs/manifest.md#signatures)).

## Team roles

| Role | Who |
|---|---|
| Committers and reviewers | [Salochi](https://github.com/Salochi) |
| Approvers | [Salochi](https://github.com/Salochi) |

Changes from anyone else are reviewed by a committer before they're merged. Every team member uses multi-factor
authentication on GitHub and on SignPath.

## Privacy

The launcher sends no personal information anywhere, and has no telemetry or analytics.

The only connections it makes are downloads from GitHub (`github.com` and GitHub's download hosts), and only to do
what it's for:
- the signed mod list (`manifest.json`) and launcher version (`launcher.json`) from this repository's latest release,
  each time it opens;
- the mods you pick, the pictures of the cars, BepInEx, and newer launcher versions, from the same releases.

These are ordinary HTTPS requests. GitHub sees your IP address, as it does for any download, and its handling of that
is covered by the [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
The launcher's own files (settings, a log, downloaded mods) stay on your computer, in `%LOCALAPPDATA%\Salochi\Launcher`.
