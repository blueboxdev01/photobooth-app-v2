import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import type {
  CameraInfo,
  CapturedPhoto,
  DeliveryUpdate,
  Layout,
  SessionSnapshot,
  SlotRect,
} from './types'

/**
 * Subscribes both windows to one authoritative session state.
 *
 * The server pushes a full snapshot on every transition rather than deltas: a
 * session has few state changes and they are small, so replacing the whole thing
 * removes any chance of the two screens drifting apart.
 */
export function useSession() {
  const [snapshot, setSnapshot] = useState<SessionSnapshot | null>(null)
  const [delivery, setDelivery] = useState<DeliveryUpdate | null>(null)
  const [layout, setLayout] = useState<Layout | null>(null)
  const [camera, setCamera] = useState<CameraInfo | null>(null)
  // Where finished sessions land, so the console can say where rather than
  // naming a folder the operator then has to go hunting for.
  const [outputFolder, setOutputFolder] = useState<string | null>(null)
  // The shape of one photo on the strip, for the guest screen's framing guide.
  // Polled with the rest of the state: it only changes when the template does.
  const [slotAspect, setSlotAspect] = useState(4 / 3)
  const [connected, setConnected] = useState(false)
  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hub/session')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('state', (s: SessionSnapshot) => setSnapshot(s))
    connection.on('delivery', (d: DeliveryUpdate) => setDelivery(d))
    connection.onreconnected(() => setConnected(true))
    connection.onreconnecting(() => setConnected(false))
    connection.onclose(() => setConnected(false))

    connection.start().then(
      () => setConnected(true),
      () => setConnected(false),
    )
    connectionRef.current = connection

    return () => {
      connectionRef.current = null
      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop()
      }
    }
  }, [])

  // Camera health is not part of session state and changes rarely, so it is
  // polled instead of pushed.
  useEffect(() => {
    let cancelled = false
    const load = async () => {
      try {
        const r = await fetch('/api/state')
        if (!r.ok) return
        const body = await r.json()
        if (!cancelled) {
          setCamera(body.camera)
          // Polled as well as pushed, so a screen that connects mid-session --
          // or reloads while the QR is up -- still learns where to point the
          // guest instead of waiting for the next push that may never come.
          setDelivery(body.delivery ?? null)
          if (typeof body.slotAspect === 'number' && body.slotAspect > 0) {
            setSlotAspect(body.slotAspect)
          }
          if (body.layout) setLayout(body.layout as Layout)
          if (typeof body.outputFolder === 'string') setOutputFolder(body.outputFolder)
          if (!connectionRef.current) setSnapshot(body.session)
        }
      } catch {
        /* server restarting */
      }
    }
    void load()
    const id = setInterval(load, 3000)
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [])

  return { snapshot, delivery, camera, connected, slotAspect, outputFolder, layout }
}

export async function command(name: string, body?: unknown) {
  await fetch(`/api/session/${name}`, {
    method: 'POST',
    headers: body ? { 'content-type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
}

/**
 * Rearrange the shots. `positions` is expressed in the order currently on screen
 * -- entry i is the position that should move into slot i -- which is exactly
 * what a drag produces without the console needing to track capture order.
 *
 * Returns the server's reason on refusal, or null. The new state arrives over the
 * hub like every other transition, so there is nothing to apply here.
 */
export async function reorder(positions: number[]): Promise<string | null> {
  try {
    const r = await fetch('/api/session/order', {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ order: positions }),
    })
    if (r.ok) return null
    const body = await r.json()
    return body.error ?? `HTTP ${r.status}`
  } catch (e) {
    return e instanceof Error ? e.message : 'Request failed'
  }
}

/** Seconds left until an absolute deadline, ticking locally. */
export function useCountdown(deadlineUtc: string | null) {
  const [remaining, setRemaining] = useState<number | null>(null)

  useEffect(() => {
    if (!deadlineUtc) {
      setRemaining(null)
      return
    }

    const end = new Date(deadlineUtc).getTime()
    const tick = () => setRemaining(Math.max(0, (end - Date.now()) / 1000))
    tick()
    const id = setInterval(tick, 100)
    return () => clearInterval(id)
  }, [deadlineUtc])

  return remaining
}

/**
 * The shot just taken, held briefly so the guest sees it before the next pose.
 *
 * Detects arrival by *file name* rather than by the photo count, because a
 * retake replaces a shot in place: redoing pose two leaves the count unchanged
 * and puts the new photo at index one, so anything watching `photos.length` or
 * `photos.at(-1)` peeks at the wrong person's pose or at nothing at all.
 *
 * The first snapshot seeds the set without peeking. Otherwise reloading the iPad
 * mid-session would flash a photo from three poses ago, as though it had just
 * been taken.
 */
export function usePeek(snapshot: SessionSnapshot | null, holdMs = 2000) {
  const [peek, setPeek] = useState<CapturedPhoto | null>(null)
  const seen = useRef<Set<string> | null>(null)

  const state = snapshot?.state
  const photos = snapshot?.photos

  useEffect(() => {
    if (!photos) return

    // Between guests: forget everything, so the next session's first shot is new
    // again rather than something this browser has already seen.
    if (state === 'Idle') {
      seen.current = null
      setPeek(null)
      return
    }

    if (seen.current === null) {
      seen.current = new Set(photos.map((p) => p.fileName))
      return
    }

    const fresh = photos.find((p) => !seen.current!.has(p.fileName))
    if (!fresh) return

    for (const p of photos) seen.current.add(p.fileName)
    setPeek(fresh)
  }, [photos, state])

  // Cut the peek short when the booth moves on. The operator may start the next
  // countdown inside the hold, and a stale photo over a live countdown is worse
  // than no peek: the guest is looking at themselves a moment ago while being
  // counted in.
  useEffect(() => {
    if (peek && state !== 'Collecting') setPeek(null)
  }, [peek, state])

  useEffect(() => {
    if (!peek) return
    const id = setTimeout(() => setPeek(null), holdMs)
    return () => clearTimeout(id)
  }, [peek, holdMs])

  return peek
}

/**
 * Keeps the guest screen awake.
 *
 * An iPad left alone between guests dims and then locks, and the booth is dark
 * exactly when somebody walks up to it. The lock is dropped whenever the tab is
 * hidden, so it has to be taken again when the tab comes back -- iOS releases it
 * on every backgrounding, including the screen being locked by hand.
 *
 * Entirely best-effort: unsupported on older iOS, and refused when the page is
 * not visible. A booth that runs with a dimming screen is still a booth.
 */
export function useWakeLock(active = true) {
  useEffect(() => {
    if (!active) return

    type Sentinel = { release: () => Promise<void> }
    const wakeLock = (navigator as Navigator & {
      wakeLock?: { request: (type: 'screen') => Promise<Sentinel> }
    }).wakeLock

    if (!wakeLock) return

    let held: Sentinel | null = null
    let cancelled = false

    const take = async () => {
      if (document.visibilityState !== 'visible') return
      try {
        const sentinel = await wakeLock.request('screen')
        if (cancelled) {
          void sentinel.release()
          return
        }
        held = sentinel
      } catch {
        /* refused; the screen dims and the booth still works */
      }
    }

    void take()
    document.addEventListener('visibilitychange', take)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', take)
      void held?.release().catch(() => {})
    }
  }, [active])
}

/**
 * Move one photo's rectangle for this session only.
 *
 * The new state arrives over the hub like every other transition, so there is
 * nothing to apply here -- only a reason to report when the server says no.
 */
export async function adjustSlot(slot: number, rect: SlotRect): Promise<string | null> {
  try {
    const r = await fetch(`/api/session/slots/${slot}`, {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ x: rect.x, y: rect.y, w: rect.w, h: rect.h }),
    })
    if (r.ok) return null
    return (await r.json()).error ?? `HTTP ${r.status}`
  } catch (e) {
    return e instanceof Error ? e.message : 'Request failed'
  }
}
