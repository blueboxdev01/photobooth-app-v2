import { useRef, useState } from 'react'
import { AppShell, Panel, RailSection } from './AppShell'
import { photoUrl } from './types'
import type {
  DeliveryUpdate,
  Layout,
  SessionSnapshot,
  SessionState,
  SlotRect,
} from './types'
import { adjustSlot, command, reorder, useCountdown, useSession } from './useSession'

const MOCK_MODES = [
  ['Normal', 'Simulate press'],
  ['DuplicateName', 'Duplicate name'],
  ['Stale', 'Stale file'],
  ['NeverFinishes', 'Stalled transfer'],
] as const

/** What the operator should understand at a glance, per state. */
const HEADLINE: Record<SessionState, string> = {
  Idle: 'Ready for the next guest',
  Countdown: 'Get ready',
  Collecting: 'Waiting for the photo',
  TimedOut: 'No photo arrived',
  ReviewShots: 'All shots are in',
  Composing: 'Building the strip',
  ShowQr: 'Showing the QR code',
  Done: 'Session complete',
}

export function Operator() {
  const { snapshot, delivery, camera, connected, outputFolder, layout } = useSession()
  const [mockResult, setMockResult] = useState<{ ok: boolean; text: string } | null>(null)

  if (!snapshot) {
    return (
      <AppShell page="/operator">
        <Panel><p className="muted">Connecting…</p></Panel>
      </AppShell>
    )
  }

  const state = snapshot.state
  const running = state !== 'Idle' && state !== 'Done'

  const press = async (mode: string) => {
    try {
      const r = await fetch(`/api/mock/press?mode=${mode}`, { method: 'POST' })
      const body = await r.json()
      if (!r.ok) {
        setMockResult({ ok: false, text: body.error ?? `HTTP ${r.status}` })
        return
      }
      setMockResult({
        ok: true,
        text: running
          ? `wrote ${body.file}`
          : `wrote ${body.file} — no session running, so it will be ignored`,
      })
    } catch (e) {
      setMockResult({ ok: false, text: e instanceof Error ? e.message : 'Request failed' })
    }
  }

  return (
    <AppShell
      page="/operator"
      aside={
        <>
          <RailSection title="Session">
            <button className="btn btn--primary btn--block" onClick={() => command('arm')}>
              {running ? 'Restart session' : 'Start session'}
            </button>
            <div className="btnrow">
              <button className="btn" disabled={!running} onClick={() => command('retake')}>
                Retake
              </button>
              <button className="btn" disabled={state !== 'TimedOut'}
                      onClick={() => command('resume')}>
                Keep waiting
              </button>
            </div>
            <div className="btnrow">
              <button className="btn btn--go" disabled={state !== 'ReviewShots'}
                      onClick={() => command('accept')}>
                Accept
              </button>
              <button className="btn btn--stop" disabled={!running}
                      onClick={() => command('abort')}>
                Abort
              </button>
            </div>
          </RailSection>

          <RailSection title="Mock camera">
            <p className="hint">
              The app cannot fire the shutter. These stand in for the remote.
            </p>
            <div className="btngrid">
              {MOCK_MODES.map(([mode, label]) => (
                <button key={mode} className="btn btn--quiet" onClick={() => press(mode)}>
                  {label}
                </button>
              ))}
            </div>
            {mockResult && (
              <p className={mockResult.ok ? 'hint' : 'hint hint--bad'}>{mockResult.text}</p>
            )}
          </RailSection>
        </>
      }
    >
      {!connected && <p className="notice notice--warn">Reconnecting to the booth…</p>}
      {snapshot.message && <p className="notice">{snapshot.message}</p>}
      <DeliveryNotice delivery={delivery} />

      <Stage snapshot={snapshot} />

      <Panel
        title="Shots"
        actions={state === 'ReviewShots' && snapshot.isReordered ? (
          <button className="btn btn--quiet" onClick={() => command('order/reset')}>
            Back to capture order
          </button>
        ) : undefined}
      >
        <Filmstrip snapshot={snapshot} />
      </Panel>

      {state === 'ReviewShots' && layout && (
        <Panel
          title="Placement"
          actions={snapshot.hasMovedSlots ? (
            <button className="btn btn--quiet" onClick={() => command('slots/reset')}>
              Back to the template
            </button>
          ) : undefined}
        >
          <SlotNudger snapshot={snapshot} layout={layout} />
        </Panel>
      )}

      {snapshot.stripUrl && (
        <Panel
          title="Strip"
          actions={
            <>
              {snapshot.gifUrl && (
                <a className="btn btn--quiet" href={snapshot.gifUrl}
                   target="_blank" rel="noreferrer">GIF</a>
              )}
              <a className="btn btn--quiet" href={snapshot.stripUrl}
                 target="_blank" rel="noreferrer">Open full size</a>
            </>
          }
        >
          <div className="result">
            <img className="result__strip" src={snapshot.stripUrl} alt="Composed strip" />
            <dl className="facts">
              <dt>Saved to</dt>
              <dd>
                {/* The full path, not just the folder name. "Saved to
                    2026-09-13_2102_eylvz8" tells you nothing about where. */}
                <code className="path">
                  {outputFolder ? `${outputFolder}\\${snapshot.sessionFolder}` : snapshot.sessionFolder}
                </code>
              </dd>
              <dt>Contents</dt>
              <dd>{snapshot.shotCount} raw photos, the strip, and session.json</dd>
              <Delivery snapshot={snapshot} delivery={delivery} />
            </dl>
          </div>
        </Panel>
      )}

      {camera && (
        <Panel title="Watch folder">
          <code className="path">{camera.watchFolder}</code>
        </Panel>
      )}
    </AppShell>
  )
}

/**
 * Delivery trouble, at the top where the operator will see it.
 *
 * Local delivery cannot fail the way an upload could -- there is no sign-in to
 * revoke and no quota to exhaust -- but it can still point somewhere no phone
 * can reach, which looks identical from behind the booth. A loopback address is
 * the one case detection gets wrong silently, and it means every QR of the night
 * is dead.
 */
function DeliveryNotice({ delivery }: { delivery: DeliveryUpdate | null }) {
  if (!delivery) return null

  const unreachable =
    delivery.url.includes('127.0.0.1') || delivery.url.includes('localhost')

  if (!unreachable) return null

  return (
    <p className="notice notice--warn">
      The guest link points at this machine only ({delivery.url}) — no phone can
      reach it. Check the booth is on a network, then set the address in{' '}
      <a href="/diagnostics">Setup</a>.
    </p>
  )
}

/** Where this guest's photos got to, as extra rows on the strip facts. */
function Delivery({
  snapshot,
  delivery,
}: {
  snapshot: SessionSnapshot
  delivery: DeliveryUpdate | null
}) {
  const mine =
    delivery && delivery.sessionFolder === snapshot.sessionFolder ? delivery : null

  if (!mine) {
    return (
      <>
        <dt>Delivery</dt>
        <dd className="muted">Not published yet.</dd>
      </>
    )
  }

  return (
    <>
      <dt>Guest link</dt>
      <dd>
        <a href={mine.url} target="_blank" rel="noreferrer">{mine.url}</a>
      </dd>
    </>
  )
}

/**
 * Drag a photo's rectangle for this guest only.
 *
 * Deliberately separate from the template editor, which changes every strip from
 * now on. This changes one: a guest too tall for the middle slot is a problem
 * with tonight's third session, not with the template, and the two being the
 * same control is how an operator fixes one strip and quietly breaks forty.
 *
 * Sent on release rather than during the drag. A PUT per pointermove would be a
 * few hundred requests per nudge, each one broadcasting a new state to the iPad.
 */
function SlotNudger({
  snapshot,
  layout,
}: {
  snapshot: SessionSnapshot
  layout: Layout
}) {
  const stageRef = useRef<HTMLDivElement>(null)
  const [dragging, setDragging] = useState<{ slot: number; rect: SlotRect } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const slots = dragging
    ? layout.slots.map((s, i) => (i === dragging.slot ? dragging.rect : s))
    : layout.slots

  const startDrag =
    (i: number, mode: 'move' | 'resize') => (e: React.PointerEvent) => {
      e.preventDefault()
      e.stopPropagation()

      const stage = stageRef.current
      if (!stage) return

      const bounds = stage.getBoundingClientRect()
      const start = layout.slots[i]
      const originX = e.clientX
      const originY = e.clientY
      let latest = start

      const onMove = (ev: PointerEvent) => {
        const dx = (ev.clientX - originX) / bounds.width
        const dy = (ev.clientY - originY) / bounds.height

        latest =
          mode === 'move'
            ? {
                ...start,
                x: clamp(start.x + dx, 0, 1 - start.w),
                y: clamp(start.y + dy, 0, 1 - start.h),
              }
            : {
                ...start,
                w: clamp(start.w + dx, 0.02, 1 - start.x),
                h: clamp(start.h + dy, 0.02, 1 - start.y),
              }

        setDragging({ slot: i, rect: latest })
      }

      const onUp = () => {
        window.removeEventListener('pointermove', onMove)
        window.removeEventListener('pointerup', onUp)
        setDragging(null)
        void adjustSlot(i, latest).then(setError)
      }

      window.addEventListener('pointermove', onMove)
      window.addEventListener('pointerup', onUp)
    }

  return (
    <>
      {error && <p className="notice notice--warn">{error}</p>}

      <div
        ref={stageRef}
        className="nudger"
        style={{
          aspectRatio: `${layout.width} / ${layout.height}`,
          background: layout.background,
        }}
      >
        {slots.map((slot, i) => (
          <div
            key={i}
            className="slot"
            style={{
              left: `${slot.x * 100}%`,
              top: `${slot.y * 100}%`,
              width: `${slot.w * 100}%`,
              height: `${slot.h * 100}%`,
            }}
            onPointerDown={startDrag(i, 'move')}
          >
            {snapshot.photos[i] && (
              <img src={photoUrl(snapshot.photos[i])} alt="" draggable={false} />
            )}
            <span className="slot__index">{i + 1}</span>
            <span className="slot__handle" onPointerDown={startDrag(i, 'resize')} />
          </div>
        ))}
      </div>

      <p className="muted small">
        Drag to move, or the corner to resize. This guest only — the template is
        left alone.
      </p>
    </>
  )
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))

/**
 * The readout. Deliberately the largest thing on the screen: it is read from
 * across a booth, mid-conversation, not studied.
 */
function Stage({ snapshot }: { snapshot: SessionSnapshot }) {
  const counting = snapshot.state === 'Countdown'
  const remaining = useCountdown(counting ? snapshot.countdownEndsUtc : null)

  return (
    <section className={`stagecard stagecard--${snapshot.state}`}>
      <div className="stagecard__main">
        <p className="stagecard__state">{snapshot.state}</p>
        <h1 className="stagecard__headline">{HEADLINE[snapshot.state]}</h1>
      </div>

      <div className="stagecard__metric">
        {counting && remaining !== null ? (
          <>
            <span className="metric">{Math.max(0, remaining).toFixed(1)}</span>
            <span className="metric__unit">seconds — press on “1”</span>
          </>
        ) : (
          <>
            <span className="metric">
              {snapshot.capturedCount}<span className="metric__of">/{snapshot.shotCount}</span>
            </span>
            <span className="metric__unit">photos captured</span>
          </>
        )}
      </div>
    </section>
  )
}

/**
 * The shots, in the order they will be composited.
 *
 * That order is the operator's to change during review: the guest posed six
 * times, and which of those opens the strip is a judgement no software makes
 * well. Dragging is the mouse path; the arrows exist because a booth gets run
 * from a touchscreen, where HTML5 drag does nothing at all.
 *
 * Each thumbnail keeps its capture number, so "shot 4" is still shot 4 after it
 * has moved to the front -- without it, a rearranged strip is impossible to talk
 * about with the person standing next to you.
 */
function Filmstrip({ snapshot }: { snapshot: SessionSnapshot }) {
  const [dragging, setDragging] = useState<number | null>(null)
  const [over, setOver] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  const taken = snapshot.photos.length
  const canReorder = snapshot.state === 'ReviewShots' && taken > 1
  const canRetake = snapshot.state === 'ReviewShots'

  /** Reshoot one pose, leaving the rest of the strip alone. */
  const retake = async (slot: number) => {
    const r = await fetch(`/api/session/retake/${slot + 1}`, { method: 'POST' })
    if (!r.ok) {
      const body = await r.json().catch(() => ({}))
      setError(body.error ?? `HTTP ${r.status}`)
    }
  }

  /** Lift the shot at `from` out and drop it in at `to`, as a whole permutation. */
  const move = async (from: number, to: number) => {
    if (from === to || from < 0 || to < 0 || from >= taken || to >= taken) return
    const positions = Array.from({ length: taken }, (_, i) => i)
    positions.splice(to, 0, positions.splice(from, 1)[0])
    setError(await reorder(positions))
  }

  const endDrag = () => {
    setDragging(null)
    setOver(null)
  }

  return (
    <>
      {canReorder && (
        <p className="hint">
          Drag a shot, or use the arrows, to change where it lands on the strip.
          <strong> Retake</strong> reshoots just that pose and puts it back in the
          same place.
        </p>
      )}
      {snapshot.retakingSlot !== null && (
        <p className="notice">
          Retaking photo {snapshot.retakingSlot + 1} — the others are kept.
        </p>
      )}
      {error && <p className="hint hint--bad">{error}</p>}

      <div className="filmrow">
        {Array.from({ length: snapshot.shotCount }).map((_, i) => {
          const photo = snapshot.photos[i]
          if (!photo) {
            return (
              <figure key={`empty-${i}`} className="frame frame--empty">
                <span>{i + 1}</span>
              </figure>
            )
          }

          const shot = (snapshot.order[i] ?? i) + 1
          const classes = [
            'frame',
            canReorder ? 'frame--movable' : '',
            dragging === i ? 'frame--dragging' : '',
            over === i && dragging !== i ? 'frame--over' : '',
          ].filter(Boolean).join(' ')

          return (
            <figure
              key={photo.fileName}
              className={classes}
              draggable={canReorder}
              onDragStart={() => setDragging(i)}
              onDragEnd={endDrag}
              onDragOver={(e) => {
                if (!canReorder || dragging === null) return
                e.preventDefault()
                setOver(i)
              }}
              onDrop={(e) => {
                e.preventDefault()
                if (dragging !== null) void move(dragging, i)
                endDrag()
              }}
            >
              <img src={photoUrl(photo)} alt={`Shot ${shot}`} />

              {canReorder && (
                <div className="frame__move">
                  <button className="btn btn--icon" disabled={i === 0}
                          title={`Move shot ${shot} earlier`}
                          onClick={() => void move(i, i - 1)}>‹</button>
                  <span className="frame__slot">slot {i + 1}</span>
                  <button className="btn btn--icon" disabled={i === taken - 1}
                          title={`Move shot ${shot} later`}
                          onClick={() => void move(i, i + 1)}>›</button>
                </div>
              )}

              {canRetake && (
                <button className="btn btn--block frame__retake"
                        onClick={() => void retake(i)}>
                  Retake this one
                </button>
              )}

              <figcaption>
                <span className="frame__shot">shot {shot}</span>
                {photo.fileName}
              </figcaption>
            </figure>
          )
        })}
      </div>
    </>
  )
}
