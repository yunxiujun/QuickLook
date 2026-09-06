using System;

namespace QuickLook.Common.Plugin;

/// <summary>Optional capability. Existing third-party IViewer implementations remain compatible.</summary>
public interface IVideoPreviewControl
{
    bool CanSeek { get; }
    void SeekRelative(TimeSpan offset);
    void SetPreviewMuted(bool muted);
    void ShowPlaybackControls();
    void HidePlaybackControls();
    void SetPlaybackRate(double rate);
}
