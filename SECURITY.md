# Security policy

## Reporting a vulnerability

Report it privately through GitHub: the repository's **Security** tab →
**Report a vulnerability**, or directly at
<https://github.com/fredrikhu/hultui/security/advisories/new>.

Please don't open a public issue, discussion or pull request for it. A public report
shows everyone the problem before there is a fix.

A useful report says:

- **what is affected**: the package or app, and the version or commit;
- **how to reproduce it**: steps, a file, or a small proof of concept;
- **what an attacker gains**, as far as you can tell;
- a fix, if you have one in mind.

## What happens next

HultUI has one maintainer, so this is best effort, not a guaranteed response time.

1. The report becomes a private draft advisory, visible only to you and the maintainer.
2. Once the problem is confirmed, the fix is worked on in a private fork, with you if you
   want to take part.
3. The fix ships: on `main` for the framework, in a new release for an app.
4. The advisory is published, crediting you unless you'd rather not be named, with a CVE
   when the problem warrants one.

If a report turns out not to be a vulnerability, it is closed with the reason, and an
ordinary bug in it can move to a public issue.

## Supported versions

| Component | Gets security fixes |
|---|---|
| The framework (`src/`, the `HultUI.*` packages) | `main`. The packages are not published to a feed yet (`0.1.0-preview`), so fixes land on `main` only. |
| The apps: Junction, Vesper Commander, Meridian, Aperture, Reel | The latest release of each (`<app>-vX.Y.Z`). A fix ships in the next release; older releases are not patched. |
| The native packages (mpv natives, CEF natives, the CEF helper) | The latest release. |
| Samples and spikes | No: they are examples, not products. |

## Scope

In scope, for example:

- secrets the apps keep or send: API keys, access tokens, SSH credentials, values in the
  OS keyring, and masked input leaking through the clipboard, accessibility or an IME;
- untrusted input the code parses: archives (a member path escaping its folder), git
  repositories, HTTP responses and HTML previews, terminal escape sequences, media files,
  and web content in `HultUI.WebView`;
- the release and CI workflows in `.github/workflows/`, and what the installers put on a
  machine;
- HultUI's patches to the vendored `external/XTerm.NET/`.

Out of scope:

- vulnerabilities in upstream projects (SkiaSharp, SDL, libmpv and FFmpeg, CEF and
  Chromium, WebView2, SSH.NET, XTerm.NET itself and others): report those upstream. Do
  tell us if HultUI ships an affected version or uses the library in an unsafe way;
- attacks that need control of the user's account or machine already;
- scanner output with no demonstrated impact.
