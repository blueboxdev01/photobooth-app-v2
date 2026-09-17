namespace Photobooth.Core;

/// <summary>
/// Where a session currently is.
///
/// Note what is missing: there is no "selecting" state. Every photo captured
/// goes on the strip, and the template's slot count decides how many that is.
/// There is also no "uploading" state. Delivery is local: the files are on disk
/// before the session ends, so there is never a moment where a guest is waiting
/// on a transfer.
/// </summary>
public enum SessionState
{
    /// <summary>Between guests. The attract screen.</summary>
    Idle,

    /// <summary>
    /// Advisory countdown before a pose. Advisory because the app cannot fire the
    /// shutter -- the guest sees "3-2-1" and the operator presses the remote, and
    /// the two can drift apart.
    /// </summary>
    Countdown,

    /// <summary>Waiting for the next photo to land in the watch folder.</summary>
    Collecting,

    /// <summary>
    /// No photo arrived within the window. With an external trigger the app cannot
    /// tell "not pressed yet" from "camera asleep", so it says so rather than
    /// hanging.
    /// </summary>
    TimedOut,

    /// <summary>All shots captured; guest and operator look them over.</summary>
    ReviewShots,

    /// <summary>Building the strip. Wired up in M4.</summary>
    Composing,

    /// <summary>QR on the guest screen.</summary>
    ShowQr,

    /// <summary>Finished. Returns to Idle when the operator starts the next guest.</summary>
    Done,
}
