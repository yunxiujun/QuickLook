using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuickLook.Enhancements;

internal static class ExplorerSelection
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();

    internal static string[] Read(IntPtr source)
    {
        if (NativeMethods.QuickLook.GetFocusedWindowType() != NativeMethods.QuickLook.FocusedWindowType.Explorer)
            return [];
        object shell = null, windows = null;
        var candidates = new List<string[]>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            windows = ((dynamic)shell).Windows();
            for (int i = 0; i < (int)((dynamic)windows).Count; i++)
            {
                object window = null, document = null, selected = null;
                try
                {
                    window = ((dynamic)windows).Item(i);
                    if (new IntPtr((long)((dynamic)window).HWND) != source) continue;
                    document = ((dynamic)window).Document;
                    selected = ((dynamic)document).SelectedItems();
                    var paths = new List<string>();
                    for (int j = 0; j < (int)((dynamic)selected).Count; j++)
                    {
                        object item = null;
                        try { item = ((dynamic)selected).Item(j); paths.Add((string)((dynamic)item).Path); }
                        finally { Release(item); }
                    }
                    if (paths.Count > 0) candidates.Add(paths.ToArray());
                }
                finally { Release(selected); Release(document); Release(window); }
            }
            if (candidates.Count == 1) return candidates[0];
            // Never open another Explorer tab's selection when COM exposes multiple candidates.
            if (candidates.Count > 1) throw new InvalidOperationException("无法确定当前资源管理器标签页，请在单独窗口中重试。");
            return [];
        }
        finally { Release(windows); Release(shell); }
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
