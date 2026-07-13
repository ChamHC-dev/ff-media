using System.Windows.Controls;

namespace FFMedia.Ui.Playback;

/// <summary><see cref="IMediaPlayer"/> over a real WPF <see cref="MediaElement"/>.
///
/// <para><b>This is the only place that knows about <c>MediaElement</c>.</b> Everything else talks to the
/// interface, which is what makes the proxy-fallback logic testable without a window.</para>
///
/// <para><b>Constructible before the element exists.</b> <see cref="ViewModels.VideoPreviewViewModel"/>
/// takes its <see cref="IMediaPlayer"/> in its <b>constructor</b>, but the real <c>MediaElement</c> does
/// not exist until <c>VideoPreview</c>'s XAML has actually parsed — so DI cannot hand the VM a real
/// player at construction time. The resolution: this class has a parameterless constructor and is
/// registered as the app's single <see cref="IMediaPlayer"/> singleton, so the VM is constructor-injected
/// normally; <see cref="Attach"/> is then called exactly once — by <c>VideoPreview</c>, on the SAME
/// singleton instance, right after its own <c>InitializeComponent()</c> — to hand over the real
/// element.</para>
///
/// <para><b>Before <see cref="Attach"/>, every member degrades safely instead of throwing:</b>
/// <see cref="Position"/> reads <see cref="TimeSpan.Zero"/>, <see cref="Duration"/> is <c>null</c>, and
/// <see cref="Play"/>/<see cref="Pause"/> are no-ops. <see cref="Open"/> specifically does NOT no-op —
/// it QUEUES the requested path and <see cref="Attach"/> replays it. A silent no-op here would never
/// raise <see cref="MediaOpened"/>/<see cref="MediaFailed"/>, and <c>VideoPreviewViewModel.LoadAsync</c>
/// awaits exactly one of those — so a load that raced the control's own startup would hang forever on the
/// UI thread, which is precisely the class of bug Task 3 found and fixed for concurrent loads. In
/// practice <c>VideoPreview</c> attaches synchronously in its constructor, before any caller has a chance
/// to call <c>LoadAsync</c> — but queueing costs nothing and closes the gap outright rather than relying
/// on that ordering.</para>
///
/// <para><c>ScrubbingEnabled</c> is <b>load-bearing</b>: without it, setting <c>Position</c> while paused
/// does not render the new frame — so the user would capture a timestamp for a frame they never saw.</para></summary>
public sealed class MediaElementPlayer : IMediaPlayer
{
    private MediaElement? _element;
    private string? _pendingSource;

    public event EventHandler? MediaOpened;

    public event EventHandler<string>? MediaFailed;

    public TimeSpan Position
    {
        get => _element?.Position ?? TimeSpan.Zero;
        set
        {
            if (_element is not null)
            {
                _element.Position = value;
            }
        }
    }

    public TimeSpan? Duration
    {
        get
        {
            if (_element is null || !_element.NaturalDuration.HasTimeSpan)
            {
                return null;
            }

            return _element.NaturalDuration.TimeSpan;
        }
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Gives the player its real <see cref="MediaElement"/>. Called exactly once, by
    /// <c>VideoPreview</c>, once its own <c>InitializeComponent()</c> has run and the element exists.</summary>
    public void Attach(MediaElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        _element = element;
        _element.LoadedBehavior = MediaState.Manual;
        _element.ScrubbingEnabled = true;
        _element.MediaOpened += (_, _) => MediaOpened?.Invoke(this, EventArgs.Empty);
        _element.MediaFailed += (_, e) =>
            MediaFailed?.Invoke(this, e.ErrorException?.Message ?? "The player could not open this video.");

        if (_pendingSource is { } path)
        {
            _pendingSource = null;
            Open(path);
        }
    }

    public void Open(string path)
    {
        if (_element is null)
        {
            // Not attached yet — queue rather than drop, so a caller that raced our own startup still
            // eventually gets a MediaOpened/MediaFailed once Attach replays this.
            _pendingSource = path;
            return;
        }

        IsPlaying = false;
        _element.Source = new Uri(path);
    }

    public void Play()
    {
        if (_element is null)
        {
            return;
        }

        _element.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        if (_element is null)
        {
            return;
        }

        _element.Pause();
        IsPlaying = false;
    }
}
