using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QuickLook.Enhancements;

internal static class PreviewEnhancements
{
    private static readonly List<ViewerWindow> Batch = [];
    private static readonly HashSet<Forms.Keys> Held = [];
    private static readonly HashSet<string> Saving = new(StringComparer.OrdinalIgnoreCase);
    private static ViewerWindow _active;
    private static IntPtr _source;
    private static string[] _selection = [];
    private static DispatcherTimer _seekTimer;
    private static ViewerWindow _seekWindow;
    private static Forms.Keys _seekKey;
    private static readonly HoldRepeat SeekRepeat = new();
    private static double _seekRate = 1d;
    private static long Now => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
    private static bool _opening;
    private static int _openGeneration;
    internal static bool HasBatch => Batch.Count > 0;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static void RecordSource() => _source = ExplorerSelection.GetForegroundWindow();
    internal static void Track(ViewerWindow window)
    {
        window.Activated += (_, _) => { StopSeek(); _active = window; UpdateSound(); };
        window.Closed += (_, _) =>
        {
            StopSeek(); Batch.Remove(window);
            if (_active == window) _active = Batch.FirstOrDefault();
            UpdateSound();
        };
    }

    private static ViewerWindow Target => _active != null && _active.IsVisible ? _active :
        ViewWindowManager.GetInstance().CurrentWindow;

    private static bool InScope()
    {
        var foreground = ExplorerSelection.GetForegroundWindow();
        var target = Target;
        if (target == null || !target.IsVisible) return false;
        if (foreground == new WindowInteropHelper(target).Handle)
        {
            var focused = Keyboard.FocusedElement;
            return focused is not TextBoxBase && focused is not System.Windows.Controls.PasswordBox &&
                focused is not System.Windows.Controls.ComboBox;
        }
        return foreground == _source &&
            NativeMethods.QuickLook.GetFocusedWindowType() != NativeMethods.QuickLook.FocusedWindowType.Invalid;
    }

    internal static bool Handle(Forms.KeyEventArgs e, bool down)
    {
        var key = e.KeyCode;
        // Consume paired releases even if focus changed since the key went down.
        if (!down && Held.Remove(key))
        {
            if (key == _seekKey) StopSeek();
            e.Handled = true; return true;
        }
        if (!down || e.Modifiers != Forms.Keys.None) return false;
        if (Held.Contains(key)) { e.Handled = true; return true; }
        if (_opening && key == Forms.Keys.Escape && ExplorerSelection.GetForegroundWindow() == _source)
        {
            _openGeneration++; _opening = false; Held.Add(key); e.Handled = true; CloseBatch(); return true;
        }
        var sourceType = key == Forms.Keys.Space ? NativeMethods.QuickLook.GetFocusedWindowType() :
            NativeMethods.QuickLook.FocusedWindowType.Invalid;
        if (key == Forms.Keys.Space && (sourceType == NativeMethods.QuickLook.FocusedWindowType.Explorer ||
            sourceType == NativeMethods.QuickLook.FocusedWindowType.Everything))
        {
            // Never perform Shell COM calls or load plugins inside WH_KEYBOARD_LL.
            var source = ExplorerSelection.GetForegroundWindow();
            _source = source; _opening = true;
            var generation = ++_openGeneration;
            Held.Add(key); e.Handled = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (generation != _openGeneration || ExplorerSelection.GetForegroundWindow() != source) return;
                    var selected = sourceType == NativeMethods.QuickLook.FocusedWindowType.Everything
                        ? await EverythingSelection.ReadAsync(source) : ExplorerSelection.Read(source);
                    if (generation != _openGeneration || ExplorerSelection.GetForegroundWindow() != source) return;
                    if (selected.Length > 1 || HasBatch) ToggleBatch(selected, source);
                    else if (selected.Length == 1) ViewWindowManager.GetInstance().TogglePreview(selected[0]);
                }
                catch (Exception error) { TrayIconManager.ShowNotification("无法预览", error.Message, true); }
                finally { if (generation == _openGeneration) _opening = false; }
            }), DispatcherPriority.Background);
            return true;
        }
        if (!InScope()) return false;
        if (HasBatch && (key == Forms.Keys.Escape || key == Forms.Keys.Space))
        {
            Held.Add(key); e.Handled = true;
            StopSeek();
            Application.Current.Dispatcher.BeginInvoke(new Action(CloseBatch), DispatcherPriority.Send);
            return true;
        }
        if (HasBatch && (key == Forms.Keys.Enter || key == Forms.Keys.F11))
        {
            Held.Add(key); e.Handled = true;
            var target = Target;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            { if (key == Forms.Keys.Enter) target.RunAndClose(); else target.ToggleFullscreen(); }));
            return true;
        }
        if (key == Forms.Keys.Insert)
        {
            Held.Add(key); e.Handled = true; Save(Target); return true;
        }
        if ((key == Forms.Keys.A || key == Forms.Keys.D) && Target.Plugin is IVideoPreviewControl { CanSeek: true })
        {
            Held.Add(key); e.Handled = true; StopSeek();
            _seekKey = key; _seekWindow = Target;
            _seekRate = 1d; (_seekWindow.Plugin as IVideoPreviewControl)?.SetPlaybackRate(1d);
            Seek(); SeekRepeat.Start(Now, EnhancementSettings.HoldDelay, EnhancementSettings.RepeatInterval);
            _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _seekTimer.Tick += (_, _) =>
            {
                if (!InScope() || Target != _seekWindow || !Held.Contains(_seekKey) ||
                    Keyboard.Modifiers != ModifierKeys.None) { StopSeek(); return; }
                if (SeekRepeat.Tick(Now))
                {
                    _seekRate = Math.Min(5d, _seekRate + 1d);
                    (_seekWindow.Plugin as IVideoPreviewControl)?.SetPlaybackRate(_seekRate);
                }
            };
            _seekTimer.Start(); return true;
        }
        return false;
    }

    private static void Seek() => (_seekWindow?.Plugin as IVideoPreviewControl)?.SeekRelative(
        TimeSpan.FromSeconds((_seekKey == Forms.Keys.A ? -1 : 1) * EnhancementSettings.StepSeconds));
    internal static void StopSeek() { SeekRepeat.Stop(); (_seekWindow?.Plugin as IVideoPreviewControl)?.SetPlaybackRate(1d); _seekRate = 1d; _seekTimer?.Stop(); _seekTimer = null; _seekWindow = null; }

    private static async void Save(ViewerWindow window)
    {
        var path = window.CurrentPath;
        if (!Saving.Add(path)) return;
        try
        {
            var root = EnhancementSettings.SaveRoot;
            var saved = await System.Threading.Tasks.Task.Run(() => QuickSaveService.CopyAsync(path, root));
            TrayIconManager.ShowNotification("已保存", saved);
        }
        catch (Exception e) { TrayIconManager.ShowNotification("保存失败", e.Message, true); }
        finally { Saving.Remove(path); }
    }

    internal static void CloseBatch()
    {
        StopSeek();
        foreach (var window in Batch.ToArray()) window.Close();
        Batch.Clear(); _selection = [];
    }

    private static void ToggleBatch(string[] paths, IntPtr source)
    {
        if (paths.Length > 9) { TrayIconManager.ShowNotification("最多预览 9 个文件", "请减少选择后重试。"); return; }
        var same = HasBatch && _selection.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase);
        CloseBatch();
        if (same || paths.Length == 0) return;
        _source = source;
        if (paths.Length == 1) { ViewWindowManager.GetInstance().InvokePreview(paths[0]); return; }
        ViewWindowManager.GetInstance().ClosePreview();
        FocusMonitor.GetInstance().Stop();
        _selection = paths;
        var area = Forms.Screen.FromHandle(source).WorkingArea;
        var columns = paths.Length == 2 ? 2 : paths.Length == 4 ? 2 : 3;
        var rows = (paths.Length + columns - 1) / columns;
        for (int i = 0; i < paths.Length; i++)
        {
            var window = new ViewerWindow { Pinned = true, ShowActivated = false };
            window.IsBatchPreview = true;
            Track(window); Batch.Add(window);
            window.MinWidth = 0; window.MinHeight = 0;
            var index = i;
            window.ContentRendered += (_, _) =>
            {
                if (window.IsPreviewClosed) return;
                var left = area.Left + index % columns * area.Width / columns;
                var top = area.Top + index / columns * area.Height / rows;
                SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, left + 4, top + 4,
                    area.Width / columns - 8, area.Height / rows - 8, 0x0014);
                window.ResizeMode = ResizeMode.CanResize;
            };
            try
            {
                var plugin = PluginManager.GetInstance().FindMatch(paths[i]);
                (plugin as IVideoPreviewControl)?.SetPreviewMuted(i != 0);
                window.BeginShow(plugin, paths[i], (_, error) =>
                {
                    if (window.IsPreviewClosed) return;
                    window.UnloadPlugin();
                    window.ContextObject.ViewerContent = new System.Windows.Controls.TextBlock
                    { Text = "无法预览：" + error.SourceException.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(15) };
                    window.ContextObject.IsBusy = false;
                });
            }
            catch (Exception e) { TrayIconManager.ShowNotification("无法预览", e.Message, true); window.Close(); }
        }
        _active = Batch.FirstOrDefault(); UpdateSound();
    }

    private static void UpdateSound()
    {
        foreach (var window in Batch)
            (window.Plugin as IVideoPreviewControl)?.SetPreviewMuted(window != _active);
    }
}
