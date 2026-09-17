# Test build — setting it up

Everything needed to run the booth for the first time, in order. Budget about
45 minutes for the first run, most of it waiting for EOS Utility to install.

**What this test is for.** Nothing in this build has ever met the camera. The
point is to find out what the app assumes about EOS Utility that turns out to be
wrong, and whether the iPad guest screen works in a real room. Notes on what
broke are worth more than a session that happened to succeed.

---

## What you need

| | |
|---|---|
| Laptop | Windows 10 or 11 |
| Camera | Canon EOS R50, its USB-C cable, and a charged battery |
| iPad | Any iPad with a front camera, on iOS 15 or later |
| Network | A router the laptop and iPad can both join. Home wifi is fine for this test |
| Phone | Any phone, to collect photos at the end |

No printer. This booth is digital only.

---

## 1. Canon EOS Utility

The app never talks to the camera. EOS Utility does, and drops JPEGs into a
folder the app watches.

1. Install **EOS Utility** from Canon's site (search "EOS Utility download").
2. Plug the R50 into the laptop by USB and switch it on.
3. In EOS Utility choose **Remote Shooting**.
4. Find **Preferences → Destination Folder** and note the path. That is the
   watch folder, and the app needs it in step 3.
5. Set the camera to **stills**, not video.
6. Turn **auto power off** off, on the camera. A booth that sleeps between
   guests misses the next one.

Take one photo with the shutter and confirm a JPEG appears in that folder. If it
does not, nothing downstream will work, and that is worth saying straight away.

## 2. Start the booth

Unzip the download anywhere — the desktop is fine — and run
**`Photobooth.Server.exe`**.

Windows will warn about an unknown publisher: **More info → Run anyway**. The
build is not code-signed.

Windows will also ask about the firewall. **Allow it on private networks.** If
that prompt is dismissed, the booth works on the laptop and is invisible to
every other device, which is a confusing way to lose an hour.

Leave the window open. Closing it stops the booth.

Then open **<http://localhost:8080/operator>** in a browser.

## 3. Point it at the camera's folder

Go to **Setup** in the left-hand rail.

- **Watch folder** — the path from step 1. Press *Check*, then save.
- **Output folder** — anywhere else. It must not be the watch folder.
- **Guest delivery** — note the address shown. It should look like
  `http://192.168.x.x:8080`. If it says `127.0.0.1` or `localhost`, the laptop is
  not on a network and guests will not be able to collect anything.

## 4. Set up the iPad

Join the iPad to the same wifi as the laptop, then open the delivery address from
step 3 with `/setup` on the end:

```
http://192.168.x.x:8080/setup
```

Follow the four steps on that page. It walks through installing the booth's
certificate, which iOS needs before it will let a web page use the camera.

**Step three is the one everybody misses.** Installing the profile is not the
same as trusting it — you also have to switch it on under
**Settings → General → About → Certificate Trust Settings**. Without that, the
profile is installed, looks completely correct, and the camera still will not
work.

Then open the guest screen, by name rather than by address:

```
https://booth.local:8443/guest
```

Allow the camera. You should see yourself, mirrored, with a framing guide.

> If `booth.local` does not resolve, use `https://192.168.x.x:8443/guest` and
> accept the warning. The camera still works; just expect to allow it again if
> the laptop's address changes. There is more on this in `NETWORK-SETUP.md`.

Worth doing while you are there: **Share → Add to Home Screen** for a fullscreen
page, and **Auto-Lock → Never** so the screen stays on.

## 5. Run a session

Camera pointed at the scene, iPad where a guest would see it, and someone
standing in front of both.

1. On the operator screen, press **Instant session**.
2. The iPad counts down 3-2-1. **Press the camera's shutter at zero.**
3. The photo appears on the iPad for a couple of seconds, then it returns to the
   mirror for the next pose.
4. Repeat until all the shots are taken.
5. The operator screen shows every shot. **Retake this one** redoes a single
   pose; the thumbnails can be dragged into a different order.
6. **Placement** lets you drag a photo's position on the strip, for this guest
   only — it does not change the template.
7. Press **Accept**. The strip and a GIF are built, and a QR appears on the iPad.
8. Scan the QR with a phone **on the same wifi**. You should get the strip, the
   GIF and every individual shot.

The app cannot fire the shutter yet, so the countdown and the press are only as
synchronised as the person pressing it. That is the next thing being built.

## 6. Try the other sizes

**Setup → Strip layout** offers 2×6, 4×6, 5×7, landscape, square and 9×16 story.
Changing the size or photo count re-lays the slots out and detaches any frame
art. Run a session on the square and on the story — those two are new and have
never been used on real photographs.

---

## What to report

Most useful, roughly in order:

1. **Anything about EOS Utility.** File names, how long a photo takes to appear,
   what happens when the camera sleeps or the cable is knocked. Every one of
   those is currently a guess in the app's configuration.
2. **The framing guide versus what the camera actually got.** The iPad and the
   R50 see different things and the guide is not calibrated yet — so: stand on
   the edge of the guide, take a shot, and say how far out it was.
3. **Anything that needed explaining.** If a step here was wrong or missing, that
   is a bug in this page.
4. **Photos of the setup.** How the iPad and camera ended up positioned tells us
   more than a description.

**Setup → Download diagnostics bundle** collects the logs, the settings and what
the app made of every file it saw. It contains no photographs. Send that along
with the notes.

---

## When it goes wrong

| Symptom | Cause |
|---|---|
| Photos never appear on the operator screen | Watch folder is wrong, or EOS Utility is saving to the camera card rather than the computer |
| Guest screen will not use the camera | Certificate Trust Settings toggle, step 4. Or the address is `http://` rather than `https://` |
| "The camera is busy" on the iPad | Another tab has it — the Setup page opens the camera too |
| iPad shows the room behind the booth | It got the rear camera. Close the tab and reopen it |
| QR scans but the page will not load | The phone is on cellular or a different wifi. It must be on the booth's network |
| Guest link shows `127.0.0.1` | The laptop is not on a network |
| Nothing reachable from the iPad or phone | The Windows firewall prompt was dismissed — see `NETWORK-SETUP.md` |
| Strip is built but a photo is missing | Worth reporting with the diagnostics bundle; that should not happen |

Anything not on this list is worth sending. A build nobody can set up is the
most useful failure of all, and the least likely to be reported.
