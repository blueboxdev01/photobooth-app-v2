using Microsoft.Extensions.Options;
using Photobooth.Delivery;

namespace Photobooth.Delivery.Tests;

/// <summary>
/// The address a guest is sent to.
///
/// Worth testing carefully despite being string building, because it is the one
/// piece of the booth that fails silently: a wrong URL still renders a perfectly
/// valid QR, the guest screen looks right, and nobody finds out until someone
/// scans it and gets nothing -- by which point they have left.
/// </summary>
public sealed class LocalPublisherTests
{
    private static SessionRecord Record(string token = "aB3-dEf_1") => new(
        token,
        "2026-09-17_1942_ab3def",
        DateTimeOffset.UtcNow,
        "classic-2x6",
        4,
        "strip.jpg",
        ["photo-1.jpg", "photo-2.jpg"],
        ["IMG_0001.JPG", "IMG_0002.JPG"]);

    private static LocalPublisher With(string baseUrl, int port = 8080) =>
        new(Options.Create(new DeliveryOptions { BaseUrl = baseUrl, Port = port }));

    [Fact]
    public void The_link_is_the_session_token_on_the_configured_origin()
    {
        var link = With("http://192.168.8.2:8080").Publish(Record());

        Assert.Equal("http://192.168.8.2:8080/s/aB3-dEf_1", link.Url);
    }

    /// <summary>
    /// The operator types the address by hand, and a trailing slash is the most
    /// natural thing in the world to leave on the end of one.
    /// </summary>
    [Fact]
    public void A_trailing_slash_on_the_override_does_not_double_up()
    {
        var link = With("http://192.168.8.2:8080/").Publish(Record());

        Assert.Equal("http://192.168.8.2:8080/s/aB3-dEf_1", link.Url);
        Assert.DoesNotContain("//s/", link.Url);
    }

    /// <summary>
    /// With no override, fall back to this machine's address rather than
    /// refusing to publish. A booth that will not hand out a link at all is
    /// worse than one handing out a link the operator can correct in Setup.
    /// </summary>
    [Fact]
    public void With_no_override_it_falls_back_to_the_detected_address()
    {
        var publisher = With("");

        var link = publisher.Publish(Record());

        Assert.StartsWith("http://", link.Url);
        Assert.Equal($"{publisher.Detected()}/s/aB3-dEf_1", link.Url);
    }

    /// <summary>
    /// The detected address must carry the port the server is really listening
    /// on. A correct address on the wrong port is exactly as dead as a wrong
    /// address, and looks just as plausible in Setup.
    /// </summary>
    [Fact]
    public void The_detected_address_uses_the_configured_port()
    {
        Assert.EndsWith(":9000", With("", port: 9000).Detected());
    }

    /// <summary>
    /// Tokens are what keep one guest out of another guest's photos, so two
    /// sessions must never be handed the same link.
    /// </summary>
    [Fact]
    public void Different_sessions_get_different_links()
    {
        var publisher = With("http://192.168.8.2:8080");

        Assert.NotEqual(
            publisher.Publish(Record("first")).Url,
            publisher.Publish(Record("second")).Url);
    }

    /// <summary>
    /// The QR is the only way a guest ever reads this URL, so the bytes on the
    /// screen have to encode the link we think we published.
    /// </summary>
    [Fact]
    public void The_qr_encodes_the_published_link()
    {
        var link = With("http://192.168.8.2:8080").Publish(Record());

        Assert.Equal(QrRenderer.Png(link.Url), QrRenderer.Png("http://192.168.8.2:8080/s/aB3-dEf_1"));
        Assert.NotEqual(QrRenderer.Png(link.Url), QrRenderer.Png("http://192.168.8.2:8080/s/other"));
    }

    /// <summary>
    /// The booth's own screens fetch the QR by folder, not by token: they are
    /// already on the origin, and the folder is what the operator sees on disk.
    /// </summary>
    [Fact]
    public void The_qr_path_is_relative_and_addresses_the_session_folder()
    {
        var record = Record();

        var link = With("http://192.168.8.2:8080").Publish(record);

        Assert.Equal($"/api/sessions/{record.FolderName}/qr.png", link.QrUrl);
    }

    /// <summary>
    /// Detection must produce something a phone could actually dial, never an
    /// empty string quietly concatenated into a broken URL.
    /// </summary>
    [Fact]
    public void The_detected_address_is_always_a_usable_ipv4_address()
    {
        var address = LocalPublisher.LocalAddress();

        Assert.True(
            System.Net.IPAddress.TryParse(address, out var parsed),
            $"'{address}' is not an IP address");
        Assert.Equal(
            System.Net.Sockets.AddressFamily.InterNetwork, parsed!.AddressFamily);
    }
}
