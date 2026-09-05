using System;
using System.Collections.Generic;
using System.IO;

namespace QuickLook.Enhancements;

internal static class SelectionPaths
{
    internal static string[] Parse(string text)
    {
        var paths = new List<string>();
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = line.Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            if (!Path.IsPathRooted(path) || (!File.Exists(path) && !Directory.Exists(path)))
                throw new IOException("Everything 所选文件不存在或无法访问，请刷新结果后重试。");
            if (!paths.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) paths.Add(path);
            // Ten is enough for the common batch limit to reject the entire selection.
            if (paths.Count == 10) break;
        }
        return paths.ToArray();
    }
}
