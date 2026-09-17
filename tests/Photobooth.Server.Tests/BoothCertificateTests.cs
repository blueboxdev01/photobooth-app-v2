using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Photobooth.Server;

namespace Photobooth.Server.Tests;

/// <summary>
/// Loading the booth's certificate.
///
/// Every test here is about a failure, because the success case announces itself
/// and the failures do not. A certificate that will not load has to leave the
/// booth running and say why in words an operator can act on -- this is read
/// half an hour before an event, by someone who did not write it.
/// </summary>
public sealed class BoothCertificateTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"pb-cert-{Guid.NewGuid():N}");

    public BoothCertificateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// A stand-in for whatever mkcert or Let's Encrypt produces. The loader only
    /// cares that it is a PKCS#12 bundle with a private key in it, which is
    /// exactly the property worth pinning.
    /// </summary>
    private string WritePfx(
        string name,
        string password = "",
        bool withPrivateKey = true,
        DateTimeOffset? expires = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=booth.local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            expires ?? DateTimeOffset.UtcNow.AddDays(365));

        var path = Path.Combine(_folder, name);

        if (withPrivateKey)
        {
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
            return path;
        }

        // Round-tripping through a bare Cert export is what actually strips the
        // key: exporting straight to PKCS#12 keeps it whatever password is given,
        // which is precisely the confusion this case exists to catch.
        using var publicOnly = X509CertificateLoader.LoadCertificate(
            certificate.Export(X509ContentType.Cert));

        File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pkcs12, password));
        return path;
    }

    private static NetworkOptions At(string path, string password = "") => new()
    {
        CertificatePath = path,
        CertificatePassword = password,
    };

    [Fact]
    public void A_valid_bundle_loads_and_reports_its_subject_and_expiry()
    {
        var path = WritePfx("booth.pfx", expires: DateTimeOffset.UtcNow.AddDays(90));

        var certificate = BoothCertificate.Load(At(path), out var status);

        Assert.NotNull(certificate);
        Assert.True(status.Loaded);
        Assert.Null(status.Problem);
        Assert.Contains("booth.local", status.Subject);
        Assert.InRange(status.DaysRemaining!.Value, 88, 90);

        certificate!.Dispose();
    }

    /// <summary>
    /// No certificate at all is the state a fresh clone is in, so it must not
    /// read as breakage -- but it must still explain what is lost and point at
    /// the fix.
    /// </summary>
    [Fact]
    public void No_configured_path_is_explained_rather_than_treated_as_an_error()
    {
        var certificate = BoothCertificate.Load(new NetworkOptions(), out var status);

        Assert.Null(certificate);
        Assert.False(status.Loaded);
        Assert.Contains("HTTP only", status.Problem);
        Assert.Contains("NETWORK-SETUP", status.Problem);
    }

    /// <summary>The message has to name the path, or nobody can tell what it looked at.</summary>
    [Fact]
    public void A_missing_file_names_the_path_it_looked_for()
    {
        var missing = Path.Combine(_folder, "not-there.pfx");

        var certificate = BoothCertificate.Load(At(missing), out var status);

        Assert.Null(certificate);
        Assert.False(status.Loaded);
        Assert.Contains("not-there.pfx", status.Problem);
    }

    [Fact]
    public void A_wrong_password_reports_the_file_it_could_not_read()
    {
        var path = WritePfx("locked.pfx", password: "correct");

        var certificate = BoothCertificate.Load(At(path, "wrong"), out var status);

        Assert.Null(certificate);
        Assert.False(status.Loaded);
        Assert.Contains("locked.pfx", status.Problem);
    }

    /// <summary>
    /// Exporting without the private key is an easy mistake that produces a file
    /// which looks entirely correct until Kestrel refuses it at startup. Say so
    /// in terms of what to do differently.
    /// </summary>
    [Fact]
    public void A_bundle_with_no_private_key_says_so_in_those_words()
    {
        var path = WritePfx("public-only.pfx", withPrivateKey: false);

        var certificate = BoothCertificate.Load(At(path), out var status);

        Assert.Null(certificate);
        Assert.False(status.Loaded);
        Assert.Contains("private key", status.Problem);
    }

    /// <summary>
    /// Nothing about a bad certificate may stop the booth: the operator console,
    /// ingest and guest delivery all still work without one.
    /// </summary>
    [Fact]
    public void Nothing_throws_whatever_the_file_turns_out_to_be()
    {
        var garbage = Path.Combine(_folder, "garbage.pfx");
        File.WriteAllText(garbage, "this is not a certificate");

        var certificate = BoothCertificate.Load(At(garbage), out var status);

        Assert.Null(certificate);
        Assert.False(status.Loaded);
        Assert.False(string.IsNullOrWhiteSpace(status.Problem));
    }

    /// <summary>
    /// The warning has to arrive with enough runway to do something about it,
    /// and an expired certificate must not read as merely "soon".
    /// </summary>
    [Theory]
    [InlineData(400, false)]
    [InlineData(60, false)]
    [InlineData(14, true)]
    [InlineData(1, true)]
    public void Expiry_is_flagged_while_there_is_still_time_to_fix_it(
        int daysAway, bool expectWarning)
    {
        var path = WritePfx(
            $"expiring-{daysAway}.pfx",
            expires: DateTimeOffset.UtcNow.AddDays(daysAway));

        var certificate = BoothCertificate.Load(At(path), out var status);

        Assert.Equal(expectWarning, status.ExpiringSoon);

        certificate?.Dispose();
    }
}
