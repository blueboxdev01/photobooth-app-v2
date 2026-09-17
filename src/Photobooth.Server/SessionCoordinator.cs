using Microsoft.AspNetCore.SignalR;
using Photobooth.Cameras;
using Photobooth.Core;
using Photobooth.Delivery;
using Photobooth.Imaging;

namespace Photobooth.Server;

/// <summary>
/// The only place the camera, the session engine and the browsers meet.
///
/// Keeping the wiring here means <see cref="SessionEngine"/> stays free of
/// filesystem and network concerns, and the camera stays unaware that sessions
/// exist. Each side is testable on its own.
/// </summary>
public sealed class SessionCoordinator : IHostedService
{
    private readonly WatchFolderCamera _camera;
    private readonly SessionEngine _engine;
    private readonly IHubContext<SessionHub> _hub;
    private readonly TimeProvider _time;
    private readonly DiagnosticsService _diagnostics;
    private readonly StripCompositor _compositor;
    private readonly GifBuilder _gifs;
    private readonly FileTemplateProvider _templates;
    private readonly SessionArchive _archive;
    private readonly ISessionPublisher _publisher;
    private readonly ILogger<SessionCoordinator> _logger;

    /// <summary>
    /// The session the guest screen is currently showing, so the operator page
    /// can be told which one the QR on screen belongs to.
    /// </summary>
    private string? _showing;

    // Generated when the session is armed so the archive folder and the eventual
    // QR link refer to the same session.
    private string _token = SessionArchive.NewToken();

    public SessionCoordinator(
        WatchFolderCamera camera,
        SessionEngine engine,
        IHubContext<SessionHub> hub,
        TimeProvider time,
        DiagnosticsService diagnostics,
        StripCompositor compositor,
        GifBuilder gifs,
        FileTemplateProvider templates,
        SessionArchive archive,
        ISessionPublisher publisher,
        ILogger<SessionCoordinator> logger)
    {
        _camera = camera;
        _engine = engine;
        _hub = hub;
        _time = time;
        _diagnostics = diagnostics;
        _compositor = compositor;
        _gifs = gifs;
        _templates = templates;
        _archive = archive;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _camera.PhotoArrived += OnPhotoArrived;
        _camera.StatusChanged += OnCameraStatus;
        _camera.IngestDecision += OnIngestDecision;
        _engine.Changed += OnSessionChanged;

        // Nothing already sitting in the folder counts until a session starts.
        _camera.AcceptFrom = _time.GetUtcNow();
        await _camera.ConnectAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _camera.PhotoArrived -= OnPhotoArrived;
        _camera.StatusChanged -= OnCameraStatus;
        _camera.IngestDecision -= OnIngestDecision;
        _engine.Changed -= OnSessionChanged;
        await _camera.DisposeAsync();
    }

    /// <summary>
    /// Start a session. The camera's stale-file cutoff moves to now, so a photo
    /// left over from the previous guest can never appear on this strip.
    /// </summary>
    public SessionSnapshot Arm()
    {
        _camera.ResetSeen();
        _camera.AcceptFrom = _time.GetUtcNow();
        _token = SessionArchive.NewToken();
        _showing = null;
        return _engine.Arm();
    }

    private void OnPhotoArrived(object? sender, PhotoArrivedEventArgs e)
    {
        if (!_engine.SubmitPhoto(e.Photo))
        {
            // Not an error: the operator may be testing the camera between guests.
            _logger.LogInformation(
                "{File} arrived outside a session and was not used.", e.Photo.FileName);
        }
    }

    private void OnIngestDecision(object? sender, IngestEvent e) => _diagnostics.Record(e);

    private void OnSessionChanged(object? sender, SessionSnapshot snapshot)
    {
        Broadcast(snapshot);

        if (snapshot.State == SessionState.Composing)
        {
            // Off the caller's thread: this decodes several 24 MP JPEGs and must
            // not block the hub callback that just delivered the state change.
            _ = Task.Run(() => ComposeAsync(snapshot));
        }
    }

    /// <summary>
    /// Builds the strip and writes the session to disk.
    ///
    /// Publishing is the last step and costs nothing: once the files are saved,
    /// the guest's link is just the session's token on the current base URL.
    /// </summary>
    private async Task ComposeAsync(SessionSnapshot snapshot)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"pb-strip-{Guid.NewGuid():N}.jpg");
        var tempGif = Path.Combine(Path.GetTempPath(), $"pb-gif-{Guid.NewGuid():N}.gif");

        try
        {
            // The session's own slots when the operator nudged any, otherwise the
            // template's. Taken once here so the strip, the GIF and the record all
            // describe the same layout even if something changes mid-compose.
            var template = _templates.Current with { Slots = _engine.EffectiveSlots };
            var photos = snapshot.Photos.Select(p => p.FilePath).ToList();

            await Task.Run(() => _compositor.Compose(
                template, photos, _templates.Folder, temp));

            // After the strip, never before, and never allowed to fail the
            // session: the guest's actual product is already rendered, and a
            // missing bonus must not cost them their photos.
            var gif = false;
            try
            {
                var slot = template.Slots[0];
                var aspect = (slot.W * template.Canvas.Width)
                             / (slot.H * template.Canvas.Height);

                gif = await Task.Run(() => _gifs.Build(photos, aspect, tempGif));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not build the GIF for this session.");
            }

            var record = _archive.Save(
                _token, template, snapshot.Photos, temp,
                snapshot.StartedUtc ?? _time.GetUtcNow(),
                gif ? tempGif : null);

            _showing = record.FolderName;

            _engine.CompleteComposing(
                $"/api/sessions/{record.FolderName}/{record.Strip}",
                record.FolderName,
                record.Gif is null
                    ? null
                    : $"/api/sessions/{record.FolderName}/{record.Gif}");

            BroadcastDelivery(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Composing the strip failed.");
            _engine.FailComposing(
                $"Could not build the strip: {ex.Message}. The photos are safe -- " +
                "press Accept to try again.");
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            try { File.Delete(tempGif); } catch { /* best effort */ }
        }
    }

    private void BroadcastDelivery(SessionRecord record)
    {
        var link = _publisher.Publish(record);

        _ = _hub.Clients.All.SendAsync(SessionHub.DeliveryMessage,
                new DeliveryUpdate(record.FolderName, link.Url, link.QrUrl))
            .ContinueWith(
                t => _logger.LogWarning(t.Exception, "Failed to push delivery state."),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Where the session currently on screen can be collected.</summary>
    public DeliveryUpdate? CurrentDelivery()
    {
        var record = _showing is null
            ? null
            : _archive.All().FirstOrDefault(r => r.FolderName == _showing);

        if (record is null)
        {
            return null;
        }

        var link = _publisher.Publish(record);
        return new DeliveryUpdate(record.FolderName, link.Url, link.QrUrl);
    }

    private void OnCameraStatus(object? sender, CameraStatusEventArgs e)
    {
        _logger.LogInformation("Camera {Status}: {Message}", e.Status, e.Message);
        Broadcast(_engine.Snapshot);
    }

    private void Broadcast(SessionSnapshot snapshot)
    {
        // Fire and forget: a slow or disconnected browser must never stall ingest.
        _ = _hub.Clients.All.SendAsync(SessionHub.StateMessage, snapshot)
            .ContinueWith(
                t => _logger.LogWarning(t.Exception, "Failed to push session state."),
                TaskContinuationOptions.OnlyOnFaulted);
    }
}

/// <summary>
/// What both screens are told about delivery: which session, and where a guest
/// collects it.
///
/// Still a separate message from <see cref="SessionSnapshot"/> rather than a
/// field on it, even though a local link is ready the instant the session ends.
/// A guest reading the QR outlasts the session it came from -- the operator can
/// arm the next one while they are still scanning -- so delivery has to survive
/// the session state going back to Idle.
/// </summary>
public sealed record DeliveryUpdate(string SessionFolder, string Url, string QrUrl);
