using System.Windows.Controls;
using FFMedia.Ui.Playback;
using FFMedia.Ui.ViewModels;

namespace FFMedia.Ui.Views;

/// <summary>The video preview: player, transport, and the capture buttons. Code-behind holds only what
/// genuinely needs the visual tree — attaching the real <see cref="MediaElement"/> to the player.
/// Everything else lives in <see cref="VideoPreviewViewModel"/>, which is headless and unit-tested.
///
/// <para>Takes the <see cref="MediaElementPlayer"/> as its OWN constructor parameter, separately from the
/// <see cref="VideoPreviewViewModel"/> that already holds it as its <see cref="IMediaPlayer"/> — DI
/// resolves both parameters to the SAME singleton instance. That is what lets the VM be
/// constructor-injected in the ordinary way (its <c>IMediaPlayer</c> exists at DI-container build time)
/// while the real <c>MediaElement</c> only becomes available here, once <c>InitializeComponent()</c> has
/// run.</para></summary>
public partial class VideoPreview : UserControl
{
    public VideoPreview(VideoPreviewViewModel viewModel, MediaElementPlayer player)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(player);

        DataContext = viewModel;
        InitializeComponent();

        // The visual tree exists now — Player (the MediaElement) was created by InitializeComponent().
        // Attached here rather than on Loaded because there is nothing to wait for: the element is
        // already a real object the moment the XAML has parsed.
        player.Attach(Player);
    }
}
