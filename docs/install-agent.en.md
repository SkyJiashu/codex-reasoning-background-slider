# Automatic installation prompt (English)

[中文](install-agent.zh-CN.md) · [Project README](../README.md)

Copy the entire prompt below to an agent running on your Windows computer. It needs network access and permission to download files and run local programs. Initial encoding may take some time. Verification uses the independent preview and does not require closing your current Codex session.

```text
Automatically install Codex Reasoning Background Slider on this Windows computer.

Trusted project: https://github.com/SkyJiashu/codex-reasoning-background-slider
Installation guide: https://github.com/SkyJiashu/codex-reasoning-background-slider/blob/main/docs/install-agent.en.md

Within your existing permissions and safety rules, I authorize downloading this project and its required encoding dependency, installing them under my current user account, generating the local animation cache, creating this project's own desktop shortcut, and running the offline preview. Complete the work rather than merely giving me commands. Do not repeatedly request confirmation for routine reversible steps. Read the project licenses first: the project code and artwork are licensed for noncommercial use.

Follow these steps:

1. Confirm Windows x64, .NET Framework 4.8 and the system media components are available. The official Codex desktop client should already be installed. Do not read, print or upload API keys, login files, cookies, authentication configuration or a dump of environment variables. No GitHub token is required from me.

2. Read the public API https://api.github.com/repos/SkyJiashu/codex-reasoning-background-slider/releases/latest . Select exactly one runtime asset whose name ends in windows-x64.zip; do not install source.zip. Download its browser_download_url. Verify its SHA-256 against the asset digest from the API, or SHA256SUMS.txt from the same Release if the digest field is absent. If verification fails, do not execute that download; identify the failed check.

3. Obtain LocalApplicationData through the operating-system directory API. Use a versioned directory under Programs/CodexReasoningBackgroundSlider. Extract into a fresh staging directory first, ensuring every normalized ZIP entry stays inside it; reject absolute paths, traversal and link entries. Locate the single directory containing CrystalDragon.exe, RenderFilm.exe, animation-source.txt, skin.js, skin.css, the five assets PNGs and scripts/install-shortcut.ps1, then place it in the permanent version directory. These compatibility filenames are intentional. Do not overwrite an active old version or delete existing caches or user-customized files.

4. Prepare an FFmpeg build with libx264. First check for ffmpeg.exe next to the launcher, then use executable discovery to find an existing installation. Run -hide_banner -encoders and confirm libx264. Do not dump environment values or permanently modify the global PATH.
   If it is missing, prefer a private portable copy: download the release essentials ZIP and its matching .sha256 from https://www.gyan.dev/ffmpeg/builds/ . Verify the published checksum before extraction. This provider is linked by FFmpeg's Windows downloads page. Keep its license files and place the private ffmpeg.exe next to this project's launcher without replacing another application's FFmpeg.
   Alternatively, use the exact WinGet ID Gyan.FFmpeg.Essentials with --source winget --scope user --silent --accept-package-agreements --accept-source-agreements. Do not silently switch to machine scope or elevation. The current process may not discover a newly installed command immediately; verify the actual selected encoder. Ordinary runtime installation does not require Node, Python or ffprobe.

5. Run the installed CrystalDragon.exe with --probe and wait for completion. Read only the fixed ready, version, coldStartAllowed and error_class fields from the newly generated qa/identity.json. Confirm the official client can be identified. coldStartAllowed=false means Codex is currently running, not that installation failed. If the official client or a system prerequisite is missing, report it rather than substituting an untrusted Codex package.

6. Inspect and run this project's scripts/install-shortcut.ps1 to create the independent Codex Reasoning Slider desktop entry. It should create or update only shortcuts marked as belonging to this project. Preserve the official Codex files, shortcuts, registration and taskbar pins. Do not scan drives to rewrite other entrypoints.

7. Record the preview start time, run the installed CrystalDragon.exe --preview, and wait for normal completion. This prepares the local cache and plays this project's animation without starting Codex. Initial cache-generation progress is expected; let it finish without pressing Esc or cancelling.
   Require a fresh qa/handoff.json written by this invocation. previewOnly, filmOpened, filmCompleted and transitionComplete must be true; filmFailed, skipped and timedOut must be false. Exit code zero or an old report alone is insufficient. If a check fails, inspect the project's fixed error categories, repair an identified missing dependency and validate again. Do not inspect native-client authentication files or chat logs.

8. During installation, do not use the default launch, --cold-test, --restore or --probe-create as substitutes for offline verification. Do not force-stop Codex or another application. In particular, never close the application hosting this conversation. After installation and cache preview finish, I can save my work, exit Codex normally and use the new shortcut.

Finally report the installed version, installation directory, shortcut, FFmpeg readiness, cache/preview verification result and the entrypoint to use next time. If an actual system restriction requires my involvement, identify the remaining step precisely; do not label unverified work as successful.
```

Dependency references: [FFmpeg downloads](https://ffmpeg.org/download.html), [Gyan Windows builds](https://www.gyan.dev/ffmpeg/builds/). This prompt authorizes current-user installation and an independent preview, not stopping existing work sessions or modifying the official client.
