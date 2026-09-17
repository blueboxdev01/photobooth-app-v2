using System.Net;
using Microsoft.Extensions.Options;

namespace Photobooth.Server;

/// <summary>
/// The page the iPad opens once, over plain HTTP, to start trusting the booth.
///
/// A chicken-and-egg problem otherwise: the guest screen needs HTTPS before
/// Safari will hand over the camera, HTTPS needs a certificate the iPad trusts,
/// and getting a certificate onto an iPad normally means a laptop, a file
/// transfer and a settings menu nobody can find. The one door that is open
/// without any of that is the plain-HTTP port the guest phones already use, so
/// the booth hands the certificate out through it.
///
/// Served over HTTP on purpose. It exists precisely because HTTPS does not work
/// yet, and putting it behind the certificate it is supposed to deliver would be
/// a locked door with the key inside.
/// </summary>
public static class SetupPage
{
    public static void MapSetupPage(this WebApplication app)
    {
        app.MapGet("/setup", (IOptions<NetworkOptions> options, CertificateStatus status) =>
            Results.Content(
                Render(options.Value, status), "text/html; charset=utf-8"));

        // The media type is what makes iOS offer to install this rather than
        // download it. Served as an attachment with a plain name because the
        // profile screen shows the file name to the person approving it.
        app.MapGet($"/{BoothCertificate.AuthorityFileName}", (IOptions<NetworkOptions> options) =>
        {
            var path = BoothCertificate.AuthorityPath(options.Value);

            return path is null
                ? Results.NotFound()
                : Results.File(path, "application/x-x509-ca-cert", BoothCertificate.AuthorityFileName);
        });
    }

    private static string Render(NetworkOptions network, CertificateStatus status)
    {
        var host = string.IsNullOrWhiteSpace(network.Hostname) ? "booth.local" : network.Hostname;
        var guest = $"https://{Html(host)}:{network.BoothPort}/guest";
        var available = BoothCertificate.AuthorityPath(network) is not null;

        var steps = available
            ? $"""
              <ol>
                <li>
                  <a class="btn" href="/{BoothCertificate.AuthorityFileName}">Download the certificate</a>
                  <span class="note">Tap <b>Allow</b> when iOS asks.</span>
                </li>
                <li>
                  Open <b>Settings &rsaquo; General &rsaquo; VPN &amp; Device Management</b>
                  and install the downloaded profile.
                </li>
                <li>
                  Open <b>Settings &rsaquo; General &rsaquo; About &rsaquo; Certificate Trust
                  Settings</b> and switch <b>Photobooth booth authority</b> on.
                  <span class="note">
                    This step is separate from installing it, and skipping it is the
                    single most common reason the guest screen still will not work.
                  </span>
                </li>
                <li>Open <a href="{guest}">{guest}</a> and allow the camera.</li>
              </ol>
              """
            : """
              <p class="warn">
                This booth has no certificate to hand out. Check the operator's
                Setup page for what went wrong.
              </p>
              """;

        var state = status.Loaded
            ? $"<p class=\"ok\">HTTPS is running. {Html(status.DaysRemaining?.ToString() ?? "?")} days left on the certificate.</p>"
            : $"<p class=\"warn\">HTTPS is not running: {Html(status.Problem ?? "no certificate")}</p>";

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Set up this iPad</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body {
            margin: 0 auto; padding: 24px 16px 48px; max-width: 620px;
            background: #14161a; color: #f2f4f7;
            font: 16px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
          }
          h1 { font-size: 24px; margin: 0 0 4px; }
          .lede { color: #9aa3ad; margin: 0 0 20px; }
          ol { padding-left: 22px; }
          li { margin-bottom: 20px; }
          .btn {
            display: inline-block; text-decoration: none; margin-bottom: 6px;
            background: #f2f4f7; color: #14161a; font-weight: 600;
            padding: 11px 18px; border-radius: 9px;
          }
          .note { display: block; color: #9aa3ad; font-size: 14px; margin-top: 4px; }
          .ok, .warn {
            padding: 11px 14px; border-radius: 9px; font-size: 15px; margin: 0 0 20px;
          }
          .ok { background: #10291a; color: #8fe0ab; }
          .warn { background: #2d1a17; color: #ff9a8b; }
          a { color: #8ab4ff; }
          code { background: #1d2026; padding: 2px 6px; border-radius: 5px; }
        </style>
        </head>
        <body>
        <h1>Set up this iPad</h1>
        <p class="lede">
          Four steps, once. After this the iPad trusts the booth and the guest
          screen can use the camera.
        </p>
        {{state}}
        {{steps}}
        <p class="note">
          Always open the guest screen by name (<code>{{Html(host)}}</code>), never
          by IP address. iOS ties camera permission to the exact address, so
          reaching it the other way starts the permission prompt over.
        </p>
        </body>
        </html>
        """;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
