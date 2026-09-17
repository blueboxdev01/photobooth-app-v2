namespace Photobooth.Core;

public enum TemplateOrientation
{
    /// <summary>Taller than wide — the classic strip.</summary>
    Portrait,

    /// <summary>Wider than tall.</summary>
    Landscape,
}

/// <summary>
/// Spacing for generated layouts, as fractions rather than pixels so a template
/// looks the same at any output size.
/// </summary>
/// <param name="Margin">
/// Border around the whole block of photos, as a fraction of the canvas's
/// **shorter** side. Using the shorter side keeps the border visually even
/// instead of stretching with the long axis.
/// </param>
/// <param name="Gap">Space between photos, also relative to the shorter side.</param>
/// <param name="Footer">
/// Band reserved at the bottom for branding, as a fraction of canvas height.
/// This is what a strip's logo sits in, and why the slots are not simply the
/// whole canvas divided by the photo count.
/// </param>
public sealed record LayoutOptions(double Margin = 0.035, double Gap = 0.03, double Footer = 0.22)
{
    /// <summary>
    /// Sensible defaults per orientation. A portrait strip wants a generous
    /// footer; a landscape print usually does not, because there is no long tail
    /// of empty canvas to fill.
    /// </summary>
    public static LayoutOptions For(TemplateOrientation orientation) => orientation switch
    {
        TemplateOrientation.Portrait => new LayoutOptions(Footer: 0.22),
        _ => new LayoutOptions(Footer: 0.06),
    };

    /// <summary>
    /// Defaults for an actual canvas, which is what callers want -- the footer
    /// that suits a 2x6 strip is wrong for the two social sizes, and orientation
    /// alone cannot tell them apart.
    ///
    /// A 1:1 square is Portrait by the width-versus-height test, and a strip's
    /// 22% footer would spend nearly a quarter of an Instagram post on branding.
    /// A 9:16 story is portrait too but far less elongated than a 2x6, so it
    /// wants something between the two.
    /// </summary>
    public static LayoutOptions For(TemplateCanvas canvas)
    {
        var ratio = canvas.Height / (double)canvas.Width;

        return ratio switch
        {
            // Square-ish, give or take a nudge.
            < 1.15 => For(SlotLayout.OrientationOf(canvas)) with { Footer = 0.10 },

            // Story-shaped: 9:16 is 1.78, a 4x6 portrait is 1.5, and a 2x6 strip
            // is 3.0. Anything under 2.2 is not a strip and should not wear a
            // strip's footer.
            < 2.2 => For(SlotLayout.OrientationOf(canvas)) with { Footer = 0.14 },

            _ => For(SlotLayout.OrientationOf(canvas)),
        };
    }
}

/// <summary>
/// Places N photos evenly on a canvas.
///
/// Exists so the photo count can be a setting rather than a consequence of
/// hand-dragging rectangles: pick a number, get a layout. The result is ordinary
/// <see cref="TemplateSlot"/> values, so a generated layout is still editable by
/// hand afterwards — auto-arrange is a starting point, not a cage.
/// </summary>
public static class SlotLayout
{
    /// <summary>The photo counts a strip can sensibly hold.</summary>
    public const int MinPhotos = 1;
    public const int MaxPhotos = 8;

    public static TemplateOrientation OrientationOf(TemplateCanvas canvas) =>
        canvas.Width > canvas.Height ? TemplateOrientation.Landscape : TemplateOrientation.Portrait;

    /// <summary>
    /// Rows and columns for a given count and orientation.
    ///
    /// A portrait strip is a single column — that is what makes it a strip.
    /// Landscape runs along a row while the photos stay reasonably large, then
    /// switches to a grid rather than shrinking them to slivers.
    /// </summary>
    public static (int Rows, int Columns) Grid(int photoCount, TemplateOrientation orientation)
    {
        var count = Math.Clamp(photoCount, MinPhotos, MaxPhotos);

        if (orientation == TemplateOrientation.Portrait)
        {
            // Beyond four, a single column leaves each photo a sliver, so pair
            // them up rather than let faces disappear.
            return count <= 4 ? (count, 1) : ((count + 1) / 2, 2);
        }

        return count <= 3
            ? (1, count)
            : (2, (count + 1) / 2);
    }

    /// <summary>
    /// Rows and columns for an actual canvas.
    ///
    /// A 1:1 square is Portrait by the width-versus-height test, so on
    /// orientation alone it inherits the strip's single column -- and four photos
    /// stacked down a square give each one a 5:1 letterbox with the tops of
    /// everybody's heads outside it. The same is true, less severely, of a 9:16
    /// story and a 4x6 postcard.
    ///
    /// So elongation decides, not orientation. Only a canvas actually shaped like
    /// a strip gets a strip's column; anything squarer gets a balanced grid.
    /// </summary>
    public static (int Rows, int Columns) Grid(int photoCount, TemplateCanvas canvas)
    {
        var count = Math.Clamp(photoCount, MinPhotos, MaxPhotos);
        var ratio = canvas.Height / (double)canvas.Width;

        // Landscape keeps its own rules: ratio below 1 is wider than tall, where
        // running along a row is the whole point.
        var portrait = ratio >= 1;

        // 2.2 sits between a 4x6 postcard at 1.5 and a 2x6 strip at 3.0.
        var strippy = ratio >= 2.2;

        // A square is squeezed by even two photos in a column; a taller canvas
        // copes until there are four.
        var threshold = ratio < 1.15 ? 2 : 4;

        if (portrait && !strippy && count >= threshold)
        {
            var columns = (int)Math.Ceiling(Math.Sqrt(count));
            return ((int)Math.Ceiling(count / (double)columns), columns);
        }

        return Grid(count, OrientationOf(canvas));
    }

    /// <summary>
    /// Evenly spaced slots for <paramref name="photoCount"/> photos.
    ///
    /// A final row holding fewer photos than the others is centred, so five
    /// photos in a 2×3 grid look deliberate rather than truncated.
    /// </summary>
    public static IReadOnlyList<TemplateSlot> Arrange(
        int photoCount,
        TemplateCanvas canvas,
        LayoutOptions? options = null,
        SlotFit fit = SlotFit.Cover)
    {
        var count = Math.Clamp(photoCount, MinPhotos, MaxPhotos);
        var orientation = OrientationOf(canvas);
        var layout = options ?? LayoutOptions.For(canvas);
        var (rows, columns) = Grid(count, canvas);

        // Worked in pixels, then normalised. Margins and gaps expressed directly
        // as fractions of each axis would come out visibly wider than tall on a
        // 600x1800 canvas.
        double width = canvas.Width;
        double height = canvas.Height;
        var shorter = Math.Min(width, height);

        var margin = layout.Margin * shorter;
        var gap = layout.Gap * shorter;
        var footer = layout.Footer * height;

        var availableWidth = width - (2 * margin);
        var availableHeight = height - (2 * margin) - footer;

        var cellWidth = (availableWidth - (gap * (columns - 1))) / columns;
        var cellHeight = (availableHeight - (gap * (rows - 1))) / rows;

        if (cellWidth <= 0 || cellHeight <= 0)
        {
            // Margins and gaps have eaten the canvas. Better an ugly layout than
            // negative-sized slots that would throw deep inside the compositor.
            return [new TemplateSlot(0, 0, 1, 1, fit)];
        }

        var slots = new List<TemplateSlot>(count);
        var placed = 0;

        for (var row = 0; row < rows && placed < count; row++)
        {
            var inThisRow = Math.Min(columns, count - placed);

            // Centre a short final row.
            var rowWidth = (inThisRow * cellWidth) + (gap * (inThisRow - 1));
            var rowLeft = margin + ((availableWidth - rowWidth) / 2);
            var top = margin + (row * (cellHeight + gap));

            for (var column = 0; column < inThisRow; column++)
            {
                var left = rowLeft + (column * (cellWidth + gap));
                slots.Add(new TemplateSlot(
                    left / width,
                    top / height,
                    cellWidth / width,
                    cellHeight / height,
                    fit));
                placed++;
            }
        }

        return slots;
    }
}
