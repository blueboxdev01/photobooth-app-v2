# Self Photobooth — digital-only booth, iPad guest screen, local delivery

## Context

You already have [`blueboxdev01/photobooth-app`](https://github.com/blueboxdev01/photobooth-app): a .NET 10 + React booth for the Canon R50 with watch-folder ingest, a strip compositor, retake/reorder, a slot editor, QR delivery to Google Drive, and 181 passing tests. It reached a field-test build but was never verified against the camera.

This project is the **next version in a separate repo**: the guest screen moves off a laptop webcam onto an **iPad using its own camera**, delivery becomes **local-network only** (Drive removed), the app **fires the shutter itself** by driving EOS Utility, and the output set grows to four sizes plus an animated GIF.

The camera path does not change: EOS Utility owns the USB cable and drops JPEGs into a watch folder. That decision is what forces the iPad to use its own camera for the mirror — EOS Utility holds exclusive USB live view — and what makes the frame-guide calibration below mandatory rather than optional.

### Decisions locked in

| Area | Decision |
|---|---|
| Starting point | Full fork of `photobooth-app` into a new repo, then evolve |
| Capture | Unchanged: EOS Utility → watch folder → `WatchFolderCamera` |
| Guest live view | iPad's own front camera (no DSLR live view, no capture dongle) |
| Shutter | App clicks EOS Utility's release at countdown zero; manual fallback always available |
| Confirm flow | Quick ~2s peek after each shot; all accept/retake at the end on the strip preview |
| Sizes | 2×6 strip, 4×6, 1:1 square, 9:16 story |
| Layout editing | Templates authored ahead of time; per-session drag nudge during review |
| Delivery | Local HTTP over a travel router (primary), laptop hotspot / venue Wi-Fi as fallbacks. No Cloudflare tunnel. |
| Certificate | Hostname-agnostic: mkcert + `booth.local` to start, Let's Encrypt + a domain as a drop-in upgrade |
| Retention | Keep everything on disk; manual cleanup |
| In scope extras | Animated GIF, UI polish pass |
| Deferred | Operator live mirror, cloud delivery |

### Decisions taken during M1

- Lives in a **new** public repo, `blueboxdev01/photobooth-app-v2`. The original repo is left untouched.
- Fresh `git init` rather than a GitHub fork, so history starts clean.
- "Ignore cloud delivery hooks" means **delete** the Drive implementation, but keep a one-method publisher seam so cloud can return later without restructuring.

---

## Architecture

Unchanged from the fork: one `Photobooth.Server.exe` (ASP.NET Core + SignalR) serving one React SPA to multiple screens, with session state pushed over `/hub/session`.

What changes is the screen topology and the network surface:

```
Canon R50 ──USB──> EOS Utility ──writes JPEG──> watch folder
                        ▲                            │
                   synthetic click              WatchFolderCamera
                        │                            ▼
  Laptop: Photobooth.Server.exe  ──  SessionEngine / StripCompositor / SessionArchive
        ├── https://booth.<domain>:8443  →  iPad  /guest   (camera needs a secure context)
        ├── https://booth.<domain>:8443  →  laptop /operator, /templates, /diagnostics
        └── http://<lan-ip>:8080      →  guest phones  /s/{token}   (no camera, no cert)
```

**Two listeners is a deliberate split.** Safari refuses `getUserMedia` on plain `http://192.168.x.x`, so the iPad needs HTTPS with a cert it trusts. Guest phones only download files and are reached by raw LAN IP, which no certificate can cover. Phones get plain HTTP; the iPad gets the real cert on the fixed hostname.

---

## Milestones

### M1 — Repo bring-up and Drive removal — **done**

Copy the working tree of `photobooth-app` into this directory, `git init`, push to the new remote. Then strip cloud:

- Delete `src/Photobooth.Delivery/DriveAuth.cs`, `DriveOptions.cs`, `DrivePublisher.cs`, `UploadQueue.cs`, and the DPAPI token store; delete `tests/Photobooth.Delivery.Tests/{DpapiTokenStoreTests,UploadQueueTests}.cs` and `docs/DRIVE-SETUP.md`.
- Remove the `/api/delivery/*` endpoints and `DeliveryMessage` plumbing from `src/Photobooth.Server/Program.cs` and `SessionHub.cs`.
- Replace the Drive-shaped `IGalleryPublisher` (whose `Authorised` flag, `linkReady` callback and retry contract only exist because remote uploads are slow and fail) with a one-method `ISessionPublisher` returning a `DeliveryLink`, implemented by `LocalPublisher`. Local publishing is synchronous and cannot fail: `SessionArchive.Save` already mints the unguessable token, so the guest's URL is known the instant it returns. This is the seam that keeps cloud a later addition rather than a rewrite.
- Strip the Drive-only fields (`UploadState`, `DriveFolderId`, `DriveUrl`, `UploadAttempts`, `UploadError`, `Qr`) from `SessionRecord`. Render the QR live from the current base URL rather than storing a PNG, since a stored code would encode a stale URL after the booth moves networks.

Gate: solution builds, remaining tests green, `Photobooth.Server.exe` runs with the mock press simulator.

### M2 — HTTPS, hostname, and the network layer — **done**

**Revised after the Cloudflare question.** A tunnel is the wrong tool for the iPad link — routing the countdown between two devices three feet apart out to Cloudflare's edge makes a venue Wi-Fi hiccup freeze the guest screen. But the *certificate* a tunnel would have given us is worth having on its own, and we can get it without routing any traffic through Cloudflare.

- **Hostname-agnostic cert config.** Kestrel loads a PFX from a configured path for a configured hostname. Where the PFX came from is a runbook detail, not an architecture one, so both routes are supported and switching between them is a file swap:
  - **No domain (start here):** `mkcert` issues a local CA plus a leaf for `booth.local`. Install the **CA** on the iPad once (~5 min). Leaf certs can then be regenerated forever without touching the iPad again, so renewal is zero-touch. Keep the leaf under **398 days** — Safari enforces that validity cap.
  - **With a domain (~$10/yr):** Let's Encrypt via the **DNS-01** challenge — proves ownership with a TXT record, so it needs no inbound internet and no open ports. No profile install on any device.
- Either way the iPad must always reach the booth by **hostname, never raw IP**. iOS scopes camera permission per-origin, and one visit to the IP starts the permission prompt over.
- Point the chosen hostname at the laptop's LAN IP through the travel router's DNS override, so the iPad connects at LAN speed against a genuinely trusted name. Note that OpenWrt/GL.iNet enable DNS-rebind protection by default and it must be disabled (or a local hosts override used) for a public name to resolve to a private IP.
- Add a second Kestrel endpoint: HTTP `8080` (guest phone delivery) and HTTPS `8443` (iPad + operator), in `appsettings.json`.
- Document both cert routes, the router config and the hotspot/venue fallbacks in `docs/NETWORK-SETUP.md`. If the domain route is chosen, automate the 90-day renewal (win-acme or lego with a DNS API token).
- `BoothSettings` gains a delivery base URL that auto-detects the active LAN adapter and can be overridden in `/diagnostics`. This one setting is what makes the router / hotspot / venue-Wi-Fi fallbacks a config change instead of a code change.
- `cloudflared` stays **out of scope**, but the configurable base URL means adding it later as an optional cellular-delivery path is a config change, not a rewrite.

Gate: iPad loads `/guest` over HTTPS with no warning and no profile installed, and is granted camera permission; a phone on the same network loads `/s/{token}` over HTTP.

### M3 — The iPad guest screen — **done**

**Corrected during implementation.** The plan called for a new `/guest` route. That was wrong: `Display.tsx` already implemented the entire guest flow, so a second guest screen would have been a near-copy that drifted the first time either was touched. `/guest` and `/display` are now two names for one screen.

- `PosingMirror` asks for `facingMode: 'user'` — a soft preference, so the same page still works on a laptop. Mirroring was already in CSS.
- **Peek**: the shot just taken, held ~2s *over* the mirror rather than replacing it, so the camera stays open underneath. Detects arrival by **file name**, not count or last entry, because a retake replaces a shot in place — both obvious implementations show the wrong pose and look fine until someone retakes a middle shot at an event.
- Screen wake lock, re-taken on `visibilitychange` because iOS drops it on every backgrounding.
- Viewport and web-app meta; `docs/IPAD-SETUP.md` covers Guided Access and mounting.
- Added **vitest**, and covered `usePeek` including the retake case.

Still to verify on real hardware: the whole screen, on an actual iPad.

### M4 — Frame-guide calibration

Without this the guide box lies about what's in shot: the iPad's front camera and the R50 have different fields of view, and they sit in different places.

- New `PreviewCalibration` record in `Photobooth.Core`: a normalized rect mapping the iPad camera frame onto the R50's captured frame. Persisted in `BoothSettings`.
- Calibration flow in `/diagnostics`: fire one R50 test shot, show it as a ghost overlay on the live iPad preview, operator drags and pinches until the two line up, save.
- `PosingMirror` draws `calibration ∩ slot aspect` as the guide, so the box shows what survives both the FOV difference and the slot crop.
- Mount the iPad directly under the lens to keep parallax small; note it in `docs/FIELD-TEST.md`.

Gate: a person standing exactly on the guide edge appears at the strip slot edge in the real capture.

### M5 — Shutter automation

- New `IShutterTrigger` in `Photobooth.Cameras`, with three implementations tried in order:
  1. `EosUtilityUiaTrigger` — UI Automation `InvokePattern` on the release control, when EOS Utility exposes one.
  2. `EosUtilityClickTrigger` — `PostMessage(WM_LBUTTONDOWN/UP)` to a control point the operator records once in `/diagnostics`. **Post, don't `SendInput`** — it doesn't move the operator's real cursor, and it survives EOS Utility's custom-drawn controls where UIA typically fails.
  3. `ManualTrigger` — no-op; operator fires the remote. Always reachable from the operator UI.
- `SessionCoordinator` fires the trigger at countdown zero. The existing `SessionSettings.NoPhotoTimeoutSeconds` already covers the "nothing arrived" case, and `CountdownSeconds` stops being merely advisory.
- Windows-only P/Invoke behind an interface so the rest of the suite stays testable; add a "Test shutter" button to `/diagnostics`.

Gate: countdown zero produces a file in the watch folder, unattended, ten times running.

**This is the riskiest milestone.** Driving another app's UI breaks on EOS Utility updates. `ManualTrigger` is not a nicety — keep it one click away in the operator UI and rehearse it before the first event.

### M6 — Sizes, per-session nudge, and GIF

- **Sizes:** add `square-1x1` (1080×1080) and `story-9x16` (1080×1920) to `CanvasPresets.All`. These are screen formats, so pass `dpi: 72` — `TemplateCanvas` already carries DPI. Give `LayoutOptions.For` a case for them; the portrait default's 0.22 footer is far too tall for a square.
- **Per-session nudge:** `SessionEngine` gains `AdjustSlot(int index, TemplateSlot slot)` and `ResetSlots()`, with overrides carried on `SessionSnapshot` so they never touch the saved template. Slots are already normalized fractions and `StripCompositor` already takes them, so this is plumbing plus a drag surface on the operator's review screen — reuse the rectangle-drag editor from `Templates.tsx`.
- **GIF:** new `Photobooth.Imaging/GifBuilder.cs` using ImageSharp's GIF encoder — the session's shots, slot-cropped, ~600px wide, looping, roughly 400ms per frame. Written into the session archive and listed on the delivery page.

Gate: golden-image tests for the two new presets and a frame-count/dimension test for the GIF.

### M7 — Local delivery page

- `GET /s/{token}` over HTTP: strip, GIF, individual shots, per-file download and a "download all" zip.
- Per-session opaque token rather than the folder name, so links can't be guessed by incrementing.
- `QrRenderer` already exists — point it at `{baseUrl}/s/{token}`.
- Files stay on disk indefinitely; cleanup is manual.

One caveat to put in the runbook: on a network with no internet uplink, iOS shows "no internet" and can drop back to cellular, which breaks the download. Give the travel router an uplink (venue Wi-Fi or a phone hotspot) so the booth network is genuinely online.

Gate: scan the QR on a real phone, receive strip + GIF + individual photos.

### M8 — UI polish

A dedicated pass on both screens using the `frontend-design` skill: guest screen readable across a room with large countdown and calm transitions; operator screen dense, keyboard-driven, one-glance session state. Reuse the existing `useTheme` / `useDisplayTheme` hooks.

---

## Files that carry most of the change

| File | Change |
|---|---|
| `src/Photobooth.Server/Program.cs` | Drop `/api/delivery/*`, add `/s/{token}`, slot-adjust endpoints, dual Kestrel endpoints |
| `src/Photobooth.Web/src/PosingMirror.tsx` | Front camera, mirroring, calibrated guide |
| `src/Photobooth.Web/src/AppShell.tsx` | New `/guest` route and state machine |
| `src/Photobooth.Core/SessionEngine.cs` | `AdjustSlot`, `ResetSlots`, peek state |
| `src/Photobooth.Core/CanvasPresets.cs` | Square and story presets |
| `src/Photobooth.Core/SlotLayout.cs` | `LayoutOptions.For` cases for the new shapes |
| `src/Photobooth.Cameras/` | New `IShutterTrigger` + EOS Utility implementations |
| `src/Photobooth.Imaging/GifBuilder.cs` | New |
| `src/Photobooth.Delivery/` | Drive files deleted, `ISessionPublisher` + `LocalPublisher` added |
| `src/Photobooth.Server/BoothSettings.cs` | Base URL, `PreviewCalibration`, shutter trigger config |

Reuse as-is, do not rewrite: `StripCompositor`, `ArtInspector`, `FileTemplateProvider`, `JpegDensity`, `WatchFolderCamera`, `MockEosUtility`, `SessionArchive`, `QrRenderer`, `SessionHub`.

---

## Verification

**Automated** — `dotnet test` on the whole solution. The 181 inherited tests must stay green apart from the deliberately deleted Drive suites. New coverage: slot-override behaviour in `SessionEngineTests`, golden images for the square and story presets in `StripCompositorTests`, a `GifBuilder` frame-count/dimension test, and a `PreviewCalibration` mapping test.

**Without hardware** — the operator page's deliberately adversarial mock simulator drives a full session; run it with the iPad on `/guest` to exercise the real guest flow before the camera is involved.

**With hardware, in order:**
1. EOS Utility tethered, writing to the watch folder; confirm ingest.
2. iPad trusts the cert, loads `/guest`, grants camera permission.
3. Run M4 calibration; verify a person on the guide edge lands on the slot edge.
4. `/diagnostics` → Test shutter, ten consecutive unattended fires.
5. Full four-shot session: countdown → peek → review → retake one shot → accept.
6. Scan the QR on a phone that has never joined before; download strip, GIF, and individual photos.
7. Repeat on all three network modes (router, laptop hotspot, venue Wi-Fi) changing only the base URL.

---

## Environment constraint found during M2

**Smart App Control is in enforcement mode on the development laptop** and blocks the project's freshly built unsigned assemblies from loading (`0x800711C7`). It spread from `Photobooth.Imaging.dll` to `Photobooth.Server.dll` to `Photobooth.Delivery.dll`, in Debug and Release, and stops the app starting at all.

Disabling it is irreversible without reinstalling Windows, so the decision was to leave it on and verify through CI. `.github/workflows/ci.yml` runs the full suite on every push. A red local suite is not evidence of anything until the error has been checked.

The cost lands on anything needing the app actually running — which from here means the guest screen, the calibration flow and the shutter trigger all need hands-on checking on the booth hardware rather than at a desk.

---

## Open risks

- **EOS Utility automation is brittle.** Version updates can move or redraw the release control. Mitigated by the recorded click point and the manual fallback, not eliminated.
- **iOS camera permission is per-origin.** A changed IP silently kills the mirror. The real cert on a fixed `booth.<domain>` in M2 is the fix; verify after any network change.
- **Cert expiry bites at the worst time.** On the domain route, a broken Let's Encrypt renewal means the iPad stops trusting the booth mid-event. On the mkcert route the CA is long-lived but the leaf still expires. Either way, check expiry in the pre-event checklist rather than trusting the automation.
- **Parallax.** The iPad cannot sit where the lens sits, so calibration is exact only at one distance. Calibrate at the guest's actual standing mark.
- **Untested inheritance.** The fork's 181 tests pass but nothing in it has ever met the R50. Treat M1–M2 as also being the fork's first hardware validation.
