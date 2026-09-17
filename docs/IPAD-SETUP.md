# iPad setup

The iPad is the guest screen. It shows guests themselves, counts them in, flashes
each shot back, and finishes with the QR they scan.

The camera it uses is **its own front camera**, not the R50. EOS Utility holds
the R50's live view over USB and will not share it, so the two cameras see
slightly different things — which is what the frame-guide calibration in M4
exists to correct. Until that is done, treat the on-screen guide as a hint about
proportions rather than a promise about what is in shot.

---

## Before you start

Do [docs/NETWORK-SETUP.md](NETWORK-SETUP.md) first. The guest page cannot use the
camera at all without HTTPS on a trusted certificate — Safari simply refuses, and
the failure looks like a broken app rather than a missing certificate.

---

## 1. Open the guest page

On the iPad, in Safari:

```
https://booth.local:8443/guest
```

**Always by hostname, never by IP.** iOS scopes camera permission per origin, so
visiting the raw address once makes it a different site and the permission
prompt starts over — and the certificate will not match it anyway.

Allow camera access when asked. You should see yourself, mirrored, with a framing
guide.

## 2. Add it to the home screen

Share → **Add to Home Screen**. Launched from there it opens without Safari's
address bar and tab bar, which is roughly 15% of the screen back.

## 3. Lock guests into it

**Settings → Accessibility → Guided Access → On**, and set a passcode.

Then open the booth page from the home screen and **triple-click the top button**
to start Guided Access. Guests can now use the page and nothing else. Triple-click
again and enter the passcode to leave.

Without this, a guest will eventually swipe out of the booth and into your photos
app.

## 4. Settings worth changing

| Setting | Why |
|---|---|
| **Display & Brightness → Auto-Lock → Never** | Belt and braces. The page takes a screen wake lock, but iOS drops it whenever the tab is backgrounded |
| **Brightness → full**, Auto-Brightness off | Booth lighting is not room lighting, and auto-brightness will dim the mirror mid-session |
| **Do Not Disturb → on** | A notification banner lands across the guest's face during a countdown |
| **Rotation lock → on** | In whichever orientation the stand holds it |

## 5. Mounting it

Put the iPad **directly under the lens**, as close to it as the stand allows.

The iPad cannot be where the lens is, so the two cameras look from slightly
different places — the closer they are, the smaller that error, and the better
the framing guide holds. The error is also only exact at one distance, so
calibrate at the spot guests actually stand.

---

## What the guest sees

| | |
|---|---|
| **Idle** | Themselves, mirrored, with the framing guide. This is what pulls people into the booth |
| **Countdown** | A large 3-2-1 over the mirror |
| **After each shot** | The photo just taken, held about two seconds, then back to the mirror. Not mirrored — this is what was really captured |
| **Review** | All the shots together, while the operator retakes anything that needs it |
| **Done** | The finished strip and the QR |

The peek is deliberately short and there is no per-shot Keep/Retake button. All
the accept and retake decisions happen once, at the end, on the operator's
screen — asking guests to approve every frame roughly doubles how long a session
takes, which is felt immediately when there is a queue.

---

## When it goes wrong

| Symptom | Cause |
|---|---|
| Certificate warning | Reaching the booth by IP instead of hostname, or the Certificate Trust Settings toggle was never turned on — see [NETWORK-SETUP.md](NETWORK-SETUP.md) |
| "The camera is busy" | Another tab has it. `/diagnostics` opens the camera too, to read device labels |
| Camera permission never prompts | Not a secure origin. Check the address really is `https://` and the certificate loaded — Setup on the laptop says |
| Shows the room behind the booth | The rear camera was granted. Close the tab, reopen, and choose the front camera |
| Screen dims mid-session | Auto-Lock is not set to Never |
| Guests wander off the page | Guided Access is not on |
