using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Photobooth.Core;

namespace Photobooth.Core.Tests;

file sealed class FakeTemplateProvider(int slots) : ITemplateProvider
{
    public StripTemplate Current { get; } = new(
        "fake",
        new TemplateCanvas(600, 1800),
        [.. Enumerable.Range(0, slots).Select(i => new TemplateSlot(0.1, i * 0.25, 0.8, 0.2))]);
}

/// <summary>
/// Nudging one photo's rectangle during review.
///
/// The property that matters most is what this does <b>not</b> touch: the
/// template on disk. A guest too tall for the middle slot is a problem with one
/// strip, and quietly rewriting the template to fix it would carry that
/// compromise into every guest for the rest of the night.
/// </summary>
public class SessionSlotTests
{
    private const int Shots = 3;
    private const int Countdown = 3;

    private static (SessionEngine Engine, FakeTimeProvider Time, ITemplateProvider Templates)
        Reviewing(int shots = Shots)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
        var templates = new FakeTemplateProvider(shots);

        var engine = new SessionEngine(
            Options.Create(new SessionSettings
            {
                CountdownSeconds = Countdown,
                NoPhotoTimeoutSeconds = 20,
            }),
            templates,
            NullLogger<SessionEngine>.Instance,
            time);

        engine.Arm();
        for (var i = 1; i <= shots; i++)
        {
            time.Advance(TimeSpan.FromSeconds(Countdown));
            engine.SubmitPhoto(new CapturedPhoto(
                $@"C:\watch\IMG_{i:0000}.JPG", $"IMG_{i:0000}.JPG", 250_000, DateTimeOffset.UtcNow));
        }

        Assert.Equal(SessionState.ReviewShots, engine.Snapshot.State);
        return (engine, time, templates);
    }

    [Fact]
    public void Moving_a_slot_changes_what_this_session_composites_with()
    {
        var (engine, _, _) = Reviewing();

        var result = engine.AdjustSlot(1, new TemplateSlot(0.2, 0.3, 0.5, 0.15));

        Assert.True(result.Ok);
        Assert.Equal(new TemplateSlot(0.2, 0.3, 0.5, 0.15), engine.EffectiveSlots[1]);
    }

    /// <summary>The whole point: the saved template is left exactly as it was.</summary>
    [Fact]
    public void Moving_a_slot_does_not_touch_the_template()
    {
        var (engine, _, templates) = Reviewing();
        var before = templates.Current.Slots[1];

        engine.AdjustSlot(1, new TemplateSlot(0.2, 0.3, 0.5, 0.15));

        Assert.Equal(before, templates.Current.Slots[1]);
    }

    [Fact]
    public void Slots_that_were_not_touched_keep_the_templates_rectangle()
    {
        var (engine, _, templates) = Reviewing();

        engine.AdjustSlot(1, new TemplateSlot(0.2, 0.3, 0.5, 0.15));

        Assert.Equal(templates.Current.Slots[0], engine.EffectiveSlots[0]);
        Assert.Equal(templates.Current.Slots[2], engine.EffectiveSlots[2]);
    }

    /// <summary>
    /// Until something is moved, the session composites with the template itself
    /// -- which is what lets the console offer "back to the template" only when
    /// there is something to go back from.
    /// </summary>
    [Fact]
    public void An_untouched_session_reports_no_moved_slots()
    {
        var (engine, _, templates) = Reviewing();

        Assert.Null(engine.Snapshot.Slots);
        Assert.False(engine.Snapshot.HasMovedSlots);
        Assert.Equal(templates.Current.Slots, engine.EffectiveSlots);
    }

    [Fact]
    public void Resetting_goes_back_to_the_template()
    {
        var (engine, _, templates) = Reviewing();
        engine.AdjustSlot(0, new TemplateSlot(0, 0, 0.3, 0.3));

        var snapshot = engine.ResetSlots();

        Assert.Null(snapshot.Slots);
        Assert.False(snapshot.HasMovedSlots);
        Assert.Equal(templates.Current.Slots, engine.EffectiveSlots);
    }

    /// <summary>
    /// A drag off the edge is an operator pulling a photo to the very edge, not
    /// an error worth a dialogue. A slot outside the canvas composites to
    /// nothing, which looks to everyone like a lost photo.
    /// </summary>
    [Theory]
    [InlineData(-0.5, -0.5, 0.4, 0.4)]
    [InlineData(0.9, 0.9, 0.5, 0.5)]
    [InlineData(0.1, 0.1, 5, 5)]
    public void A_slot_dragged_off_the_canvas_is_clamped_onto_it(
        double x, double y, double w, double h)
    {
        var (engine, _, _) = Reviewing();

        Assert.True(engine.AdjustSlot(0, new TemplateSlot(x, y, w, h)).Ok);

        var slot = engine.EffectiveSlots[0];

        Assert.InRange(slot.X, 0, 1);
        Assert.InRange(slot.Y, 0, 1);
        Assert.InRange(slot.X + slot.W, 0, 1.0001);
        Assert.InRange(slot.Y + slot.H, 0, 1.0001);
    }

    /// <summary>
    /// A slot shrunk to nothing is indistinguishable on screen from a photo that
    /// failed to load, and sends the operator hunting for a bug in ingest.
    /// </summary>
    [Fact]
    public void A_slot_cannot_be_shrunk_to_nothing()
    {
        var (engine, _, _) = Reviewing();

        engine.AdjustSlot(0, new TemplateSlot(0.5, 0.5, 0, 0));

        Assert.True(engine.EffectiveSlots[0].W > 0);
        Assert.True(engine.EffectiveSlots[0].H > 0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void A_slot_that_is_not_on_the_strip_is_refused(int slot)
    {
        var (engine, _, _) = Reviewing();

        var result = engine.AdjustSlot(slot, new TemplateSlot(0, 0, 0.5, 0.5));

        Assert.False(result.Ok);
        Assert.Contains("between 1 and 3", result.Error);
    }

    /// <summary>
    /// Before review the set of shots is still changing, and after Accept the
    /// strip has already been built from whatever the slots said at the time.
    /// </summary>
    [Fact]
    public void Slots_cannot_be_moved_outside_review()
    {
        var (engine, _, _) = Reviewing();
        engine.Accept();

        var result = engine.AdjustSlot(0, new TemplateSlot(0, 0, 0.5, 0.5));

        Assert.False(result.Ok);
        Assert.Contains("reviewing", result.Error);
    }

    /// <summary>One guest's nudge must not follow the next guest into their strip.</summary>
    [Fact]
    public void Arming_the_next_session_forgets_the_last_ones_nudges()
    {
        var (engine, _, templates) = Reviewing();
        engine.AdjustSlot(0, new TemplateSlot(0, 0, 0.3, 0.3));

        engine.Arm();

        Assert.Null(engine.Snapshot.Slots);
        Assert.Equal(templates.Current.Slots, engine.EffectiveSlots);
    }

    [Fact]
    public void Aborting_forgets_them_too()
    {
        var (engine, _, templates) = Reviewing();
        engine.AdjustSlot(0, new TemplateSlot(0, 0, 0.3, 0.3));

        engine.Abort("operator");

        Assert.Equal(templates.Current.Slots, engine.EffectiveSlots);
    }
}
