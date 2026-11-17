using System;
using System.Drawing;
using System.IO;

namespace LSwitcher
{
    internal static partial class Tests
    {
        private static void TestColorConfigurationRecovery()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "color-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            // Regression: actual background-only entry format emitted by the color editor.
            const string backgroundOnly = "{\"SchemaVersion\":1,\"TapMilliseconds\":300,\"Bindings\":[{\"LayoutId\":\"F0C00409\",\"Shortcut\":{\"Key\":162,\"Modifiers\":0}}],\"Exclusions\":[],\"ConversionShortcut\":{\"Key\":0,\"Modifiers\":0},\"LabelColors\":[{\"BackgroundArgb\":-15531841,\"LayoutId\":\"F0C00409\"},{\"BackgroundArgb\":-8257533,\"LayoutId\":\"04190419\"}]}";
            try
            {
                File.WriteAllText(path, backgroundOnly);
                var store = new SettingsStore(path); var settings = store.Load();
                Check(store.LoadWarning == null, "Background-only color settings load without recovery or errors"); Equal(2, settings.LabelColors.Count, "Both background overrides retained");
                Equal(-15531841, settings.LabelBackground("F0C00409").ToArgb(), "English background retained"); Equal(-8257533, settings.LabelBackground("04190419").ToArgb(), "Russian background retained");
                Equal(SystemColors.Window.ToArgb(), settings.LabelColor("F0C00409").ToArgb(), "Absent text override inherits default"); Equal(162, settings.Bindings[0].Shortcut.Key, "Color migration keeps hotkey assignments");
                Equal(backgroundOnly, File.ReadAllText(path), "Loading does not rewrite configuration");
                store.Save(settings); var reloaded = store.Load(); Check(store.LoadWarning == null, "Background-only settings remain valid after saving"); Equal(-8257533, reloaded.LabelBackground("04190419").ToArgb(), "Background survives disk round trip");
                Equal(backgroundOnly, File.ReadAllText(path + ".bak"), "Original background-only settings remain a valid backup");
                var invalid = settings.Clone(); invalid.LabelColors.Add(new LayoutLabelColor { LayoutId = "F0C00409", Argb = Color.Red.ToArgb() });
                using (var stream = File.Create(path)) Settings.Serializer.WriteObject(stream, invalid);
                string broken = File.ReadAllText(path); var recovery = new SettingsStore(path); var recovered = recovery.Load();
                Check(recovery.LoadWarning != null && recovery.LoadWarning.Contains("backup"), "Semantic validation errors recover through the backup"); Equal(2, recovered.LabelColors.Count, "Duplicate color entry does not crash or discard valid backup colors");
                Equal(broken, File.ReadAllText(path), "Recovery leaves invalid original available for inspection");
                invalid.LabelColors.RemoveAt(invalid.LabelColors.Count - 1); invalid.LabelColors[0].Argb = 0;
                using (var stream = File.Create(path)) Settings.Serializer.WriteObject(stream, invalid);
                recovery = new SettingsStore(path); recovered = recovery.Load(); Check(recovery.LoadWarning != null, "Transparent color validation failure is handled"); Equal(-15531841, recovered.LabelBackground("F0C00409").ToArgb(), "Invalid opacity recovers valid background");
                File.Copy(path, path + ".bak", true); recovery = new SettingsStore(path); recovered = recovery.Load();
                Check(recovery.LoadWarning.Contains("Default settings"), "Invalid primary and backup load defaults with a warning"); Equal(0, recovered.Bindings.Count, "Default recovery does not create hotkeys");
                File.WriteAllText(path, "null"); recovery = new SettingsStore(path); recovered = recovery.Load(); Check(recovery.LoadWarning != null, "Null JSON configuration is recovered instead of crashing");
                var legacy = new Settings(); legacy.LabelColors.Add(new LayoutLabelColor { LayoutId = "legacy", Argb = Color.Red.ToArgb() }); store.Save(legacy); reloaded = store.Load();
                Equal(Color.Red.ToArgb(), reloaded.LabelColor("legacy").ToArgb(), "Legacy foreground-only color remains valid"); Equal(SystemColors.WindowText.ToArgb(), reloaded.LabelBackground("legacy").ToArgb(), "Legacy entry inherits background");
            }
            finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
