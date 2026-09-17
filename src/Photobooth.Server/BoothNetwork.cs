using System.Security.Cryptography.X509Certificates;

namespace Photobooth.Server;

/// <summary>
/// How devices reach the booth.
///
/// Two listeners, deliberately, because the two audiences cannot be served by
/// one. The iPad runs the guest page and needs the camera, which Safari only
/// grants on a secure origin -- so it needs HTTPS on a hostname a certificate
/// can actually be issued for. A guest's phone only downloads files and reaches
/// the booth by raw LAN address, which no certificate can cover; pointing it at
/// the same HTTPS origin would hand every guest a browser warning instead of
/// their photos.
/// </summary>
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>Plain HTTP, for guest phones collecting their photos.</summary>
    public int DeliveryPort { get; set; } = 8080;

    /// <summary>HTTPS, for the iPad guest screen and the operator console.</summary>
    public int BoothPort { get; set; } = 8443;

    /// <summary>
    /// The name the iPad uses, e.g. <c>booth.local</c>.
    ///
    /// iOS scopes camera permission per origin, so this must not change between
    /// events: reaching the booth once by raw IP starts the permission prompt
    /// over, and reaching it by a name the certificate does not cover fails
    /// outright. Informational here -- it is the certificate and DNS that make
    /// it work -- but it is what the setup page tells the operator to type.
    /// </summary>
    public string Hostname { get; set; } = "";

    /// <summary>
    /// A PKCS#12 bundle for <see cref="Hostname"/>.
    ///
    /// Where it came from is deliberately not this code's business: mkcert with
    /// a local CA trusted on the iPad, or Let's Encrypt against a real domain,
    /// produce the same thing as far as Kestrel is concerned. That is what lets
    /// the booth start on mkcert and move to a real domain without a code change.
    /// </summary>
    public string CertificatePath { get; set; } = "";

    public string CertificatePassword { get; set; } = "";

    /// <summary>
    /// Listen on every interface rather than loopback.
    ///
    /// Has to be true for any of this to work -- a booth only reachable from
    /// itself is no booth -- but it is the setting that makes Windows raise a
    /// firewall prompt, so it is worth being a visible switch rather than a
    /// buried constant.
    /// </summary>
    public bool ListenOnAllInterfaces { get; set; } = true;
}

/// <summary>What happened when the booth tried to load its certificate.</summary>
/// <param name="Problem">
/// Null when it loaded. Phrased for an operator rather than a developer, because
/// this is read on the setup page half an hour before an event.
/// </param>
public sealed record CertificateStatus(
    bool Loaded,
    string? Subject = null,
    DateTimeOffset? ExpiresUtc = null,
    string? Problem = null)
{
    /// <summary>
    /// Days left, or null when there is no certificate.
    ///
    /// Surfaced rather than left for somebody to work out from a date, because
    /// an expired certificate does not degrade gracefully: the iPad simply stops
    /// trusting the booth, and it always happens at an event rather than at a
    /// desk.
    /// </summary>
    public int? DaysRemaining => ExpiresUtc is { } at
        ? (int)Math.Floor((at - DateTimeOffset.UtcNow).TotalDays)
        : null;

    /// <summary>Worth warning about on the setup page before somebody is caught out.</summary>
    public bool ExpiringSoon => DaysRemaining is { } days && days <= 21;

    public static CertificateStatus None(string problem) => new(false, Problem: problem);
}

/// <summary>
/// Loads the booth's certificate, and says clearly why not when it cannot.
///
/// Never throws. A missing or broken certificate has to leave the booth running
/// on HTTP -- the operator console, the watch folder and guest delivery all
/// still work, and only the iPad's camera is lost. Refusing to start would turn
/// a degraded booth into no booth at all, half an hour before an event.
/// </summary>
public static class BoothCertificate
{
    public static X509Certificate2? Load(NetworkOptions options, out CertificateStatus status)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            status = CertificateStatus.None(
                "No certificate configured, so the booth is HTTP only. The iPad "
                + "cannot use its camera without one -- see docs/NETWORK-SETUP.md.");
            return null;
        }

        var path = Path.IsPathRooted(options.CertificatePath)
            ? options.CertificatePath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CertificatePath));

        if (!File.Exists(path))
        {
            status = CertificateStatus.None($"No certificate file at {path}.");
            return null;
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path, options.CertificatePassword);

            if (!certificate.HasPrivateKey)
            {
                // A public-only export is an easy mistake to make and produces a
                // file that looks entirely correct until Kestrel refuses it.
                status = CertificateStatus.None(
                    $"{Path.GetFileName(path)} has no private key in it. Export "
                    + "the certificate *with* its key.");
                return null;
            }

            status = new CertificateStatus(
                true,
                certificate.Subject,
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));

            return certificate;
        }
        catch (Exception ex)
        {
            // Overwhelmingly a wrong password, but the exception text is the only
            // thing that can tell the difference between that and a corrupt file.
            status = CertificateStatus.None(
                $"Could not read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}
