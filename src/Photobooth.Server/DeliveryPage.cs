using System.IO.Compression;
using System.Net;
using System.Text;
using Photobooth.Delivery;

namespace Photobooth.Server;

/// <summary>
/// The page a guest's phone opens after scanning the QR.
///
/// Server-rendered HTML rather than the React app, for three reasons that all
/// point the same way. It is served over plain HTTP to phones that are not on
/// the booth's certificate, so it has to be self-contained; it must appear
/// instantly on a venue's network rather than after a 300 KB bundle; and it has
/// to work on whatever browser a guest happens to have, including one with
/// scripting off. There is nothing interactive here -- it is a list of files and
/// a way to save them.
/// </summary>
public static class DeliveryPage
{
    public static void MapDeliveryPage(this WebApplication app)
    {
        // Short on purpose: this is typed in by hand when a QR will not scan, and
        // read aloud across a noisy room more often than anyone expects.
        app.MapGet("/s/{token}", (string token, SessionArchive archive) =>
        {
            var record = archive.FindByToken(token);

            return record is null
                ? Results.Content(NotFound(), "text/html; charset=utf-8", Encoding.UTF8, 404)
                : Results.Content(Render(record), "text/html; charset=utf-8");
        });

        // Files hang off the token rather than the folder, so the guest-facing URL
        // never exposes a dated, sequential-looking folder name.
        app.MapGet("/s/{token}/f/{file}", (string token, string file, SessionArchive archive) =>
        {
            var record = archive.FindByToken(token);

            if (record is null || !Owns(record, file))
            {
                // Deliberately the same answer as a bad token. Distinguishing
                // "wrong session" from "no such file" tells someone probing which
                // of the two they got right.
                return Results.NotFound();
            }

            var path = Path.Combine(archive.FolderFor(record), file);

            return File.Exists(path)
                ? Results.File(path, ContentType(file), enableRangeProcessing: true)
                : Results.NotFound();
        });

        app.MapGet("/s/{token}/all.zip", (string token, SessionArchive archive) =>
        {
            var record = archive.FindByToken(token);
            if (record is null)
            {
                return Results.NotFound();
            }

            var folder = archive.FolderFor(record);
            var buffer = new MemoryStream();

            // Built in memory rather than streamed: a session is a handful of
            // JPEGs, and a temporary file would need cleaning up after a guest
            // who closed the tab halfway through.
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var name in Files(record))
                {
                    var path = Path.Combine(folder, name);
                    if (File.Exists(path))
                    {
                        zip.CreateEntryFromFile(path, name);
                    }
                }
            }

            buffer.Position = 0;

            // Named for the session rather than "all.zip", because it lands in a
            // downloads folder beside everything else the guest saved that night.
            return Results.File(
                buffer, "application/zip", $"photobooth-{record.FolderName}.zip");
        });
    }

    /// <summary>
    /// Whether this session actually holds the named file.
    ///
    /// An allow-list taken from the record, not a path check. Validating the
    /// shape of a name is how directory traversal gets through eventually;
    /// asking "is this one of the files we wrote?" cannot be tricked by encoding.
    /// </summary>
    private static bool Owns(SessionRecord record, string file) =>
        Files(record).Contains(file, StringComparer.Ordinal);

    private static IEnumerable<string> Files(SessionRecord record)
    {
        yield return record.Strip;

        if (record.Gif is not null)
        {
            yield return record.Gif;
        }

        foreach (var photo in record.Photos)
        {
            yield return photo;
        }
    }

    private static string ContentType(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };

    private static string Render(SessionRecord record)
    {
        var body = new StringBuilder();

        body.Append("<h1>Your photos</h1>");
        body.Append(
            "<p class=\"hint\">Press and hold any photo to save it, "
            + "or use the button below to get them all at once.</p>");

        body.Append(
            $"<a class=\"btn\" href=\"/s/{Url(record.Token)}/all.zip\">Download everything</a>");

        body.Append("<h2>The strip</h2>");
        body.Append(Figure(record.Token, record.Strip, "Your photo strip"));

        if (record.Gif is not null)
        {
            body.Append("<h2>Animated</h2>");
            body.Append(Figure(record.Token, record.Gif, "An animation of your photos"));
        }

        body.Append("<h2>Every shot</h2>");
        body.Append("<div class=\"grid\">");
        foreach (var photo in record.Photos)
        {
            body.Append(Figure(record.Token, photo, "One of your photos"));
        }
        body.Append("</div>");

        body.Append(
            $"<p class=\"foot\">Taken {record.CreatedUtc.ToLocalTime():d MMMM yyyy, HH:mm}. "
            + "This link works while the booth is running and on its network.</p>");

        return Document("Your photos", body.ToString());
    }

    private static string Figure(string token, string file, string alt)
    {
        var href = $"/s/{Url(token)}/f/{Url(file)}";

        return $"<figure><a href=\"{href}\" download><img src=\"{href}\" alt=\"{Html(alt)}\" "
               + "loading=\"lazy\"></a></figure>";
    }

    private static string NotFound() => Document(
        "Link not found",
        "<h1>We could not find those photos</h1>"
        + "<p class=\"hint\">The link may have been mistyped, or this booth may be "
        + "showing a different event. Ask at the booth and we can find them for you.</p>");

    /// <summary>
    /// One self-contained document. No external stylesheet, font or script: the
    /// booth's network usually has no route to the internet, so anything not
    /// inline would simply never arrive.
    /// </summary>
    private static string Document(string title, string body) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Html(title)}}</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body {
            margin: 0; padding: 24px 16px 48px;
            background: #14161a; color: #f2f4f7;
            font: 16px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            max-width: 720px; margin-inline: auto;
          }
          h1 { font-size: 26px; margin: 0 0 8px; }
          h2 { font-size: 15px; text-transform: uppercase; letter-spacing: .08em;
               color: #9aa3ad; margin: 32px 0 12px; font-weight: 600; }
          .hint { color: #9aa3ad; margin: 0 0 20px; }
          .btn {
            display: block; text-align: center; text-decoration: none;
            background: #f2f4f7; color: #14161a; font-weight: 600;
            padding: 14px; border-radius: 10px;
          }
          figure { margin: 0 0 12px; }
          img { width: 100%; height: auto; display: block; border-radius: 10px; background: #0d0f12; }
          .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
          .foot { color: #6b7480; font-size: 14px; margin-top: 32px; }
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string Url(string value) => Uri.EscapeDataString(value);
}
