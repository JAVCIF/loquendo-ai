using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LoquendoAI.App;

/// <summary>
/// Timeline of the scene preview (1.1.1). Pressing anywhere on the bar jumps there and dragging scrubs, also over
/// the ball; the arrow keys move 1 s and Page Up / Page Down 5 s. While the user holds the bar the playback timer
/// does not move it back, and a playing video pauses during the drag and continues from the new point.
/// </summary>
public partial class MainWindow
{
    private bool _seekDragging;
    private bool _seekResumeAfterDrag;
    /// <summary>True while the code (the playback timer, a new video) moves the slider: that is not a seek.</summary>
    private bool _updatingSeekSlider;
    private long _lastScrubTicks;
    private long _seekSettleUntil;

    private void SceneSeekSlider_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SceneSeekSlider.IsEnabled) return;
        if (_scenePreviewPath is null)
        {
            e.Handled = true; // no video yet: the ball stays at the start
            return;
        }
        // Handled here so neither the Thumb nor the page buttons of the Track take the press: one behaviour for
        // the whole bar (jump to the point and keep following the mouse).
        e.Handled = true;
        SceneSeekSlider.Focus();
        _seekDragging = true;
        _seekResumeAfterDrag = _scenePreviewPlaying;
        if (_scenePreviewPlaying)
        {
            ScenePreviewPlayer.Pause();
            _scenePreviewPlaying = false;
        }
        if (!SceneSeekSlider.CaptureMouse())
        {
            // No capture, no drag (a stuck drag would freeze the timer): just jump to the point.
            _seekDragging = false;
            ScrubTo(e, force: true);
            EndSeekDragResume();
            return;
        }
        ScrubTo(e, force: true);
    }

    private void SceneSeekSlider_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_seekDragging || e.LeftButton != MouseButtonState.Pressed) return;
        ScrubTo(e, force: false);
    }

    private void SceneSeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seekDragging) return;
        e.Handled = true;
        ScrubTo(e, force: true);
        EndSeekDrag(resume: true);
    }

    private void SceneSeekSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Alt+Tab or a dialog during the drag: keep the point where the ball is (the last scrub may have been throttled).
        if (!_seekDragging) return;
        SeekScenePreview(SceneSeekSlider.Value);
        EndSeekDrag(resume: true);
    }

    /// <summary>Keyboard and any other change made by the user (the mouse goes through <see cref="ScrubTo"/>).</summary>
    private void SceneSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeekSlider || _seekDragging || _scenePreviewPath is null) return;
        SeekScenePreview(e.NewValue);
        // Right after a seek MediaElement can still report the old position for a moment: let the timer wait so
        // it does not pull the ball back (and the next arrow press starts from the new point).
        _seekSettleUntil = Environment.TickCount64 + 300;
    }

    private void ScrubTo(MouseEventArgs e, bool force)
    {
        if (SceneSeekSlider.Template?.FindName("PART_Track", SceneSeekSlider) is not Track track || track.ActualWidth <= 0) return;
        var value = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), SceneSeekSlider.Minimum, SceneSeekSlider.Maximum);
        _updatingSeekSlider = true;
        try { SceneSeekSlider.Value = value; }
        finally { _updatingSeekSlider = false; }
        ShowScenePreviewTime(value);
        // Decoding a frame per mouse event would lag on long videos: about 16 frames per second while dragging,
        // and always the exact point on press and release.
        var now = Environment.TickCount64;
        if (!force && now - _lastScrubTicks < 60) return;
        _lastScrubTicks = now;
        SeekScenePreview(value);
    }

    private void SeekScenePreview(double milliseconds)
    {
        if (_scenePreviewPath is null) return;
        var target = Math.Clamp(milliseconds, 0, SceneSeekSlider.Maximum);
        // Stopped (■) or finished: Pause first (a stopped MediaElement may ignore Position), so the frame of the
        // new point shows (ScrubbingEnabled) without playing.
        if (!_scenePreviewPlaying) ScenePreviewPlayer.Pause();
        ScenePreviewPlayer.Position = TimeSpan.FromMilliseconds(target);
        ShowScenePreviewTime(target);
    }

    private void EndSeekDrag(bool resume)
    {
        if (!_seekDragging) return;
        _seekDragging = false;
        _seekSettleUntil = Environment.TickCount64 + 300;
        if (SceneSeekSlider.IsMouseCaptured) SceneSeekSlider.ReleaseMouseCapture();
        if (resume) EndSeekDragResume();
        else _seekResumeAfterDrag = false;
    }

    private void EndSeekDragResume()
    {
        if (!_seekResumeAfterDrag) return;
        _seekResumeAfterDrag = false;
        // Dropped at the very end (or the preview went away): stay paused there.
        if (_scenePreviewPath is null || SceneSeekSlider.Value >= SceneSeekSlider.Maximum - 1)
        {
            ToggleScenePreviewButton.Content = "▶ Reproducir";
            return;
        }
        ScenePreviewPlayer.Play();
        _scenePreviewPlaying = true;
        ToggleScenePreviewButton.Content = "❚❚ Pausar";
    }

    private void UpdateScenePreviewTime()
    {
        if (_scenePreviewPath is null || ScenePreviewPlayer is null || _seekDragging ||
            Environment.TickCount64 < _seekSettleUntil) return;
        var current = Math.Clamp(ScenePreviewPlayer.Position.TotalMilliseconds, 0, SceneSeekSlider.Maximum);
        _updatingSeekSlider = true;
        try { SceneSeekSlider.Value = current; }
        finally { _updatingSeekSlider = false; }
        ShowScenePreviewTime(current);
    }

    private void ShowScenePreviewTime(double current) =>
        ScenePreviewTimeText.Text = $"{FormatTimelineTime((long)current)} / {FormatTimelineTime((long)SceneSeekSlider.Maximum)}";
}
