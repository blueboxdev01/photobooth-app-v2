using Photobooth.Core;

namespace Photobooth.Core.Tests;

/// <summary>
/// The output sizes the booth offers, and how each one wants its photos laid out.
///
/// The social sizes are the interesting ones. They are portrait by the naive
/// width-versus-height test but nothing like a 2×6 strip, and inheriting a
/// strip's layout would spend a quarter of an Instagram post on an empty footer.
/// </summary>
public class CanvasPresetTests
{
    [Theory]
    [InlineData("strip-2x6")]
    [InlineData("portrait-4x6")]
    [InlineData("square-1x1")]
    [InlineData("story-9x16")]
    public void The_sizes_the_booth_promises_are_all_present(string id) =>
        Assert.NotNull(CanvasPresets.Find(id));

    [Fact]
    public void The_social_sizes_are_the_pixel_dimensions_those_platforms_use()
    {
        Assert.Equal(new TemplateCanvas(1080, 1080, 72), CanvasPresets.Find("square-1x1")!.Canvas);
        Assert.Equal(new TemplateCanvas(1080, 1920, 72), CanvasPresets.Find("story-9x16")!.Canvas);
    }

    /// <summary>
    /// A screen size at 300 DPI would report itself as a 3.6 inch print, which is
    /// true of nothing and would be shown to an operator as fact.
    /// </summary>
    [Theory]
    [InlineData("square-1x1")]
    [InlineData("story-9x16")]
    public void Screen_sizes_are_described_in_pixels(string id)
    {
        var preset = CanvasPresets.Find(id)!;

        Assert.Contains("px", preset.Size);
        Assert.DoesNotContain("in", preset.Size);
    }

    [Theory]
    [InlineData("strip-2x6")]
    [InlineData("portrait-4x6")]
    [InlineData("landscape-6x4")]
    public void Print_sizes_are_described_in_inches(string id) =>
        Assert.Contains("in", CanvasPresets.Find(id)!.Size);

    [Fact]
    public void Every_preset_is_findable_by_its_own_dimensions()
    {
        foreach (var preset in CanvasPresets.All)
        {
            Assert.Equal(preset.Id, CanvasPresets.Matching(preset.Canvas)?.Id);
        }
    }

    [Fact]
    public void Ids_are_unique()
    {
        var ids = CanvasPresets.All.Select(p => p.Id.ToLowerInvariant()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // --- layout --------------------------------------------------------------

    /// <summary>
    /// The strip's generous footer is what makes it look like a photobooth strip.
    /// Nothing else should inherit it.
    /// </summary>
    [Fact]
    public void A_2x6_strip_keeps_its_generous_footer() =>
        Assert.Equal(0.22, LayoutOptions.For(CanvasPresets.Find("strip-2x6")!.Canvas).Footer);

    [Fact]
    public void A_square_does_not_wear_a_strips_footer()
    {
        var square = LayoutOptions.For(CanvasPresets.Find("square-1x1")!.Canvas);

        Assert.True(
            square.Footer < 0.15,
            $"a 1:1 post should not spend {square.Footer:P0} of itself on branding");
    }

    [Fact]
    public void A_story_sits_between_a_square_and_a_strip()
    {
        var square = LayoutOptions.For(CanvasPresets.Find("square-1x1")!.Canvas).Footer;
        var story = LayoutOptions.For(CanvasPresets.Find("story-9x16")!.Canvas).Footer;
        var strip = LayoutOptions.For(CanvasPresets.Find("strip-2x6")!.Canvas).Footer;

        Assert.InRange(story, square, strip);
    }

    /// <summary>
    /// Whatever the footer, the photos have to fit on the canvas -- a layout that
    /// runs off the edge composites to a cropped or empty slot.
    /// </summary>
    [Theory]
    [InlineData("strip-2x6", 3)]
    [InlineData("portrait-4x6", 4)]
    [InlineData("square-1x1", 4)]
    [InlineData("story-9x16", 3)]
    [InlineData("landscape-6x4", 2)]
    public void Generated_slots_stay_on_the_canvas(string id, int photos)
    {
        var slots = SlotLayout.Arrange(photos, CanvasPresets.Find(id)!.Canvas);

        Assert.Equal(photos, slots.Count);

        foreach (var slot in slots)
        {
            Assert.InRange(slot.X, 0, 1);
            Assert.InRange(slot.Y, 0, 1);
            Assert.InRange(slot.X + slot.W, 0, 1.0001);
            Assert.InRange(slot.Y + slot.H, 0, 1.0001);
            Assert.True(slot.W > 0 && slot.H > 0);
        }
    }

    /// <summary>
    /// The bug this caught: a 1:1 square is Portrait by the width-versus-height
    /// test, so on orientation alone it inherited the strip's single column --
    /// and four photos down a square gave each one a 5:1 letterbox. It showed up
    /// first in the GIF, which came out 600x120.
    /// </summary>
    [Fact]
    public void Four_photos_on_a_square_are_a_grid_not_a_column()
    {
        var (rows, columns) = SlotLayout.Grid(4, CanvasPresets.Find("square-1x1")!.Canvas);

        Assert.Equal((2, 2), (rows, columns));
    }

    [Fact]
    public void Four_photos_on_a_story_are_a_grid_too()
    {
        var (rows, columns) = SlotLayout.Grid(4, CanvasPresets.Find("story-9x16")!.Canvas);

        Assert.Equal((2, 2), (rows, columns));
    }

    /// <summary>
    /// A single column is what makes a strip a strip, and no amount of
    /// grid-balancing elsewhere may take it away.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_2x6_strip_keeps_its_single_column(int photos)
    {
        var (_, columns) = SlotLayout.Grid(photos, CanvasPresets.Find("strip-2x6")!.Canvas);

        Assert.Equal(1, columns);
    }

    /// <summary>Landscape runs along a row; that rule is untouched.</summary>
    [Fact]
    public void Landscape_still_runs_along_a_row()
    {
        var (rows, columns) = SlotLayout.Grid(3, CanvasPresets.Find("landscape-6x4")!.Canvas);

        Assert.Equal((1, 3), (rows, columns));
    }

    /// <summary>
    /// The shape of one photo is what the framing guide and the GIF are both cut
    /// from, so a layout that produces letterboxed slivers is wrong twice over.
    /// </summary>
    [Theory]
    [InlineData("square-1x1", 4)]
    [InlineData("story-9x16", 4)]
    [InlineData("portrait-4x6", 4)]
    [InlineData("strip-2x6", 3)]
    public void No_layout_produces_a_letterboxed_sliver(string id, int photos)
    {
        var canvas = CanvasPresets.Find(id)!.Canvas;
        var slots = SlotLayout.Arrange(photos, canvas);

        foreach (var slot in slots)
        {
            var aspect = slot.W * canvas.Width / (slot.H * canvas.Height);

            Assert.InRange(aspect, 0.4, 2.4);
        }
    }

    /// <summary>Two photos must never be drawn on top of each other.</summary>
    [Theory]
    [InlineData("square-1x1", 4)]
    [InlineData("story-9x16", 4)]
    [InlineData("strip-2x6", 3)]
    public void Generated_slots_do_not_overlap(string id, int photos)
    {
        var slots = SlotLayout.Arrange(photos, CanvasPresets.Find(id)!.Canvas);

        for (var a = 0; a < slots.Count; a++)
        {
            for (var b = a + 1; b < slots.Count; b++)
            {
                var overlaps =
                    slots[a].X < slots[b].X + slots[b].W - 0.0001 &&
                    slots[b].X < slots[a].X + slots[a].W - 0.0001 &&
                    slots[a].Y < slots[b].Y + slots[b].H - 0.0001 &&
                    slots[b].Y < slots[a].Y + slots[a].H - 0.0001;

                Assert.False(overlaps, $"slots {a + 1} and {b + 1} overlap on {id}");
            }
        }
    }
}
