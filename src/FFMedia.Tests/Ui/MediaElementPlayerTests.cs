using System.Windows.Controls;
using FFMedia.Tests.Views;
using FFMedia.Ui.Playback;
using Xunit;

namespace FFMedia.Tests.Ui;

/// <summary>Pins the two flags <see cref="MediaElementPlayer.Attach"/> sets on the real
/// <see cref="MediaElement"/> — neither was asserted anywhere before this.
///
/// <para><b>FINDING 3.</b> Without <c>ScrubbingEnabled</c>, setting <c>Position</c> while paused does
/// NOT render the new frame — so the user would capture a timestamp for a frame they never saw, and the
/// preview's entire reason for existing ("pause on the frame you want, then capture") is quietly wrong
/// while every other test in the suite stays green. <c>LoadedBehavior == Manual</c> is equally
/// load-bearing: without it the element auto-plays and ignores every <c>Play()</c>/<c>Pause()</c>/seek
/// this control issues.</para>
///
/// <para>Needs a real <see cref="MediaElement"/>, hence an STA thread — the shared <see cref="WpfHost"/>,
/// same as every other WPF-hosted test in this project.</para></summary>
[Collection("wpf")]
public class MediaElementPlayerTests
{
    private readonly WpfHost _wpf;

    public MediaElementPlayerTests(WpfHost wpf) => _wpf = wpf;

    [Fact]
    public void Attach_EnablesScrubbing_AndSetsManualLoadedBehavior()
    {
        // The MediaElement is owned by the STA thread that created it -- every read of it (not just the
        // construction) must happen on THAT thread, so the two flags under test are read inside the Run
        // lambda rather than smuggled out through a captured reference.
        bool scrubbingEnabled = false;
        MediaState loadedBehavior = MediaState.Play;

        var error = _wpf.Run(() =>
        {
            var element = new MediaElement();
            var player = new MediaElementPlayer();
            player.Attach(element);

            scrubbingEnabled = element.ScrubbingEnabled;
            loadedBehavior = element.LoadedBehavior;
        });

        Assert.True(error is null, $"Attach threw:\n{error}");
        Assert.True(
            scrubbingEnabled,
            "ScrubbingEnabled must be true -- without it, a paused seek does not render the new frame, " +
            "and the user captures a timestamp for a frame they never saw.");
        Assert.Equal(MediaState.Manual, loadedBehavior);
    }
}
