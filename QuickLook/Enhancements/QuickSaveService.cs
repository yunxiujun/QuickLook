using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace QuickLook.Enhancements;

internal static class QuickSaveService
{
    internal sealed class AlreadySavedException : IOException
    {
        internal string ExistingPath { get; }
        internal AlreadySavedException(string path) : base("该文件已经收藏过。") => ExistingPath = path;
    }
    // ImageViewer's extension list; keep in sync when merging upstream.
    private static readonly HashSet<string> Images = new((".apng .ari .arw .avif .ani .bay .bmp .cap .cr2 .cr3 .crw .cur " +
        ".dcr .dcs .dds .dng .drf .dcm .dicom .eip .emf .erf .exr .fff .gif .hdr .heic .heif .ico .icon .icns .iiq " +
        ".jfif .jp2 .jpeg .jpg .jxl .j2k .jpf .jpx .jpm .jxr .k25 .kdc .mdc .mef .mos .mrw .mj2 .miff .nef .nrw " +
        ".obm .orf .pbm .pcx .pef .pgm .png .pnm .ppm .psb .psd .ptx .pxn .qoi .r3d .raf .raw .rw2 .rwl .rwz " +
        ".sr2 .srf .srw .svg .svgz .tga .tif .tiff .wdp .webp .wmf .x3f .xcf .xbm .xpm").Split(' '), StringComparer.OrdinalIgnoreCase);
    // VideoViewer detects content with MediaInfo; this list categorizes common video containers without opening a decoder.
    private static readonly HashSet<string> Videos = new((".mp4 .m4v .mkv .webm .avi .mov .qt .wmv .asf .flv .f4v .mpg .mpeg " +
        ".mpe .m1v .m2v .ts .mts .m2ts .vob .ogv .3gp .3g2 .rm .rmvb .divx .mod .tod .mxf .m2t .wtv .dvr-ms .qlv").Split(' '), StringComparer.OrdinalIgnoreCase);
    internal static string Category(string path) => Images.Contains(Path.GetExtension(path)) ? "图片" :
        Videos.Contains(Path.GetExtension(path)) ? "视频" : "其他文件";

    internal static async Task<string> CopyAsync(string source, string root)
    {
        if (!File.Exists(source)) throw new IOException("仅支持保存存在的文件，不能保存文件夹。");
        var directory = Path.Combine(Path.GetFullPath(root), Category(source));
        Directory.CreateDirectory(directory);
        var existing = FindIdenticalCopy(source, directory);
        if (existing != null) throw new AlreadySavedException(existing);
        // An exclusive temporary file keeps failed/partial copies out of the final namespace.
        var temporary = Path.Combine(directory, ".quicklook-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output).ConfigureAwait(false);
            for (var index = 0; ; index++)
            {
                var name = index == 0 ? Path.GetFileName(source) :
                    Path.GetFileNameWithoutExtension(source) + " (" + index + ")" + Path.GetExtension(source);
                var destination = Path.Combine(directory, name);
                try { File.Move(temporary, destination); return destination; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string FindIdenticalCopy(string source, string directory)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        foreach (var candidate in Directory.EnumerateFiles(directory, stem + "*" + extension, SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(candidate);
            if (!IsSavedVersionName(name, stem)) continue;
            if (FilesEqual(source, candidate)) return candidate;
        }
        return null;
    }

    private static bool IsSavedVersionName(string name, string stem)
    {
        if (string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = stem + " (";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(")", StringComparison.Ordinal)) return false;
        return int.TryParse(name.Substring(prefix.Length, name.Length - prefix.Length - 1), out var index) && index > 0;
    }

    private static bool FilesEqual(string left, string right)
    {
        var a = new FileInfo(left); var b = new FileInfo(right);
        if (a.Length != b.Length) return false;
        const int bufferSize = 1024 * 1024;
        var leftBuffer = new byte[bufferSize]; var rightBuffer = new byte[bufferSize];
        using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
        while (true)
        {
            var leftRead = leftStream.Read(leftBuffer, 0, bufferSize);
            var rightRead = rightStream.Read(rightBuffer, 0, bufferSize);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            for (var i = 0; i < leftRead; i++) if (leftBuffer[i] != rightBuffer[i]) return false;
        }
    }
}
