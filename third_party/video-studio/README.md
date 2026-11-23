# BestSnap Video Editor

A modified [Video Studio](https://github.com/HeyPuter/everything/tree/main/src/video-studio) (AGPL-3.0-only) multi-track editor, vendored for BestSnap.

Timeline editing, text, graphics, effects, filters, transitions, keyframes, on-device captions, and MP4 export. Projects and media stay on this device. BestSnap hosts the built files in an offline WebView2 window.

## What changed (2026-11-23)

- No cloud host SDK, sign-in, workers, or cloud storage.
- Cloud-only AI (chat, image, video, review sharing, script-to-video) and its UI are removed, not stubbed as error toasts.
- Captions use BestSnap's local Whisper through `src/host/bridge.js`.
- Export goes through the host save dialog (`<original>-edited.mp4` next to the recording by default).
- Attribution in About: **Based on Video Studio (AGPL-3.0)**.

See `NOTICE` for the original copyright and `LICENSE` for AGPL-3.0.

## Build

Requires Node.js 22.12 or newer.

```bash
npm ci
npm test          # scans this tree for the old cloud-host SDK name
npm run build     # writes dist/ for the WebView2 host
```

BestSnap's `build.ps1` runs the same commands and copies `dist/` next to `BestSnap.exe` as `video-studio/`.
