import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { usePeek } from './useSession'
import type { CapturedPhoto, SessionSnapshot, SessionState } from './types'

/**
 * Holding the shot just taken on the guest screen.
 *
 * The interesting cases are all about *which* photo is new. A retake replaces a
 * shot in place rather than appending one, so the obvious implementations --
 * watch the count, or take the last entry -- both show the wrong pose, and both
 * look completely fine until somebody retakes photo two at an event.
 */

const photo = (fileName: string): CapturedPhoto => ({
  filePath: `C:\\watch\\${fileName}`,
  fileName,
  sizeBytes: 1024,
  detectedAtUtc: new Date().toISOString(),
})

const snapshot = (
  state: SessionState,
  photos: CapturedPhoto[],
): SessionSnapshot =>
  ({
    state,
    shotCount: 3,
    photos,
    order: photos.map((_, i) => i),
    countdownEndsUtc: null,
    timeoutAtUtc: null,
    startedUtc: null,
    message: null,
    stripUrl: null,
    sessionFolder: null,
    retakingSlot: null,
    capturedCount: photos.length,
    currentShot: photos.length + 1,
    isReordered: false,
  }) as unknown as SessionSnapshot

describe('usePeek', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('holds a newly arrived shot', () => {
    const { result, rerender } = renderHook(
      ({ s }: { s: SessionSnapshot }) => usePeek(s),
      { initialProps: { s: snapshot('Collecting', []) } },
    )

    expect(result.current).toBeNull()

    rerender({ s: snapshot('Collecting', [photo('IMG_0001.JPG')]) })

    expect(result.current?.fileName).toBe('IMG_0001.JPG')
  })

  it('lets go after the hold', () => {
    const { result, rerender } = renderHook(
      ({ s }: { s: SessionSnapshot }) => usePeek(s, 2000),
      { initialProps: { s: snapshot('Collecting', []) } },
    )

    rerender({ s: snapshot('Collecting', [photo('IMG_0001.JPG')]) })
    expect(result.current).not.toBeNull()

    act(() => void vi.advanceTimersByTime(2000))

    expect(result.current).toBeNull()
  })

  /**
   * The case the obvious implementations get wrong. Redoing pose two leaves the
   * count at three and puts the replacement at index one, so anything watching
   * `photos.length` peeks at nothing and anything taking `photos.at(-1)` peeks
   * at pose three -- a photo the guest has already seen.
   */
  it('holds the replacement when a middle shot is retaken', () => {
    const before = [photo('IMG_0001.JPG'), photo('IMG_0002.JPG'), photo('IMG_0003.JPG')]

    const { result, rerender } = renderHook(
      ({ s }: { s: SessionSnapshot }) => usePeek(s),
      { initialProps: { s: snapshot('Collecting', []) } },
    )

    rerender({ s: snapshot('Collecting', before) })
    act(() => void vi.advanceTimersByTime(5000))
    expect(result.current).toBeNull()

    const after = [before[0], photo('IMG_0004.JPG'), before[2]]
    rerender({ s: snapshot('Collecting', after) })

    expect(result.current?.fileName).toBe('IMG_0004.JPG')
  })

  /**
   * Reloading the iPad mid-session must not flash a pose from earlier as though
   * it had just been taken.
   */
  it('does not peek at photos that were already there on the first snapshot', () => {
    const { result } = renderHook(() =>
      usePeek(snapshot('Collecting', [photo('IMG_0001.JPG'), photo('IMG_0002.JPG')])),
    )

    expect(result.current).toBeNull()
  })

  /**
   * The operator can start the next countdown inside the hold. A stale photo
   * over a live countdown is worse than no peek at all -- the guest is watching
   * themselves a moment ago while being counted in.
   */
  it('gives way when the booth moves on', () => {
    const { result, rerender } = renderHook(
      ({ s }: { s: SessionSnapshot }) => usePeek(s),
      { initialProps: { s: snapshot('Collecting', []) } },
    )

    rerender({ s: snapshot('Collecting', [photo('IMG_0001.JPG')]) })
    expect(result.current).not.toBeNull()

    rerender({ s: snapshot('Countdown', [photo('IMG_0001.JPG')]) })

    expect(result.current).toBeNull()
  })

  /**
   * Between guests everything is forgotten, so the next session's first shot
   * counts as new. Without this the second guest of the night gets no peek at
   * all if the camera reuses a file name.
   */
  it('forgets the session once the booth returns to idle', () => {
    const shot = [photo('IMG_0001.JPG')]

    const { result, rerender } = renderHook(
      ({ s }: { s: SessionSnapshot }) => usePeek(s),
      { initialProps: { s: snapshot('Collecting', []) } },
    )

    rerender({ s: snapshot('Collecting', shot) })
    act(() => void vi.advanceTimersByTime(5000))

    rerender({ s: snapshot('Idle', []) })
    rerender({ s: snapshot('Collecting', []) })
    rerender({ s: snapshot('Collecting', shot) })

    expect(result.current?.fileName).toBe('IMG_0001.JPG')
  })

  it('survives having no snapshot at all', () => {
    const { result } = renderHook(() => usePeek(null))

    expect(result.current).toBeNull()
  })
})
