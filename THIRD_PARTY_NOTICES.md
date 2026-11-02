# Third-party notices and media

## Project additions

The project's additions are offered under the PolyForm Noncommercial License 1.0.0 in [LICENSE](LICENSE). This is a source-available, noncommercial license. It does not revoke or replace licenses already granted by upstream authors for their respective work.

## Adapted code

Windows window enumeration, responsiveness and handoff helpers in `src/NativeWindow.cs` are adapted from [Dragon Codex Boot](https://github.com/yushua0808-cpu/dragon-codex-boot), commit [`c63a203e5857e85fb3c8a9891eddb570e7a27b54`](https://github.com/yushua0808-cpu/dragon-codex-boot/tree/c63a203e5857e85fb3c8a9891eddb570e7a27b54).

The upstream MIT notice is retained in [licenses/dragon-codex-boot-MIT.txt](licenses/dragon-codex-boot-MIT.txt) and continues to apply to the upstream material. The project license must not be interpreted as withdrawing those upstream permissions.

## Design references

- The data-to-character visual concept and startup sequencing reference [codex-startup-animation](https://github.com/mytjxs/codex-startup-animation) and [PR #1](https://github.com/mytjxs/codex-startup-animation/pull/1), reviewed at head `2a7a7e4fb4b2e8f417eca4027769bff8b9ded832`. The own-window nonrectangular cover is an independent implementation of the preparation technique described there. No code, video, audio or character media from that PR is bundled.
- Effort-driven visual changes reference the concept demonstrated by [dsh-liang-skin](https://github.com/kingOfSoySauce/dsh-liang-skin). No DSH source or character media is bundled.

## Character artwork

No character artwork is included in this public repository. Users supply their own authorized images. The project code license does not grant rights in those images. Development artwork involved AI generation or editing; no claim of ownership in third-party artwork is made.

No third-party startup movie or audio is distributed. Startup movies and their poster frames are generated locally from the input artwork and animation code.

## External dependencies and branding

FFmpeg is an external encoding dependency and is not included in the repository or runtime package. Its own build, license and notices apply separately. Windows and .NET media components are provided by the operating environment.

Codex and OpenAI names identify the compatible application. This is an unofficial community project and is not affiliated with or endorsed by OpenAI.
