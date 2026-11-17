using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LSwitcher
{
    [DataContract] public sealed class Shortcut
    {
        [DataMember] public int Key { get; set; }
        [DataMember] public int Modifiers { get; set; }
        public string Identity { get { return Key + ":" + Modifiers; } }
        public override string ToString()
        {
            if (Key == 0) return "";
            int standard = Modifiers & (int)(Keys.Control | Keys.Alt | Keys.Shift);
            return ((Modifiers & HotkeyEngine.WinModifier) != 0 ? "Win+" : "") + (standard == 0 ? "" : ((Keys)standard).ToString().Replace(", ", "+") + "+") + ((Keys)Key).ToString();
        }
    }
    [DataContract] public sealed class LayoutBinding
    {
        [DataMember] public string LayoutId { get; set; }
        [DataMember] public Shortcut Shortcut { get; set; } = new Shortcut();
    }
    [DataContract] public sealed class Exclusion
    {
        [DataMember] public bool FullPath { get; set; }
        [DataMember] public string Executable { get; set; }
        public override string ToString() { return (FullPath ? "Path: " : "Name: ") + Executable; }
        public bool Matches(string path) { return string.Equals(Executable, FullPath ? path : Path.GetFileName(path), StringComparison.OrdinalIgnoreCase); }
        internal static bool IsAbsolute(string path) { string root = Path.GetPathRoot(path); return Path.IsPathRooted(path) && root.Length > 1 && !root.EndsWith(":", StringComparison.Ordinal); }
    }
    public enum ConversionDirection { SwapBothWays, FirstToSecond, SecondToFirst }
    [DataContract] public sealed class LayoutLabelColor
    {
        [DataMember] public string LayoutId { get; set; }
        [DataMember(EmitDefaultValue = false)] public int? Argb { get; set; }
        [DataMember(EmitDefaultValue = false)] public int? BackgroundArgb { get; set; }
    }
    [DataContract] public sealed class Settings
    {
        [DataMember] public int SchemaVersion { get; set; } = 1;
        [DataMember] public int TapMilliseconds { get; set; } = 300;
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public List<LayoutBinding> Bindings { get; set; } = new List<LayoutBinding>();
        [DataMember] public List<Exclusion> Exclusions { get; set; } = new List<Exclusion>();
        [DataMember] public string FirstLayout { get; set; }
        [DataMember] public string SecondLayout { get; set; }
        [DataMember] public Shortcut ConversionShortcut { get; set; } = new Shortcut();
        [DataMember] public Shortcut PauseShortcut { get; set; } = new Shortcut();
        [DataMember] public Shortcut UppercaseShortcut { get; set; } = new Shortcut();
        [DataMember] public Shortcut LowercaseShortcut { get; set; } = new Shortcut();
        [DataMember] public Shortcut InvertCaseShortcut { get; set; } = new Shortcut();
        [DataMember] public List<LayoutLabelColor> LabelColors { get; set; } = new List<LayoutLabelColor>();
        [DataMember] public ConversionDirection Direction { get; set; }
        [DataMember] public bool DeselectAfterLayoutConversion { get; set; }
        [OnDeserialized] private void UpgradeOptionalFields(StreamingContext context)
        {
            PauseShortcut = PauseShortcut ?? new Shortcut(); UppercaseShortcut = UppercaseShortcut ?? new Shortcut();
            LowercaseShortcut = LowercaseShortcut ?? new Shortcut(); InvertCaseShortcut = InvertCaseShortcut ?? new Shortcut();
            LabelColors = LabelColors ?? new List<LayoutLabelColor>();
        }
        [DataMember(EmitDefaultValue = false)] public int? DefaultTextArgb { get; set; }
        [DataMember(EmitDefaultValue = false)] public int? DefaultBackgroundArgb { get; set; }
        private static bool ValidColor(int? value) { return !value.HasValue || (unchecked((uint)value.Value) >> 24) == 255; }
        public void Validate()
        {
            if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings schema version.");
            if (TapMilliseconds < 50 || TapMilliseconds > 2000) throw new InvalidDataException("Short-tap limit must be between 50 and 2000 ms.");
            if (Bindings == null || Exclusions == null || ConversionShortcut == null || PauseShortcut == null || UppercaseShortcut == null || LowercaseShortcut == null || InvertCaseShortcut == null || LabelColors == null) throw new InvalidDataException("Settings are incomplete.");
            if (!ValidColor(DefaultTextArgb) || !ValidColor(DefaultBackgroundArgb)) throw new InvalidDataException("Default tray colors must be opaque.");
            if (LabelColors.Any(c => c == null || string.IsNullOrWhiteSpace(c.LayoutId) || !ValidColor(c.Argb) || !ValidColor(c.BackgroundArgb)) || LabelColors.GroupBy(c => c.LayoutId).Any(g => g.Count() > 1)) throw new InvalidDataException("Each layout may have one opaque tray-label color.");
            var shortcuts = new List<Shortcut> { ConversionShortcut, PauseShortcut, UppercaseShortcut, LowercaseShortcut, InvertCaseShortcut };
            foreach (var b in Bindings) { if (b == null || string.IsNullOrWhiteSpace(b.LayoutId) || b.Shortcut == null) throw new InvalidDataException("Choose a layout for every binding."); shortcuts.Add(b.Shortcut); }
            foreach (var s in shortcuts)
                if (s.Key < 0 || s.Key > 254 || (s.Modifiers & ~((int)(Keys.Control | Keys.Alt | Keys.Shift) | HotkeyEngine.WinModifier)) != 0 || (HotkeyEngine.IsModifier(s.Key) && s.Modifiers != 0)) throw new InvalidDataException("Invalid shortcut.");
            if (shortcuts.Where(s => s.Key != 0).GroupBy(s => s.Identity).Any(g => g.Count() > 1)) throw new InvalidDataException("Conflicting assignments: each shortcut can have only one action.");
            if (!Enum.IsDefined(typeof(ConversionDirection), Direction)) throw new InvalidDataException("Invalid conversion direction.");
            if (ConversionShortcut.Key != 0 && (string.IsNullOrEmpty(FirstLayout) || string.IsNullOrEmpty(SecondLayout) || FirstLayout == SecondLayout)) throw new InvalidDataException("Choose two different conversion layouts.");
            foreach (var e in Exclusions) if (e == null || string.IsNullOrWhiteSpace(e.Executable) || (e.FullPath ? !Exclusion.IsAbsolute(e.Executable) : Path.GetFileName(e.Executable) != e.Executable)) throw new InvalidDataException("Exclusions require an executable name or an absolute path.");
        }
        public Settings Clone() { using (var stream = new MemoryStream()) { Serializer.WriteObject(stream, this); stream.Position = 0; return (Settings)Serializer.ReadObject(stream); } }
        internal System.Drawing.Color LabelColor(string layoutId)
        {
            var entry = LabelColors.FirstOrDefault(c => c.LayoutId == layoutId);
            return System.Drawing.Color.FromArgb(entry?.Argb ?? DefaultTextArgb ?? System.Drawing.SystemColors.Window.ToArgb());
        }
        internal System.Drawing.Color LabelBackground(string layoutId)
        {
            var entry = LabelColors.FirstOrDefault(c => c.LayoutId == layoutId);
            return System.Drawing.Color.FromArgb(entry?.BackgroundArgb ?? DefaultBackgroundArgb ?? System.Drawing.SystemColors.WindowText.ToArgb());
        }
        internal static DataContractJsonSerializer Serializer { get { return new DataContractJsonSerializer(typeof(Settings)); } }
    }
    internal sealed class SettingsStore
    {
        internal readonly string FilePath;
        internal string LoadWarning;
        internal bool Exists { get { return File.Exists(FilePath); } }
        internal SettingsStore(string path = null) { FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "L-Switcher", "settings.json"); }
        internal Settings Load()
        {
            if (!Exists) return new Settings();
            try { return Read(FilePath); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is SerializationException || ex is InvalidOperationException || ex is ArgumentException)
            {
                LoadWarning = "Settings could not be loaded: " + ex.Message;
                try { var recovered = Read(FilePath + ".bak"); LoadWarning += "\nThe previous valid backup was loaded."; return recovered; }
                catch { LoadWarning += "\nDefault settings were loaded. The original file is retained until you save."; return new Settings(); }
            }
        }
        private static Settings Read(string path) { using (var stream = File.OpenRead(path)) { var result = (Settings)Settings.Serializer.ReadObject(stream); if (result == null) throw new InvalidDataException("Settings are empty."); result.Validate(); return result; } }
        internal void Save(Settings settings)
        {
            settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { Settings.Serializer.WriteObject(stream, settings); stream.Flush(true); }
                if (Exists)
                {
                    bool valid = false; try { Read(FilePath); valid = true; } catch { }
                    File.Replace(temp, FilePath, valid ? FilePath + ".bak" : null);
                }
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            { if (enabled) key.SetValue("L-Switcher", "\"" + Application.ExecutablePath + "\""); else key.DeleteValue("L-Switcher", false); }
        }
    }
}
