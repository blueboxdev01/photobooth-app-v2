using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Photobooth.Core;
using Photobooth.Delivery;

namespace Photobooth.Delivery.Tests;

/// <summary>
/// Finding a session from the token in a guest's link.
///
/// This is the whole of the access control on a guest's photos, so what matters
/// is that it is exact: one token reaches one session and nothing else reaches
/// it at all.
/// </summary>
public sealed class TokenLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"pb-token-{Guid.NewGuid():N}");

    private readonly SessionArchive _archive;

    public TokenLookupTests()
    {
        Directory.CreateDirectory(_root);
        _archive = new SessionArchive(
            Options.Create(new ArchiveOptions { Folder = _root }),
            NullLogger<SessionArchive>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SessionRecord Save(string token)
    {
        var photo = Path.Combine(_root, $"src-{token}.jpg");
        File.WriteAllBytes(photo, [0xFF, 0xD8, 0xFF, 0xD9]);

        var strip = Path.Combine(_root, $"strip-{token}.jpg");
        File.WriteAllBytes(strip, [0xFF, 0xD8, 0xFF, 0xD9]);

        var template = new StripTemplate(
            "t", new TemplateCanvas(600, 1800), [new TemplateSlot(0, 0, 1, 0.3)]);

        return _archive.Save(
            token,
            template,
            [new CapturedPhoto(photo, Path.GetFileName(photo), 4, DateTimeOffset.UtcNow)],
            strip,
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_token_finds_its_own_session()
    {
        var saved = Save("aB3dEf1");

        var found = _archive.FindByToken("aB3dEf1");

        Assert.Equal(saved.FolderName, found?.FolderName);
    }

    [Fact]
    public void One_guests_token_does_not_reach_another_guests_session()
    {
        var first = Save("firstGuest");
        Save("secondGuest");

        Assert.Equal(first.FolderName, _archive.FindByToken("firstGuest")?.FolderName);
        Assert.NotEqual(first.FolderName, _archive.FindByToken("secondGuest")?.FolderName);
    }

    /// <summary>
    /// Tokens are base64url, where case is significant. Matching without regard
    /// to it would quietly collapse the keyspace and let one guest reach
    /// another's photos by getting the shape right and the case wrong.
    /// </summary>
    [Fact]
    public void Case_matters()
    {
        Save("aB3dEf1");

        Assert.Null(_archive.FindByToken("ab3def1"));
        Assert.Null(_archive.FindByToken("AB3DEF1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("aB3dEf")]
    [InlineData("aB3dEf12")]
    public void Anything_else_finds_nothing(string token)
    {
        Save("aB3dEf1");

        Assert.Null(_archive.FindByToken(token));
    }

    [Fact]
    public void A_null_token_finds_nothing()
    {
        Save("aB3dEf1");

        Assert.Null(_archive.FindByToken(null));
    }

    [Fact]
    public void An_empty_archive_finds_nothing() =>
        Assert.Null(_archive.FindByToken("anything"));

    /// <summary>
    /// The record lists the files a guest may fetch, and the delivery page uses
    /// exactly that list rather than validating the shape of a name -- so the
    /// list has to hold everything the session actually wrote.
    /// </summary>
    [Fact]
    public void The_record_names_every_file_a_guest_should_get()
    {
        var record = Save("aB3dEf1");
        var onDisk = Directory
            .EnumerateFiles(_archive.FolderFor(record))
            .Select(Path.GetFileName)
            .Where(n => n != "session.json")
            .ToList();

        var listed = new List<string> { record.Strip };
        listed.AddRange(record.Photos);
        if (record.Gif is not null) listed.Add(record.Gif);

        Assert.Equal(
            onDisk.OrderBy(n => n, StringComparer.Ordinal),
            listed.OrderBy(n => n, StringComparer.Ordinal));
    }
}
