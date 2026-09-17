using System.Security.Cryptography;
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
    /// Where it came from is deliberately not this code's business: one the booth
    /// made for itself, mkcert with a local CA, or Let's Encrypt against a real
    /// domain all produce the same thing as far as Kestrel is concerned. That is
    /// what lets a booth start on a self-made certificate and move to a real
    /// domain by replacing one file.
    ///
    /// If there is no file here, the booth makes one -- see
    /// <see cref="BoothCertificate.EnsureExists"/>.
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
    /// <summary>
    /// The file the iPad installs to trust this booth, written beside the
    /// certificate itself.
    /// </summary>
    public const string AuthorityFileName = "booth-ca.crt";

    /// <summary>
    /// Safari refuses a server certificate valid for more than 398 days, however
    /// correct it is otherwise. The failure looks like an untrusted booth rather
    /// than an over-long certificate, so it is not a limit to discover by hand.
    /// </summary>
    private const int LeafDays = 390;

    /// <summary>
    /// Creates a certificate authority and a certificate for the booth, if there
    /// is not one already.
    ///
    /// Exists so that setting the booth up needs no tooling on the machine. The
    /// alternative is installing mkcert, generating a CA, finding its root
    /// certificate on disk and getting that file onto the iPad -- six steps for
    /// somebody who only wants to see whether the camera works, and six chances
    /// to give up.
    ///
    /// Generating our own instead means the iPad can fetch the authority from the
    /// booth's own plain-HTTP port, which it can already reach.
    ///
    /// This is deliberately not a replacement for a real certificate. It produces
    /// exactly what mkcert would: a private authority that has to be trusted by
    /// hand on every device that uses it. A booth with a domain should still point
    /// <see cref="NetworkOptions.CertificatePath"/> at a real one.
    /// </summary>
    public static void EnsureExists(NetworkOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath)
            || string.IsNullOrWhiteSpace(options.Hostname))
        {
            return;
        }

        var path = Resolve(options.CertificatePath);

        if (File.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var now = DateTimeOffset.UtcNow;

            // Backdated an hour, because a booth and an iPad rarely agree on the
            // time to the minute and a certificate that is not valid *yet* is
            // rejected exactly as firmly as one that has expired.
            var from = now.AddHours(-1);

            using var authorityKey = RSA.Create(2048);
            using var authority = CreateAuthority(authorityKey, from, now.AddYears(5));

            using var leafKey = RSA.Create(2048);
            using var leaf = CreateLeaf(
                leafKey, authority, options.Hostname, from, now.AddDays(LeafDays));

            File.WriteAllBytes(
                path, leaf.Export(X509ContentType.Pkcs12, options.CertificatePassword));

            File.WriteAllBytes(
                Path.Combine(Path.GetDirectoryName(path)!, AuthorityFileName),
                authority.Export(X509ContentType.Cert));

            logger.LogInformation(
                "Generated a certificate for {Host}, valid {Days} days, and wrote "
                + "the authority to {Authority} for the iPad to install.",
                options.Hostname, LeafDays, AuthorityFileName);
        }
        catch (Exception ex)
        {
            // Not fatal: the booth runs on HTTP without it, and Load reports the
            // absence in words the operator can act on.
            logger.LogWarning(ex, "Could not generate a booth certificate.");
        }
    }

    /// <summary>Where the authority file ended up, or null when there is not one.</summary>
    public static string? AuthorityPath(NetworkOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(Resolve(options.CertificatePath));
        if (folder is null)
        {
            return null;
        }

        var path = Path.Combine(folder, AuthorityFileName);
        return File.Exists(path) ? path : null;
    }

    private static X509Certificate2 CreateAuthority(
        RSA key, DateTimeOffset from, DateTimeOffset to)
    {
        var request = new CertificateRequest(
            "CN=Photobooth booth authority, O=Photobooth",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(from, to);
    }

    private static X509Certificate2 CreateLeaf(
        RSA key,
        X509Certificate2 authority,
        string hostname,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var request = new CertificateRequest(
            $"CN={hostname}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        // serverAuth. iOS ignores a certificate with no extended key usage at all,
        // whatever else is right about it.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));

        // Modern Safari reads the subject alternative name and nothing else -- the
        // common name has been ignored for years, so a certificate with only a CN
        // fails with no useful explanation.
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(hostname);
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);

        using var issued = request.Create(authority, from, to, serial);

        // The certificate and its key are made separately and have to be put back
        // together before the bundle is worth anything to Kestrel.
        return issued.CopyWithPrivateKey(key);
    }

    private static string Resolve(string path) => Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

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
