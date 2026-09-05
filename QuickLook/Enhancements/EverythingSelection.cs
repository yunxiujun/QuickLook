using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickLook.Enhancements;

internal static class EverythingSelection
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    internal static async Task<string[]> ReadAsync(IntPtr source)
    {
        if (ExplorerSelection.GetForegroundWindow() != source ||
            NativeMethods.QuickLook.GetFocusedWindowType() != NativeMethods.QuickLook.FocusedWindowType.Everything)
            return [];

        // Everything's documented command copies ALL selected full paths (1.4 and 1.5).
        // Materialize existing formats before issuing it: an IDataObject alone may be delayed-rendered.
        var before = GetClipboardSequenceNumber();
        var previous = Clipboard.GetDataObject();
        var snapshot = new DataObject();
        if (previous != null)
            foreach (var format in previous.GetFormats(false))
            {
                var value = previous.GetData(format, false);
                if (value == null) throw new IOException("无法备份当前剪贴板，请稍后重试。");
                snapshot.SetData(format, false, value);
            }
        if (GetClipboardSequenceNumber() != before) throw new IOException("剪贴板正在被其他程序使用，请重试。");
        uint copiedSequence = 0;
        try
        {
            if (SendMessageTimeout(source, 0x0111, new IntPtr(41007), IntPtr.Zero, 0x0002, 1000, out _) == IntPtr.Zero)
                throw new IOException("Everything 未响应，请确认 QuickLook 与 Everything 的运行权限一致。");
            GetWindowThreadProcessId(source, out var sourceProcess);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var sequence = GetClipboardSequenceNumber();
                GetWindowThreadProcessId(GetClipboardOwner(), out var clipboardProcess);
                if (sequence != before && clipboardProcess == sourceProcess)
                {
                    copiedSequence = sequence;
                    try
                    {
                        var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                        if (GetClipboardSequenceNumber() != sequence) throw new IOException("选择读取期间剪贴板已改变，请重试。");
                        return SelectionPaths.Parse(text);
                    }
                    catch (ExternalException) when (attempt < 9) { }
                }
                await Task.Delay(20);
            }
            throw new IOException("未能读取 Everything 所选文件，请在结果列表中选择文件后重试。");
        }
        finally
        {
            // Do not overwrite a newer clipboard entry made by the user or another application.
            if (copiedSequence != 0 && GetClipboardSequenceNumber() == copiedSequence)
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (GetClipboardSequenceNumber() != copiedSequence) break;
                        if (previous == null) Clipboard.Clear();
                        else Clipboard.SetDataObject(snapshot, true);
                        break;
                    }
                    catch (ExternalException) when (attempt < 9) { await Task.Delay(20); }
                }
            }
        }
    }
}
