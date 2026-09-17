using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Server;

namespace Photobooth.Server.Tests;

/// <summary>
/// The certificate the booth makes for itself when there is not one.
///
/// Every assertion here is a rule iOS enforces silently. Get any of them wrong
/// and Safari does not explain itself -- it simply refuses the camera, which
/// reads as a broken app rather than a rejected certificate. So they are pinned
/// here, where they can be read, rather than rediscovered on an iPad.
/// </summary>
public sealed class GeneratedCertificateTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"pb-gen-{Guid.NewGuid():N}");

    public GeneratedCertificateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private NetworkOptions Options(string host = "booth.local") => new()
    {
        Hostname = host,
        CertificatePath = Path.Combine(_folder, "booth.pfx"),
    };

    private static void Generate(NetworkOptions options) =>
        BoothCertificate.EnsureExists(options, NullLogger.Instance);

    private static X509Certificate2 Leaf(NetworkOptions options)
    {
        var certificate = BoothCertificate.Load(options, out var status);
        Assert.True(status.Loaded, status.Problem);
        return certificate!;
    }

    [Fact]
    public void It_writes_a_certificate_and_an_authority_to_hand_out()
    {
        var options = Options();

        Generate(options);

        Assert.True(File.Exists(options.CertificatePath));
        Assert.NotNull(BoothCertificate.AuthorityPath(options));
    }

    [Fact]
    public void What_it_writes_is_something_the_booth_can_load()
    {
        var options = Options();
        Generate(options);

        using var leaf = Leaf(options);

        Assert.True(leaf.HasPrivateKey);
        Assert.Contains("booth.local", leaf.Subject);
    }

    /// <summary>
    /// Safari has ignored the common name for years and reads only the subject
    /// alternative name, so a certificate without one fails while looking
    /// entirely correct to a person.
    /// </summary>
    [Fact]
    public void The_hostname_is_in_the_subject_alternative_name()
    {
        var options = Options("photobooth.lan");
        Generate(options);

        using var leaf = Leaf(options);

        var san = leaf.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .SingleOrDefault();

        Assert.NotNull(san);
        Assert.Contains("photobooth.lan", san!.EnumerateDnsNames());
    }

    /// <summary>iOS ignores a certificate with no extended key usage at all.</summary>
    [Fact]
    public void It_is_marked_for_server_authentication()
    {
        var options = Options();
        Generate(options);

        using var leaf = Leaf(options);

        var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();

        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), o => o.Value == "1.3.6.1.5.5.7.3.1");
    }

    /// <summary>
    /// Safari refuses a server certificate valid for more than 398 days, however
    /// correct it is otherwise -- and reports it as an untrusted booth.
    /// </summary>
    [Fact]
    public void It_is_valid_for_less_than_the_398_days_safari_allows()
    {
        var options = Options();
        Generate(options);

        using var leaf = Leaf(options);

        var days = (leaf.NotAfter - leaf.NotBefore).TotalDays;

        Assert.InRange(days, 1, 398);
    }

    /// <summary>
    /// A booth and an iPad rarely agree on the time to the minute, and a
    /// certificate that is not valid *yet* is rejected exactly as firmly as one
    /// that has expired.
    /// </summary>
    [Fact]
    public void It_is_already_valid_when_it_is_written()
    {
        var options = Options();
        Generate(options);

        using var leaf = Leaf(options);

        Assert.True(leaf.NotBefore.ToUniversalTime() < DateTime.UtcNow);
        Assert.True(leaf.NotAfter.ToUniversalTime() > DateTime.UtcNow);
    }

    /// <summary>
    /// The file the iPad installs has to be usable as a root, or trusting it
    /// achieves nothing.
    /// </summary>
    [Fact]
    public void The_authority_is_a_certificate_authority()
    {
        var options = Options();
        Generate(options);

        using var authority = X509CertificateLoader.LoadCertificateFromFile(
            BoothCertificate.AuthorityPath(options)!);

        var basic = authority.Extensions.OfType<X509BasicConstraintsExtension>().Single();

        Assert.True(basic.CertificateAuthority);
    }

    /// <summary>The authority must not carry a private key off the booth.</summary>
    [Fact]
    public void The_authority_handed_out_holds_no_private_key()
    {
        var options = Options();
        Generate(options);

        using var authority = X509CertificateLoader.LoadCertificateFromFile(
            BoothCertificate.AuthorityPath(options)!);

        Assert.False(authority.HasPrivateKey);
    }

    [Fact]
    public void The_certificate_is_signed_by_that_authority()
    {
        var options = Options();
        Generate(options);

        using var leaf = Leaf(options);
        using var authority = X509CertificateLoader.LoadCertificateFromFile(
            BoothCertificate.AuthorityPath(options)!);

        Assert.Equal(authority.Subject, leaf.Issuer);
    }

    /// <summary>
    /// Regenerating on every start would invalidate the profile already trusted
    /// on the iPad, and the booth would stop working on the morning of an event
    /// having changed nothing.
    /// </summary>
    [Fact]
    public void An_existing_certificate_is_left_alone()
    {
        var options = Options();
        Generate(options);
        var first = File.ReadAllBytes(options.CertificatePath);

        Generate(options);

        Assert.Equal(first, File.ReadAllBytes(options.CertificatePath));
    }

    /// <summary>
    /// A booth pointed at a real certificate must never have one invented over
    /// the top of it.
    /// </summary>
    [Fact]
    public void It_does_not_overwrite_a_certificate_that_is_already_there()
    {
        var options = Options();
        File.WriteAllText(options.CertificatePath, "somebody else's certificate");

        Generate(options);

        Assert.Equal(
            "somebody else's certificate", File.ReadAllText(options.CertificatePath));
    }

    [Fact]
    public void With_no_hostname_there_is_nothing_to_issue_a_certificate_for()
    {
        var options = Options(host: "");

        Generate(options);

        Assert.False(File.Exists(options.CertificatePath));
    }

    [Fact]
    public void With_no_path_configured_nothing_is_written()
    {
        var options = new NetworkOptions { Hostname = "booth.local", CertificatePath = "" };

        Generate(options);

        Assert.Null(BoothCertificate.AuthorityPath(options));
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }
}
