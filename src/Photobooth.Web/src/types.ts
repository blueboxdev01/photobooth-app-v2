export type SessionState =
  | 'Idle'
  | 'Countdown'
  | 'Collecting'
  | 'TimedOut'
  | 'ReviewShots'
  | 'Composing'
  | 'ShowQr'
  | 'Done'

export interface CapturedPhoto {
  filePath: string
  fileName: string
  sizeBytes: number
  detectedAtUtc: string
}

export interface SessionSnapshot {
  state: SessionState
  shotCount: number
  photos: CapturedPhoto[]
  /**
   * For each entry in `photos`, the 0-based position it was captured in. After
   * dragging the fourth shot to the front of six this is [3, 0, 1, 2, 4, 5], so a
   * thumbnail can still be labelled with the pose it actually is.
   */
  order: number[]
  /** True once the shots have been rearranged, so the console can offer to undo it. */
  isReordered: boolean
  /** Absolute instants, so the browser ticks the countdown locally. */
  countdownEndsUtc: string | null
  timeoutAtUtc: string | null
  startedUtc: string | null
  message: string | null
  capturedCount: number
  currentShot: number
  /**
   * The strip position currently being reshot, 0-based, when one is. Lets both
   * screens name the pose instead of showing a bare countdown.
   */
  retakingSlot: number | null
  /** Set once the strip is composed and archived. */
  stripUrl: string | null
  sessionFolder: string | null
}

/**
 * Where the finished session can be collected.
 *
 * Still separate from the session snapshot rather than a field on it: a guest
 * reading the QR outlasts the session it came from, because the operator can
 * arm the next one while they are still scanning. `sessionFolder` is how a
 * screen tells whether this is the session it is showing.
 */
export interface DeliveryUpdate {
  sessionFolder: string
  /** Absolute, for the QR. Points at this laptop on the booth network. */
  url: string
  /** Relative, for the booth's own screens, which are already on that origin. */
  qrUrl: string
}

export interface CameraInfo {
  status: 'Disconnected' | 'Ready' | 'Faulted'
  canTrigger: boolean
  watchFolder: string
}

export const photoUrl = (p: CapturedPhoto) =>
  `/api/photos/${encodeURIComponent(p.fileName)}`
