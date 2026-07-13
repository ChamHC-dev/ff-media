using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FFMedia.Core.Results;
using FFMedia.Media;
using FFMedia.Media.Preview;
using FFMedia.Ui.Playback;
using FFMedia.Ui.ViewModels;
using FFMedia.Ui.Views;
using FFMedia.Tests.Views;
using Wpf.Ui.Controls;
using Xunit;

namespace FFMedia.Tests.Ui;

/// <summary>Proves <see cref="VideoPreview"/>'s XAML actually LOADS.
///
/// <para>Everything else about the control is checked by the compiler or by eye, and neither catches the
/// failure that matters: a <c>StaticResource</c> that does not resolve compiles clean, passes every
/// other test, and then throws <c>XamlParseException</c> the first time a human opens the page that
/// hosts it. Mirrors <c>GifMakerPageLoadTests</c> exactly, which mirrors <c>MergerPageLoadTests</c>,
/// which caught precisely this failure once already.</para>
///
/// <para>So: build the control for real, on an STA thread, against the same two resource dictionaries
/// App.xaml merges. If any resource lookup in the XAML is wrong, this fails here instead of in front of
/// the user.</para></summary>
[Collection("wpf")]
public class VideoPreviewLoadTests
{
    private readonly WpfHost _wpf;

    public VideoPreviewLoadTests(WpfHost wpf) => _wpf = wpf;

    [Fact]
    public void VideoPreview_LoadsItsXaml_WithTheAppsRealResourceDictionaries()
    {
        var error = RunOnStaThread(() =>
        {
            // InitializeComponent() — where the XAML is parsed and every StaticResource resolved.
            _ = new VideoPreview(BuildViewModel(), new MediaElementPlayer());
        });

        Assert.True(error is null, $"VideoPreview's XAML failed to load:\n{error}");
    }

    [Fact]
    public void VideoPreview_DoesNotNestItsOwnScrollViewer()
    {
        // WPF-UI's NavigationViewContentPresenter ALREADY wraps every page in a ScrollViewer — which is
        // why no other page (or control embedded in one) in this app has one. MergerPage shipped with a
        // second, nested one once. The outer scroller hands the inner one unbounded height, so the inner
        // can never scroll (ScrollableHeight = 0) — but WPF's ScrollViewer marks mouse-wheel events
        // HANDLED even when it cannot move. So it swallowed every tick and the shell's scroller, which
        // DID have room, never saw them.
        double shellScrollable = 0;
        object? controlRoot = null;

        var error = RunOnStaThread(() =>
        {
            var control = new VideoPreview(BuildViewModel(), new MediaElementPlayer());
            controlRoot = control.Content;

            // VideoPreview is never hosted BY the shell directly -- it is embedded inside a tool page
            // (the GIF Maker's own StackPanel), and that PAGE is what the shell's
            // NavigationViewContentPresenter wraps in its own scroller. So the realistic host here is a
            // plain Page containing the control, exactly mirroring how GifMakerPage will embed it.
            // (Verified empirically: NavigationViewContentPresenter's DynamicScrollViewer wrapper is
            // only applied when its Content is Page-derived -- hosting the control directly, with no
            // Page in between, produces no ScrollViewer ancestor at all and would make this assertion
            // fail for every control, proving nothing about VideoPreview specifically.)
            var page = new Page { Content = control };
            var presenter = new NavigationViewContentPresenter { Content = page };
            var window = new Window { Content = presenter, Width = 500, Height = 200 };
            window.Show();

            // A content presenter navigates on a dispatcher pass; without draining the queue the visual
            // tree does not exist yet and every measurement below reads 0.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();

            for (DependencyObject? cur = control; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is ScrollViewer ancestor && !ReferenceEquals(cur, control))
                {
                    shellScrollable = ancestor.ScrollableHeight;
                    break;
                }
            }

            window.Close();
        });

        Assert.True(error is null, $"Hosting VideoPreview threw:\n{error}");

        Assert.False(
            controlRoot is ScrollViewer,
            "VideoPreview's root is a ScrollViewer. The shell already provides one; a nested scroller " +
            "cannot scroll and still swallows the mouse wheel.");

        Assert.True(
            shellScrollable > 0,
            $"The shell's ScrollViewer reports ScrollableHeight={shellScrollable}, so a control taller " +
            "than the window cannot be scrolled at all.");
    }

    private static VideoPreviewViewModel BuildViewModel()
        => new(new StubAnalyzer(), new StubProxies(), new MediaElementPlayer());

    /// <summary>Runs on the ONE shared STA thread that owns the ONE WPF Application (see
    /// <see cref="WpfHost"/>).</summary>
    private Exception? RunOnStaThread(Action action) => _wpf.Run(action);

    // ---- the thinnest possible stubs: this test is about XAML, not behaviour ----

    private sealed class StubAnalyzer : IMediaAnalyzer
    {
        public Task<Result<MediaInfo>> AnalyzeAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(Result<MediaInfo>.Failure("stub"));
    }

    private sealed class StubProxies : IPreviewProxyService
    {
        public Task<Result<string>> GetOrCreateAsync(
            string sourcePath, MediaInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
            => Task.FromResult(Result<string>.Failure("stub"));

        public void SweepStale()
        {
        }
    }
}
