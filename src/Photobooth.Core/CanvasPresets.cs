namespace Photobooth.Core;

/// <summary>A named output size, so nobody has to remember that 2×6 at 300 DPI is 600×1800.</summary>
public sealed record CanvasPreset(string Id, string Label, TemplateCanvas Canvas)
{
    public TemplateOrientation Orientation => SlotLayout.OrientationOf(Canvas);

    /// <summary>
    /// How the size is described in the UI: inches for something that could be
    /// printed, pixels for something that could not. Telling an operator a story
    /// is "15×26.7 in" is worse than telling them nothing.
    /// </summary>
    public string Size => Canvas.Dpi >= 200
        ? $"{Canvas.WidthInches:0.#}×{Canvas.HeightInches:0.#} in"
        : $"{Canvas.Width}×{Canvas.Height} px";
}

/// <summary>
/// The output sizes the booth offers.
///
/// Two families, and the difference is the DPI rather than the shape.
///
/// The print sizes are at 300 DPI, which is what photo printing expects and what
/// the strip's JFIF header claims: a template exists so the thing comes out of a
/// printer the right physical size, and 2x6 in inches is what a guest recognises
/// as a photobooth strip whether or not it is ever printed.
///
/// The social sizes are at 72 DPI and named in pixels, because they are never
/// printed and their physical size is meaningless. 1080 wide is what Instagram
/// serves; claiming 300 DPI for them would make a 1:1 post report itself as a
/// 3.6 inch print, which is true of nothing.
/// </summary>
public static class CanvasPresets
{
    public const string DefaultId = "strip-2x6";

    public static IReadOnlyList<CanvasPreset> All { get; } =
    [
        new("strip-2x6", "Photo strip 2×6", new TemplateCanvas(600, 1800)),
        new("portrait-4x6", "Portrait 4×6", new TemplateCanvas(1200, 1800)),
        new("portrait-5x7", "Portrait 5×7", new TemplateCanvas(1500, 2100)),
        new("landscape-6x4", "Landscape 6×4", new TemplateCanvas(1800, 1200)),
        new("landscape-7x5", "Landscape 7×5", new TemplateCanvas(2100, 1500)),

        new("square-1x1", "Square (social)", new TemplateCanvas(1080, 1080, Screen)),
        new("story-9x16", "Story 9×16", new TemplateCanvas(1080, 1920, Screen)),
    ];

    /// <summary>
    /// DPI for sizes that only ever exist on a screen. Not 300, so their reported
    /// inches stay honest -- see the note above.
    /// </summary>
    private const int Screen = 72;

    public static CanvasPreset Default => Find(DefaultId)!;

    public static CanvasPreset? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(
            p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The preset matching a canvas exactly, if there is one. Lets the UI show
    /// "Photo strip 2×6" rather than raw pixels for a size it recognises, while
    /// still allowing hand-edited dimensions.
    /// </summary>
    public static CanvasPreset? Matching(TemplateCanvas canvas) =>
        All.FirstOrDefault(p =>
            p.Canvas.Width == canvas.Width
            && p.Canvas.Height == canvas.Height
            && p.Canvas.Dpi == canvas.Dpi);
}
