import { useCallback, useEffect, useState } from 'react'

interface Preset {
  id: string
  label: string
  size: string
  orientation: 'Portrait' | 'Landscape'
  width: number
  height: number
}

interface SettingsResponse {
  watchFolder: string
  outputFolder: string
  countdownSeconds: number
  noPhotoTimeoutSeconds: number
  settingsFile: string
  suggestions: string[]
  layout: {
    minPhotos: number
    maxPhotos: number
    photoCount: number
    supportedMin: number
    supportedMax: number
    canvasPresetId: string | null
    orientation: 'Portrait' | 'Landscape'
    canvas: { width: number; height: number; dpi: number }
    template: string
    presets: Preset[]
  }
  display: {
    backgroundColor: string
    backgroundImage: string | null
  }
  delivery: {
    /** The origin actually being printed on QR codes right now. */
    baseUrl: string
    /** What auto-detection makes of the current network, override or not. */
    detected: string
    /** The operator's override, or null when detection is in charge. */
    overridden: string | null
  }
}


interface FolderCheck {
  path: string
  ok: boolean
  error: string | null
  exists: boolean
  willCreate: boolean
  jpegCount: number
}

type Status = { ok: boolean; text: string } | null

/**
 * Booth setup.
 *
 * Two folders, and they must be different ones. The **watch folder** is wherever
 * EOS Utility happens to save — every guest's raw frames land there together,
 * which is what makes it useless for handing photos to a person. The **output
 * folder** is where a finished session is filed under its own subfolder, raws
 * and strip together.
 *
 * There is no folder picker: browsers will not hand a web page a filesystem
 * path. Paste it from EOS Utility's save-location setting, or Explorer's address
 * bar.
 */
export function Settings({ onChanged }: { onChanged?: () => void }) {
  const [data, setData] = useState<SettingsResponse | null>(null)
  const [watch, setWatch] = useState('')
  const [output, setOutput] = useState('')
  const [countdown, setCountdown] = useState(3)
  const [timeout, setTimeoutSeconds] = useState(20)
  const [minPhotos, setMinPhotos] = useState(2)
  const [maxPhotos, setMaxPhotos] = useState(6)
  const [photoCount, setPhotoCount] = useState(3)
  const [presetId, setPresetId] = useState('')
  const [colour, setColour] = useState('#14161A')
  const [check, setCheck] = useState<{ which: 'watch' | 'output'; result: FolderCheck } | null>(null)
  const [status, setStatus] = useState<Status>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    const r = await fetch('/api/settings')
    if (!r.ok) return
    const body: SettingsResponse = await r.json()
    setData(body)
    setWatch(body.watchFolder)
    setOutput(body.outputFolder)
    setCountdown(body.countdownSeconds)
    setTimeoutSeconds(body.noPhotoTimeoutSeconds)
    setMinPhotos(body.layout.minPhotos)
    setMaxPhotos(body.layout.maxPhotos)
    setPhotoCount(body.layout.photoCount)
    setPresetId(body.layout.canvasPresetId ?? '')
    setColour(body.display.backgroundColor)
  }, [])

  useEffect(() => { void load() }, [load])

  if (!data) return null

  const save = async (patch: Record<string, unknown>, success: string) => {
    setBusy(true)
    try {
      const r = await fetch('/api/settings', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      })
      const body = await r.json()
      if (!r.ok) {
        setStatus({ ok: false, text: body.error ?? `HTTP ${r.status}` })
        return false
      }

      setStatus({ ok: true, text: body.note ? `${success} ${body.note}` : success })
      setCheck(null)
      await load()
      onChanged?.()
      return true
    } finally {
      setBusy(false)
    }
  }

  const checkFolder = async (which: 'watch' | 'output') => {
    const folder = which === 'watch' ? watch : output
    setBusy(true)
    try {
      const r = await fetch('/api/settings/check-folder', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(which === 'watch'
          ? { watchFolder: folder }
          : { outputFolder: folder }),
      })
      setCheck(r.ok ? { which, result: await r.json() } : null)
    } finally {
      setBusy(false)
    }
  }

  const uploadBackground = async (file: File) => {
    const form = new FormData()
    form.append('background', file)
    const r = await fetch('/api/settings/display-background', { method: 'POST', body: form })
    const body = await r.json()
    if (!r.ok) {
      setStatus({ ok: false, text: body.error ?? 'Upload failed' })
      return
    }
    setStatus({ ok: true, text: 'Backdrop uploaded.' })
    await load()
    onChanged?.()
  }

  const checkNote = (which: 'watch' | 'output') => {
    if (check?.which !== which) return null
    const c = check.result
    return (
      <p className={c.ok ? 'muted small' : 'banner'}>
        <code>{c.path}</code>{' '}
        {c.ok
          ? c.willCreate
            ? '— usable; it will be created when you save'
            : `— usable, ${c.jpegCount} JPEG(s) already there`
          : `— ${c.error}`}
      </p>
    )
  }

  const { layout, display } = data
  const preset = layout.presets.find((p) => p.id === presetId)

  return (
    <section className="settings">
      <h2>Setup</h2>

      {status && <p className={status.ok ? 'muted' : 'banner'}>{status.text}</p>}

      <div className="settings__group">
        <h3>Folders</h3>

        <label className="settings__folder">
          Watch folder — where EOS Utility saves
          <input value={watch} spellCheck={false}
                 placeholder="C:\Users\you\Pictures\Tethered"
                 onChange={(e) => { setWatch(e.target.value); setCheck(null) }} />
        </label>
        <div className="controls">
          <button className="btn" disabled={busy || !watch.trim()} onClick={() => void checkFolder('watch')}>
            Check
          </button>
          <button className="btn btn--primary"
                  disabled={busy || watch.trim() === data.watchFolder}
                  onClick={() => void save({ watchFolder: watch.trim() }, 'Watch folder applied.')}>
            Save watch folder
          </button>
        </div>
        {checkNote('watch')}

        <label className="settings__folder">
          Output folder — finished sessions, one subfolder per guest
          <input value={output} spellCheck={false}
                 placeholder="C:\Users\you\Pictures\Photobooth"
                 onChange={(e) => { setOutput(e.target.value); setCheck(null) }} />
        </label>
        <div className="controls">
          <button className="btn" disabled={busy || !output.trim()} onClick={() => void checkFolder('output')}>
            Check
          </button>
          <button className="btn btn--primary"
                  disabled={busy || output.trim() === data.outputFolder}
                  onClick={() => void save({ outputFolder: output.trim() }, 'Output folder applied.')}>
            Save output folder
          </button>
        </div>
        {checkNote('output')}
        <p className="muted small">
          Each accepted session becomes its own folder here, holding the raw
          photos, the finished strip and a <code>session.json</code>. It must be a
          different folder from the watch folder.
        </p>

        {data.suggestions.length > 0 && (
          <p className="muted small">
            Try:{' '}
            {data.suggestions.map((s) => (
              <button key={s} className="linkish"
                      onClick={() => { setOutput(s); setCheck(null) }}>{s}</button>
            ))}
          </p>
        )}
      </div>

      <div className="settings__group">
        <h3>Strip layout</h3>

        <label>Output size
          <select value={presetId} onChange={(e) => setPresetId(e.target.value)}>
            {!layout.canvasPresetId && <option value="">Custom ({layout.canvas.width}×{layout.canvas.height})</option>}
            {layout.presets.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label} — {p.size} {p.orientation === 'Portrait' ? '↕' : '↔'}
              </option>
            ))}
          </select>
        </label>
        <p className="muted small">
          The shape decides the arrangement: a portrait size stacks photos into a
          strip, a landscape one runs them along a row and then into a grid.
          {preset && ` Currently ${preset.orientation.toLowerCase()}.`}
        </p>

        <div className="fields">
          <label>Photos per strip
            <input type="number" min={layout.minPhotos} max={layout.maxPhotos}
                   value={photoCount}
                   onChange={(e) => setPhotoCount(Number(e.target.value))} />
          </label>
          <label>Minimum
            <input type="number" min={layout.supportedMin} max={layout.supportedMax}
                   value={minPhotos}
                   onChange={(e) => setMinPhotos(Number(e.target.value))} />
          </label>
          <label>Maximum
            <input type="number" min={layout.supportedMin} max={layout.supportedMax}
                   value={maxPhotos}
                   onChange={(e) => setMaxPhotos(Number(e.target.value))} />
          </label>
        </div>
        <p className="muted small">
          The bounds are what this event allows ({layout.supportedMin}–
          {layout.supportedMax} is what the layout engine supports). The photo
          count is also the number of shots a session takes.
        </p>

        <div className="controls">
          <button className="btn btn--primary" disabled={busy}
                  onClick={() => void save(
                    { minPhotos, maxPhotos, photoCount, canvasPresetId: presetId || undefined },
                    'Layout regenerated.')}>
            Apply layout
          </button>
          <a className="linkish" href="/templates">see it in the editor →</a>
        </div>
        <p className="muted small">
          Slots are placed evenly and can still be nudged in the{' '}
          <a href="/templates">template editor</a>. Changing the size or photo
          count re-lays them out, which detaches frame art drawn for the old shape.
        </p>
      </div>

      <div className="settings__group">
        <h3>Guest display</h3>

        <div className="fields">
          <label>Backdrop colour
            <input type="color" value={colour} onChange={(e) => setColour(e.target.value)} />
          </label>
          <label>Backdrop image
            <span className="btn filebtn">
              Choose image…
              <input type="file" accept="image/png,image/jpeg"
                     onChange={(e) => {
                       const f = e.target.files?.[0]
                       if (f) void uploadBackground(f)
                     }} />
            </span>
          </label>
        </div>

        {display.backgroundImage && (
          <div className="settings__backdrop">
            <img src={`${display.backgroundImage}?v=${Date.now()}`} alt="Current backdrop" />
            <button className="btn" onClick={() => void save(
              { clearDisplayBackgroundImage: true }, 'Backdrop image removed.')}>
              Remove image
            </button>
          </div>
        )}

        <div className="controls">
          <button className="btn btn--primary"
                  disabled={busy || colour.toUpperCase() === display.backgroundColor.toUpperCase()}
                  onClick={() => void save(
                    { displayBackgroundColor: colour }, 'Backdrop colour applied.')}>
            Save colour
          </button>
        </div>
        <p className="muted small">
          Shown behind the guest screen, so the booth can match an event. The
          image is drawn over the colour and covers the screen.
        </p>
      </div>

      <Delivery data={data} busy={busy} save={save} />

      <div className="settings__group">
        <h3>Timings</h3>
        <div className="fields">
          <label>Countdown (s)
            <input type="number" min={0} max={30} value={countdown}
                   onChange={(e) => setCountdown(Number(e.target.value))} />
          </label>
          <label>No-photo timeout (s)
            <input type="number" min={5} max={300} value={timeout}
                   onChange={(e) => setTimeoutSeconds(Number(e.target.value))} />
          </label>
        </div>
        <div className="controls">
          <button className="btn btn--primary"
                  disabled={busy || (countdown === data.countdownSeconds
                                     && timeout === data.noPhotoTimeoutSeconds)}
                  onClick={() => void save(
                    { countdownSeconds: countdown, noPhotoTimeoutSeconds: timeout },
                    'Timings applied.')}>
            Save timings
          </button>
        </div>
        <p className="muted small">
          Set the timeout from the press-to-file latency measured below — it is a
          guess until someone with a camera measures it.
        </p>
      </div>

      <p className="muted small">
        Saved to <code>{data.settingsFile}</code>, so all of this survives a
        restart. Folder changes apply immediately; no restart needed.
      </p>
    </section>
  )
}

/**
 * Where guests are sent to collect their photos.
 *
 * The one setting here that matters is the address, and it matters because it
 * fails invisibly: a wrong origin still produces a perfectly scannable QR. The
 * booth moves between a travel router, a laptop hotspot and venue wifi, and only
 * the operator knows which -- so detection is shown next to the override rather
 * than hidden behind it.
 */
function Delivery({
  data,
  busy,
  save,
}: {
  data: SettingsResponse
  busy: boolean
  save: (patch: Record<string, unknown>, success: string) => Promise<boolean>
}) {
  const { baseUrl, detected, overridden } = data.delivery
  const [value, setValue] = useState(overridden ?? '')

  const unreachable = baseUrl.includes('127.0.0.1') || baseUrl.includes('localhost')

  return (
    <div className="settings__group">
      <h3>Guest delivery</h3>

      {unreachable && (
        <p className="banner">
          This address only works on this machine, so no guest phone can reach it.
          Check the booth is on a network, or set the address by hand below.
        </p>
      )}

      <dl className="facts">
        <dt>Guest link</dt>
        <dd><code>{baseUrl}/s/&hellip;</code></dd>
        <dt>Detected</dt>
        <dd className="muted">{detected}</dd>
      </dl>

      <label>Address override
        <input className="control" value={value} spellCheck={false}
               placeholder={detected}
               onChange={(e) => setValue(e.target.value)} />
      </label>
      <div className="controls">
        <button className="btn" disabled={busy || value.trim() === (overridden ?? '')}
                onClick={() => void save(
                  { deliveryBaseUrl: value.trim() },
                  value.trim() ? 'Delivery address applied.' : 'Back to auto-detection.')}>
          Save address
        </button>
      </div>
      <p className="muted small">
        Leave it empty to use whichever network the booth is on. Set it when this
        laptop has more than one address — a VPN or a virtual switch will happily
        offer one no phone can reach.
      </p>

      <p className="muted small">
        Photos stay on this machine. Each session gets its own unguessable link,
        so a guest sees their own photos and nobody else&rsquo;s, and nothing is
        uploaded anywhere.
      </p>
    </div>
  )
}
