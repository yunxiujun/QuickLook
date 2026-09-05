using QuickLook.Common.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace QuickLook.Enhancements;

internal static class EnhancementSettings
{
    private const string Domain = "QuickLook.Enhancements";
    internal static string SaveRoot => SettingHelper.Get("SaveRoot", Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickLook"), Domain);
    internal static int StepSeconds => Math.Max(1, Math.Min(600, SettingHelper.Get("StepSeconds", 5, Domain)));
    internal static int HoldDelay => Math.Max(100, Math.Min(2000, SettingHelper.Get("HoldDelay", 350, Domain)));
    internal static int RepeatInterval => Math.Max(100, Math.Min(2000, SettingHelper.Get("RepeatInterval", 200, Domain)));

    internal static void Show()
    {
        using var form = new Form { Text = "QuickLook 增强功能设置", Width = 620, Height = 310,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, AutoScaleMode = AutoScaleMode.Dpi };
        var root = new TextBox { Left = 20, Top = 45, Width = 450, Text = SaveRoot };
        var browse = new Button { Left = 480, Top = 43, Width = 95, Text = "选择目录" };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = root.Text };
            if (dialog.ShowDialog(form) == DialogResult.OK) root.Text = dialog.SelectedPath; };
        var open = new Button { Left = 20, Top = 85, Width = 120, Text = "打开保存目录" };
        open.Click += (_, _) => { try { Process.Start("explorer.exe", "\"" + Path.GetFullPath(root.Text) + "\""); }
            catch (Exception e) { System.Windows.Forms.MessageBox.Show(form, e.Message); } };
        var step = Number(form, "跳转秒数", 130, StepSeconds, 1, 600);
        var delay = Number(form, "长按延迟（毫秒）", 165, HoldDelay, 100, 2000);
        var repeat = Number(form, "重复间隔（毫秒）", 200, RepeatInterval, 100, 2000);
        var save = new Button { Left = 480, Top = 220, Width = 95, Text = "保存" };
        save.Click += (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root.Text) || !Path.IsPathRooted(root.Text))
                    throw new ArgumentException("请选择完整的保存目录路径。");
                var path = Path.GetFullPath(root.Text);
                Directory.CreateDirectory(path);
                SettingHelper.Set("SaveRoot", path, Domain);
                SettingHelper.Set("StepSeconds", (int)step.Value, Domain);
                SettingHelper.Set("HoldDelay", (int)delay.Value, Domain);
                SettingHelper.Set("RepeatInterval", (int)repeat.Value, Domain);
                form.Close();
            }
            catch (Exception e) { System.Windows.Forms.MessageBox.Show(form, e.Message, "无法保存设置"); }
        };
        form.Controls.AddRange([new Label { Left = 20, Top = 18, Width = 550,
            Text = "Ins 复制保存位置（自动分类为图片、视频、其他文件）" }, root, browse, open, save]);
        form.ShowDialog();
    }

    private static NumericUpDown Number(Form form, string label, int top, int value, int min, int max)
    {
        var input = new NumericUpDown { Left = 210, Top = top, Width = 110, Minimum = min, Maximum = max, Value = value };
        form.Controls.Add(new Label { Left = 20, Top = top + 3, Width = 185, Text = label });
        form.Controls.Add(input);
        return input;
    }
}
