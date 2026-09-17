# Network setup

Everything about getting devices to reach the booth: the two listeners, the
certificate the iPad needs, and what to do when the venue's network is the
problem.

---

## Why there are two listeners

| | Port | Who | Why |
|---|---|---|---|
| **Delivery** | `8080` HTTP | guests' phones | Downloads only. Phones reach the booth by raw LAN address, which no certificate can cover, so HTTPS here would hand every guest a security warning instead of their photos |
| **Booth** | `8443` HTTPS | the iPad, the operator laptop | The guest page uses the iPad's camera, and Safari only grants that on a secure origin |

Both are set in `appsettings.json` under `Network`. The booth starts even with no
certificate at all — you lose the iPad's camera and nothing else, which is a
degraded booth rather than no booth.

---

## The certificate

The app does not care where the certificate came from. It loads a PKCS#12
bundle from `Network:CertificatePath`, so you can start on mkcert today and move
to a real domain later by replacing one file.

### Rule that matters more than either method

**Always reach the booth by hostname, never by IP.** iOS scopes camera
permission per origin. Visit `https://192.168.8.2:8443` once and it is a
different origin from `https://booth.local:8443` — the permission prompt starts
over, and the certificate will not match the address anyway.

### Option A — let the booth make its own (default, nothing to install)

On first run, if there is no certificate at `Network:CertificatePath`, the booth
creates its own authority and a certificate for `Network:Hostname`, and hands the
authority out at `http://<booth>:8080/booth-ca.crt`.

The iPad installs it from `http://<booth>:8080/setup` — four steps, no laptop
tooling, no file transfer. See [IPAD-SETUP.md](IPAD-SETUP.md).

This is exactly what mkcert would give you: a private authority that has to be
trusted by hand on each device. It is regenerated only when the file is missing,
so deleting `certs/` means trusting it again on every device.

### Option B — mkcert, no domain, free

Good enough for everything up to and including a paid event. One five-minute
setup on the iPad, then never again.

```bash
winget install FiloSottile.mkcert
```

Create the local CA and a certificate for the booth:

```bash
mkcert -install
mkcert -pkcs12 -p12-file certs/booth.pfx booth.local
```

mkcert's default PKCS#12 password is `changeit`. Put it in an untracked
`appsettings.Local.json` next to the exe:

```json
{ "Network": { "CertificatePassword": "changeit" } }
```

Then trust the CA on the iPad, once:

1. Find the CA with `mkcert -CAROOT` and get `rootCA.pem` onto the iPad — AirDrop
   or email it to yourself.
2. On the iPad: **Settings → General → VPN & Device Management → Install** the
   downloaded profile.
3. **Settings → General → About → Certificate Trust Settings** and turn the
   toggle on for mkcert. **This second step is the one everybody forgets** —
   without it the profile is installed but not trusted, and Safari still refuses.

Keep the leaf certificate under **398 days**. Safari enforces that cap, and
mkcert's default validity is longer than it. Re-issue with the command above when
it lapses — the iPad keeps trusting the CA, so there is nothing to reinstall
there.

### Option C — a real domain, ~$10/yr

Removes the profile install entirely, which is worth it if you will ever have
more than one guest device, or hand the booth to someone else to run.

1. Register a domain and put its DNS on Cloudflare (their registrar sells at
   cost).
2. Issue a certificate for `booth.yourdomain.com` using the **DNS-01** challenge.
   It proves ownership with a TXT record, so it needs no inbound internet and no
   open ports — the booth never has to be reachable from outside.

   ```bash
   winget install win-acme.win-acme
   ```

   Run `wacs.exe`, choose a manual/DNS validation plugin with your DNS
   provider's API token, and export the result as `.pfx`.
3. Point `Network:CertificatePath` at it. Nothing else changes.

Automate the 90-day renewal, and **still check the expiry before an event** —
Setup shows the days remaining, and warns under three weeks. A renewal that
silently stopped working is discovered when the iPad stops trusting the booth,
which is always at the worst moment.

---

## Making the hostname resolve

The iPad has to resolve `booth.local` (or `booth.yourdomain.com`) to the
laptop's address on the booth network.

**With mkcert / `booth.local`** — `.local` is mDNS, which iOS speaks natively.
Usually it just works. If it does not, add a static entry on the router.

**With a real domain** — two ways:

- **Router DNS override.** On GL.iNet/OpenWrt, add a static hosts entry mapping
  the name to the laptop's LAN IP. Note that **DNS-rebind protection is on by
  default** and will block a public name resolving to a private address; either
  disable it or use the hosts override, which is exempt.
- **A public A record pointing at the private IP.** Perfectly legal — put
  `192.168.8.2` in the A record with Cloudflare proxying **off**. It resolves
  everywhere and only works on the booth network, which is the only place you
  use it. Same rebind-protection caveat applies.

Give the laptop a **DHCP reservation** so its address does not move.

---

## The network itself

Recommended: a **travel router** (GL.iNet Opal ~$40, Beryl AX ~$80–100). The
booth gets its own network that behaves the same at every venue, and the laptop
keeps a stable address.

**Give the router an internet uplink** — venue wifi or a phone hotspot. On a
network with no internet, iOS shows "no internet" and can quietly fall back to
cellular, at which point the guest's phone cannot reach the booth and the QR
appears broken.

Fallbacks, in order of preference:

| | Notes |
|---|---|
| **Laptop hotspot** | Self-contained, no extra hardware. Windows hotspot is unreliable and occupies the wifi radio |
| **Venue wifi** | No hardware at all, but client isolation on guest networks commonly blocks phone-to-laptop traffic entirely, and the laptop's address moves |

Switching between them is a **Setup** change, not a code change: set the
delivery address override, or clear it to go back to auto-detection.

---

## Windows firewall

Listening on all interfaces raises a firewall prompt the first time. Allow it for
**private** networks. If it was dismissed, the symptom is that the booth works on
the laptop and is unreachable from every other device:

```powershell
New-NetFirewallRule -DisplayName "Photobooth" -Direction Inbound -Protocol TCP -LocalPort 8080,8443 -Action Allow -Profile Private
```

---

## Checking it

From the laptop, `Setup → Guest delivery` shows the effective address and what
detection makes of the current network. Then, in order:

1. **Guest link** is a LAN address — not `127.0.0.1`, not `localhost`.
2. From a phone on the booth network, open that address. It should answer.
3. From the iPad, open `https://booth.local:8443/guest` — **no** certificate
   warning, and the camera prompt appears.
4. Check the certificate's days remaining in Setup.

If the iPad shows a certificate warning, it is almost always the Certificate
Trust Settings toggle in step 3 of Option A, or reaching the booth by IP instead
of hostname.
