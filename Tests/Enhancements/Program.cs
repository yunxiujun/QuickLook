using QuickLook.Enhancements;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

var root = Path.Combine(Path.GetTempPath(), "quicklook-save-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var hold = new HoldRepeat();
hold.Start(0, 350, 200);
Check(!hold.Tick(349), "Short press must not repeat");
Check(hold.Tick(350), "Held key starts repeating after delay");
for (var time = 550; time <= 5550; time += 200)
    Check(hold.Tick(time), "Hold continues without OS key-repeat or async key state");
hold.Stop();
Check(!hold.Tick(10000), "Release or context change stops repeating");
hold.Start(11000, 350, 200);
hold.Stop();
Check(!hold.Tick(11350), "Tap released before delay stays a single seek");
hold.Start(12000, 350, 200);
Check(hold.Tick(20000) && !hold.Tick(20000), "Delayed UI tick does not cause catch-up seek storm");
hold.Stop();
Console.WriteLine("PASS: continuous held seek, tap, release/cancel and delayed-tick behavior");
try
{
    var input = Path.Combine(root, "输入"); Directory.CreateDirectory(input);
    var output = Path.Combine(root, "输出");
    foreach (var pair in new[] { ("图片.JPG", "图片"), ("电影.mkv", "视频"), ("音乐.mp3", "其他文件"), ("说明.txt", "其他文件") })
    {
        var source = Path.Combine(input, pair.Item1);
        var bytes = new byte[1024 * 1024 + 17]; new Random(42).NextBytes(bytes); File.WriteAllBytes(source, bytes);
        var copies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => QuickSaveService.CopyAsync(source, output)));
        Check(copies.Distinct().Count() == 4, "Concurrent copies must not overwrite");
        foreach (var copy in copies)
        { Check(Path.GetFileName(Path.GetDirectoryName(copy)) == pair.Item2, "Category"); Check(File.ReadAllBytes(copy).SequenceEqual(bytes), "Copy integrity"); }
        Check(File.ReadAllBytes(source).SequenceEqual(bytes), "Source preserved");
    }
    await Fails(() => QuickSaveService.CopyAsync(input, output));
    await Fails(() => QuickSaveService.CopyAsync(Path.Combine(input, "missing"), output));
    var blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "unchanged");
    await Fails(() => QuickSaveService.CopyAsync(Path.Combine(input, "说明.txt"), blocked));
    Check(!Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "No partial files after failure");
    Check(File.ReadAllText(blocked) == "unchanged", "Existing target preserved");
    var selected = SelectionPaths.Parse("\"" + Path.Combine(input, "图片.JPG") + "\"\r\n" + Path.Combine(input, "电影.mkv"));
    Check(selected.Length == 2 && selected[0].EndsWith("图片.JPG"), "Everything quoted unicode selection order");
    Check(SelectionPaths.Parse(string.Join("\n", selected.Concat(selected))).Length == 2, "Everything selection deduplication");
    var many = Enumerable.Range(0, 12).Select(i => Path.Combine(input, i + ".txt")).ToArray();
    foreach (var file in many) File.WriteAllText(file, "fixture");
    Check(SelectionPaths.Parse(string.Join("\r\n", many)).Length == 10, "Everything rejects over-limit selection");
    await Fails(() => Task.FromResult(SelectionPaths.Parse("not a file")[0]));
    Console.WriteLine("PASS: Everything quoted/unicode paths, selection order, duplicates, over-limit and invalid input");
    Console.WriteLine("PASS: categorization, unicode paths, concurrent collisions, byte integrity, source preservation, invalid source/target, partial cleanup");
}
finally { Directory.Delete(root, true); }
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Fails(Func<Task<string>> action)
{ try { await action(); } catch (IOException) { return; } throw new Exception("Expected IO failure"); }
