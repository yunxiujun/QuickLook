using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace QuickLook.Enhancements;

// One reusable, non-activating surface. Shell balloon tips queue rapid saves.
internal sealed class SaveNotification : Window
{
    private static SaveNotification _current;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly DispatcherTimer _dismiss;
    private int _pending, _completed, _failed;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private SaveNotification()
    {
        Width = 360; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(35, 38, 44));
        Foreground = Brushes.White; Focusable = false;
        _title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
        _detail = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 65, TextTrimming = TextTrimming.CharacterEllipsis };
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(_title); body.Children.Add(_detail); Content = body;
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _dismiss.Tick += (_, _) => { _dismiss.Stop(); Hide(); };
        MouseLeftButtonDown += (_, _) => { _dismiss.Stop(); Hide(); };
        Closed += (_, _) => { _dismiss.Stop(); _current = null; };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x00000080);
        };
    }

    internal static void Started(string path)
    {
        var notice = _current ??= new SaveNotification();
        if (!notice.IsVisible && notice._pending == 0) notice._completed = notice._failed = 0;
        notice._pending++;
        notice.Update("正在收藏：" + System.IO.Path.GetFileName(path));
    }

    internal static void Finished(string message, bool failed)
    {
        var notice = _current;
        if (notice == null) return;
        notice._pending = Math.Max(0, notice._pending - 1);
        if (failed) notice._failed++; else notice._completed++;
        notice.Update(message);
    }

    private void Update(string detail)
    {
        _dismiss.Stop();
        _title.Text = $"已收藏 {_completed} 个" + (_pending > 0 ? $" · 正在保存 {_pending} 个" : "") +
            (_failed > 0 ? $" · 失败 {_failed} 个" : "");
        _detail.Text = detail;
        if (!IsVisible) Show();
        UpdateLayout();
        var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var bottomRight = scale.Transform(new Point(area.Right - 16, area.Bottom - 16));
        Left = bottomRight.X - ActualWidth; Top = bottomRight.Y - ActualHeight;
        _dismiss.Interval = TimeSpan.FromMilliseconds(_failed > 0 ? 4000 : 1800);
        _dismiss.Start();
    }
}
