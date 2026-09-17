# Self Photobooth

Photobooth software for a **Canon EOS R50**, producing digital-only products:
EOS Utility saves tethered captures into a watch folder, this app ingests them,
composites a strip, and hands the guest a QR code they scan on the booth's own
network. Nothing is uploaded anywhere.

**The app never talks to the camera.** Its only interface is a folder with JPEGs
in it — EOS Utility sits upstream, and the camera upstream of that. That is what
lets the whole thing be built and tested with no camera present.

Forked from [`blueboxdev01/photobooth-app`](https://github.com/blueboxdev01/photobooth-app),
which delivered to Google Drive and used a laptop webcam for the posing mirror.
This version replaces both: delivery is local, and the guest screen is an iPad
using its own camera.

See [docs/IMPLEMENTATION-PLAN.md](docs/IMPLEMENTATION-PLAN.md) for the full plan.

## Status

| Milestone | State |
|---|---|
| **M1 Repo bring-up, Drive removed, local delivery** | **done** |
| M2 HTTPS, hostname and the network layer | next |
| M3 The iPad guest screen | |
| M4 Frame-guide calibration | |
| M5 Shutter automation | |
| M6 Sizes, per-session nudge, GIF | |
| M7 Local delivery page | |
| M8 UI polish | |

Inherited and still working: watch-folder ingest, the session engine, the
compositor and golden-image tests, per-shot retake, drag reordering, the frame
and slot editor, and the operator console.

146 tests passing. Nothing has yet been verified against a real camera.

Each session writes `data/sessions/<name>/` holding the strip, the raw photos,
and a `session.json` describing them. That folder is both the archive and what
the guest downloads from — there is no second copy anywhere that could disagree
with it.

## Guest delivery

Delivery is **local**. The photos never leave this laptop: the booth runs a small
web server, the guest's phone joins the same network, and the QR points at an
address on that network.

Each finished session gets its **own unguessable token**, so a guest reaches
their own photos and nobody else's — there is no gallery, and no id to edit.

**There is nothing to wait for.** The files are on disk before the session ends,
so the link is ready the instant the strip is composed. No upload, no queue, no
retry, no sign-in that can expire mid-event. That is the whole reason the Drive
path was removed rather than switched off.

**The address is the one thing that fails silently.** A wrong origin still
renders a perfectly scannable QR, and nobody finds out until a guest scans it and
gets nothing. So the booth detects its own address, shows it in **Setup** next to
the override, and warns on the operator screen if it has resolved to something
only this machine can reach.

Detection picks the adapter that has a gateway, because a laptop at an event
routinely also has a VPN adapter and a virtual switch, and either will happily
offer an address no phone can dial. Override it in Setup when detection guesses
wrong — the change applies immediately, no restart.

The QR is **rendered on demand**, not saved beside the photos. The booth's
address changes with the network it is plugged into, and a stored code would go
on confidently pointing at the address of the last event.

## Retaking one shot

Any single shot can be redone from **review**, before you press Accept: press
**Retake this one** on its thumbnail and the booth goes back to waiting for
exactly that pose. Every other shot keeps its photo and its position, and the
replacement drops into the same slot.

Both screens name the pose being redone, so a guest reshooting photo two is not
told they are on photo four.

**Retake** in the rail still means the shot taken *most recently*, whatever
position it has been dragged to — the two are deliberately separate.

## Rearranging the shots

The guest poses N times, and which of those opens the strip is a judgement no
software makes well. So during **review**, before you press Accept, the shots can
be put in any order: drag a thumbnail, or use the arrows on it. **Back to capture
order** undoes the lot.

The order decides everything downstream, not just the preview -- the strip, the
numbering of the raw photos in the session folder, and what the guest screen
shows. `photo-1.jpg` is whichever shot you put first.

Each thumbnail keeps its **capture number**, so a photo dragged to the front is
still labelled "shot 4" and you can talk about it with the person next to you.
Retake still discards the shot taken *last*, whatever position it has been moved
to, and its replacement comes back into the same slot rather than jumping to the
end.

Rearranging is only possible while reviewing: before that the set is incomplete,
and after Accept the strip is already being built.

## Two cameras, two entirely separate paths

| | Capture | Preview |
|---|---|---|
| Device | Canon EOS R50 | the iPad's own front camera |
| Reaches the app as | JPEGs in a watch folder | a stream inside the guest page |
| Triggered by | a remote, or the app driving EOS Utility (M5) | n/a |
| App can trigger it? | not directly — see M5 | n/a |

The app cannot fire the shutter over the USB cable, because EOS Utility owns it.
A photo therefore *arrives* as an event rather than being requested, and
everything downstream is built around that.

The same exclusivity is why the iPad uses its own camera rather than the R50's
live view: EOS Utility holds that too. The cost is that the two cameras do not
see the same thing, which is what the frame-guide calibration in M4 exists to
correct.

## Running it

**From a release** — download the ZIP, unzip, run `Photobooth.Server.exe`.
Nothing to install.

**From source** — needs the .NET 10 SDK and Node 22:

```bash
cd src/Photobooth.Web && npm ci && npm run build && cd ../..
dotnet build
dotnet src/Photobooth.Server/bin/Debug/net10.0/Photobooth.Server.dll
```

Then open:

| | |
|---|---|
| <http://localhost:5000/operator> | controls — on your laptop |
| <http://localhost:5000/display> | guest screen — fullscreen on the monitor |
| <http://localhost:5000/guest> | the iPad guest screen (M3) |
| <http://localhost:5000/templates> | frame upload and the slot editor |
| <http://localhost:5000/diagnostics> | booth setup, and what the app is seeing |

With no camera attached, the operator page can simulate the shutter. The mock is
deliberately adversarial — it writes slowly in chunks and can reproduce a stale
file, a duplicate name, and a transfer that stalls — so ingest is exercised
before real hardware exists.

## Before an event

**[docs/QA-CHECKLIST.md](docs/QA-CHECKLIST.md)** — half a day, on the machine and
camera you will actually use. It starts with the three checks that could change
your plans (does the remote work while tethered, does the camera stay powered,
does a press become a strip) and ends in a go/no-go.

## Field testing

See **[docs/FIELD-TEST.md](docs/FIELD-TEST.md)**. The short version: the build
runs standalone, and `/diagnostics` reports what the app saw,
what it rejected and why, and exactly which commit produced the answer.

## Template art

Art is uploaded in **Templates** and can be either kind. Which one it is gets
**detected from the image**, by checking whether it is transparent where the
photos go:

| | |
|---|---|
| **Backdrop** | Opaque across the photo areas. Drawn *behind*, with the photos on top. PNG or JPEG |
| **Frame** | Transparent where the photos go. Drawn *over* them, showing them through its windows. PNG only |

Judging by transparency **inside the photo slots** rather than overall is what
makes this reliable: a frame with a wide solid border is mostly opaque and is
still a frame. If the guess is ever wrong, the editor has a one-click override.

Upload at exactly these pixel sizes — all at 300 DPI:

| Output size | Pixels |
|---|---|
| Photo strip 2×6 | 600 × 1800 |
| Portrait 4×6 | 1200 × 1800 |
| Portrait 5×7 | 1500 × 2100 |
| Landscape 6×4 | 1800 × 1200 |
| Landscape 7×5 | 2100 × 1500 |

Anything else is scaled to **fill and centre-crop**, so proportions are never
distorted but the edges get trimmed. The editor states the expected size next to
the upload control, and warns when what you uploaded does not match.

Photo slots can be dragged and resized freely in the editor. Note that changing
the photo count or output size in **Setup** regenerates the slots evenly and
discards manual positioning, so settle those first.

## Upgrading a booth

`data/` and `templates/` live **next to the .exe**, so when you unzip a new
release, copy both across from the old folder. `data/` holds every past session;
`templates/` holds any frame you made in the editor.

## Booth setup

Everything an event needs is on the diagnostics page under **Setup**, saved to
`data/settings.json` so it survives a restart.

**Two folders, and they must be different ones.**

| | |
|---|---|
| **Watch folder** | Wherever EOS Utility saves. Every guest's raw frames land here together — it belongs to Canon's software, and the app only copies out of it |
| **Output folder** | Where a finished session is filed, **one subfolder per guest**, holding the raw photos, the strip and a `session.json` |

Paste a path, press *Check* to confirm it is usable, then save. Applied
immediately, no restart.

**Strip layout.** Pick an output size — the shape decides the arrangement, so a
portrait size stacks photos into a strip while a landscape one runs them along a
row and then into a grid. Set how many photos a strip holds, within the
minimum and maximum this event allows, and the slots are placed evenly. They can
still be nudged by hand in the template editor afterwards.

Changing the size or photo count re-lays the slots out, which **detaches frame
art** drawn for the old shape — the PNG stays on disk and can be re-attached in
the editor once you have art for the new layout.

**Guest display.** A backdrop colour and an optional image, so the booth can
match an event.

**Timings.** Countdown and the no-photo timeout. The timeout is a guess until
someone measures the real press-to-file latency, which the same page can do.

## Configuration

`src/Photobooth.Server/appsettings.json`, mirrored by `appsettings.example.json`.

Everything under `Camera:WatchFolder` is an **assumption** about how EOS Utility
behaves that has not been checked against a real camera. It is configuration
rather than code precisely so that when the field test contradicts it, the fix is
a settings change and not a rewrite.

Relative paths resolve against the app folder, so a published build behaves the
same as a local run.

## Releases

Push a tag and CI builds the frontend, runs the tests, publishes a self-contained
`win-x64` single file, and attaches the ZIP to a GitHub release:

```bash
git tag v0.5.0 && git push origin v0.5.0
```

## Repo hygiene

`data/` holds guest photos and is never committed. Local overrides live in
untracked `appsettings.Local.json`; only `appsettings.example.json` is tracked.
The diagnostics bundle deliberately excludes photographs.
