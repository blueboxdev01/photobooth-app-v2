using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Photobooth.Imaging;

/// <summary>
/// The session's shots as a looping animation.
///
/// Deliberately *not* the strip in motion: it is the photos themselves, cropped
/// the way the strip crops them, one frame each. A guest who wants the layout
/// already has the strip; what a GIF is for is the moment between poses, which
/// is the part a still cannot carry.
///
/// Frames are cropped to the strip's slot shape rather than the photo's own, so
/// anyone who posed to the guide is framed in the GIF the way they are framed on
/// the strip -- rather than keeping their shoulders on one and losing them on
/// the other.
///
/// This is the one piece of imaging not done with SkiaSharp. Skia can decode an
/// animated GIF but cannot write one -- its only animation encoder is WebP --
/// and a WebP is not the thing guests asked for, however much better a format it
/// is. So ImageSharp handles the GIF and nothing else.
/// </summary>
public sealed class GifBuilder(ILogger<GifBuilder> logger)
{
    /// <summary>
    /// Long edge of the output, in pixels.
    ///
    /// GIF is an indexed-colour format from 1989 and pays for every pixel: a
    /// full-size frame runs to megabytes, and the guest collecting this is on a
    /// phone. Around 600 is large enough to look deliberate in a message thread
    /// and small enough to arrive at once.
    /// </summary>
    public const int LongEdge = 600;

    /// <summary>
    /// How long each pose is held, in hundredths of a second -- GIF's own unit.
    ///
    /// Note that browsers silently round delays under about 2 up to 10, so there
    /// is no point going faster than 50ms however much a value below it looks
    /// like it should work.
    /// </summary>
    public const int FrameDelayCentiseconds = 40;

    /// <summary>
    /// Writes a looping GIF of <paramref name="photoPaths"/>.
    /// </summary>
    /// <param name="aspect">Width over height per frame, normally the strip's first slot.</param>
    /// <returns>
    /// False when there was nothing to write. The GIF is an extra: a session
    /// whose strip and photos are safe on disk must never be reported as failed
    /// because a bonus could not be produced.
    /// </returns>
    public bool Build(IReadOnlyList<string> photoPaths, double aspect, string outputPath)
    {
        if (photoPaths.Count == 0)
        {
            logger.LogWarning("No photos to animate; skipping the GIF.");
            return false;
        }

        var (width, height) = FrameSize(aspect);

        Image<Rgba32>? animation = null;

        try
        {
            foreach (var path in photoPaths)
            {
                Image<Rgba32> frame;

                try
                {
                    frame = LoadFrame(path, width, height, aspect);
                }
                catch (Exception ex)
                {
                    // One unreadable photo should cost that frame, not the whole
                    // animation -- the strip may well have rendered it fine.
                    logger.LogWarning(ex, "Could not read {Photo} for the GIF.", path);
                    continue;
                }

                if (animation is null)
                {
                    animation = frame;
                    Configure(animation.Frames.RootFrame.Metadata);
                    continue;
                }

                using (frame)
                {
                    var added = animation.Frames.AddFrame(frame.Frames.RootFrame);
                    Configure(added.Metadata);
                }
            }

            if (animation is null)
            {
                logger.LogWarning("No frames could be read; the GIF was not written.");
                return false;
            }

            // Loop forever. GIF's default is to play once, which on a phone means
            // the guest sees it animate as it loads and never again.
            animation.Metadata.GetGifMetadata().RepeatCount = 0;

            animation.Save(outputPath, new GifEncoder());

            logger.LogInformation(
                "Wrote a {Width}x{Height} GIF of {Count} frames.",
                width, height, animation.Frames.Count);

            return true;
        }
        finally
        {
            animation?.Dispose();
        }
    }

    private static void Configure(SixLabors.ImageSharp.Metadata.ImageFrameMetadata metadata) =>
        metadata.GetGifMetadata().FrameDelay = FrameDelayCentiseconds;

    private static Image<Rgba32> LoadFrame(string path, int width, int height, double aspect)
    {
        var image = Image.Load<Rgba32>(path);

        try
        {
            // The same centre-crop the strip uses, from the same function, so the
            // two cannot drift into framing the guest differently.
            var crop = StripCompositor.CoverCrop(image.Width, image.Height, (float)aspect);

            var rectangle = new Rectangle(
                (int)Math.Round(crop.Left),
                (int)Math.Round(crop.Top),
                Math.Max(1, (int)Math.Round(crop.Width)),
                Math.Max(1, (int)Math.Round(crop.Height)));

            // Rounding can push the rectangle a pixel past the edge on an odd-sized
            // source, which ImageSharp rejects outright.
            rectangle = Rectangle.Intersect(rectangle, image.Bounds);

            image.Mutate(x => x
                .Crop(rectangle)
                .Resize(width, height));

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Frame dimensions for an aspect, with the long edge at <see cref="LongEdge"/>.
    ///
    /// Both edges are forced even, because some decoders and most video
    /// converters choke on odd dimensions -- and a guest turning their GIF into a
    /// Reel should not be the one to find that out.
    /// </summary>
    internal static (int Width, int Height) FrameSize(double aspect)
    {
        // A nonsense aspect -- zero, negative, or infinite from a zero-height
        // slot -- would otherwise produce a zero-width image and throw. Square is
        // a reasonable fallback and obviously wrong if it ever appears.
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
            aspect = 1;
        }

        var width = aspect >= 1 ? LongEdge : LongEdge * aspect;
        var height = aspect >= 1 ? LongEdge / aspect : LongEdge;

        return (Even(width), Even(height));
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}
