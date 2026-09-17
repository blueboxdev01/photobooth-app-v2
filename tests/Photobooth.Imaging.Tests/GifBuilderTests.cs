using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Photobooth.Imaging.Tests;

/// <summary>
/// The looping animation a guest gets alongside their strip.
///
/// The GIF is a bonus, and most of what is worth pinning here is that it behaves
/// like one: a photo that will not decode, or no photos at all, has to cost the
/// animation rather than the session.
/// </summary>
public sealed class GifBuilderTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"pb-gif-{Guid.NewGuid():N}");

    private readonly GifBuilder _builder = new(NullLogger<GifBuilder>.Instance);

    public GifBuilderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A 3:2 frame, like the R50's, so the crop has something to trim.</summary>
    private string WritePhoto(string name, int width = 900, int height = 600)
    {
        using var image = new Image<Rgba32>(width, height);
        var path = Path.Combine(_folder, name);
        image.SaveAsJpeg(path);
        return path;
    }

    private string Output(string name = "animation.gif") => Path.Combine(_folder, name);

    [Fact]
    public void It_writes_one_frame_per_photo()
    {
        var photos = new[] { WritePhoto("a.jpg"), WritePhoto("b.jpg"), WritePhoto("c.jpg") };
        var output = Output();

        var ok = _builder.Build(photos, 4.0 / 3.0, output);

        Assert.True(ok);

        using var gif = Image.Load(output);
        Assert.Equal(3, gif.Frames.Count);
    }

    /// <summary>
    /// GIF's default is to play once, which on a phone means the guest watches it
    /// animate as it loads and never again.
    /// </summary>
    [Fact]
    public void It_loops_forever()
    {
        var output = Output();

        _builder.Build([WritePhoto("a.jpg"), WritePhoto("b.jpg")], 1, output);

        using var gif = Image.Load(output);
        Assert.Equal(0, gif.Metadata.GetGifMetadata().RepeatCount);
    }

    [Fact]
    public void Every_frame_carries_a_delay()
    {
        var output = Output();

        _builder.Build([WritePhoto("a.jpg"), WritePhoto("b.jpg")], 1, output);

        using var gif = Image.Load(output);

        Assert.All(
            gif.Frames,
            frame => Assert.Equal(
                GifBuilder.FrameDelayCentiseconds,
                frame.Metadata.GetGifMetadata().FrameDelay));
    }

    /// <summary>
    /// The frames are cropped to the slot's shape, not the photo's, so the guest
    /// is framed in the animation the way they are framed on the strip.
    /// </summary>
    [Theory]
    [InlineData(4.0 / 3.0)]
    [InlineData(1.0)]
    [InlineData(9.0 / 16.0)]
    public void Frames_take_the_slot_shape_not_the_photos(double aspect)
    {
        var output = Output();

        _builder.Build([WritePhoto("a.jpg")], aspect, output);

        using var gif = Image.Load(output);

        // Within a pixel, since both edges are rounded to even numbers.
        Assert.InRange(gif.Width / (double)gif.Height, aspect - 0.02, aspect + 0.02);
        Assert.Equal(GifBuilder.LongEdge, Math.Max(gif.Width, gif.Height));
    }

    /// <summary>
    /// Odd dimensions break some decoders and most video converters, and a guest
    /// turning their GIF into a Reel should not be the one to discover that.
    /// </summary>
    [Theory]
    [InlineData(4.0 / 3.0)]
    [InlineData(1.0)]
    [InlineData(16.0 / 9.0)]
    [InlineData(0.5625)]
    [InlineData(3.0)]
    public void Both_edges_are_even(double aspect)
    {
        var (width, height) = GifBuilder.FrameSize(aspect);

        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }

    /// <summary>
    /// A zero-height slot would otherwise produce an infinite aspect, a
    /// zero-width image, and an exception from deep inside the imaging library.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_nonsense_aspect_falls_back_to_square(double aspect)
    {
        var (width, height) = GifBuilder.FrameSize(aspect);

        Assert.Equal(width, height);
        Assert.True(width > 0);
    }

    [Fact]
    public void No_photos_means_no_gif_rather_than_an_empty_file()
    {
        var output = Output();

        var ok = _builder.Build([], 1, output);

        Assert.False(ok);
        Assert.False(File.Exists(output));
    }

    /// <summary>One bad photo costs its own frame, not the whole animation.</summary>
    [Fact]
    public void An_undecodable_photo_is_skipped_and_the_rest_still_animate()
    {
        var broken = Path.Combine(_folder, "broken.jpg");
        File.WriteAllText(broken, "not a jpeg");

        var output = Output();

        var ok = _builder.Build(
            [WritePhoto("a.jpg"), broken, WritePhoto("c.jpg")], 1, output);

        Assert.True(ok);

        using var gif = Image.Load(output);
        Assert.Equal(2, gif.Frames.Count);
    }

    /// <summary>
    /// Every photo being unreadable must report failure rather than leave a
    /// zero-frame file that fails silently on a guest's phone.
    /// </summary>
    [Fact]
    public void All_photos_unreadable_writes_nothing()
    {
        var broken = Path.Combine(_folder, "broken.jpg");
        File.WriteAllText(broken, "not a jpeg");

        var ok = _builder.Build([broken], 1, Output());

        Assert.False(ok);
        Assert.False(File.Exists(Output()));
    }
}
