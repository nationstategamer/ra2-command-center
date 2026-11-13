using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Ra2CommandCenter;

internal static class LauncherCoreTests
{
    private static int passed;
    private static int failed;
    private static string runRoot;
    private static readonly string[] Configs = { "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" };

    private sealed class Fixture : IDisposable
    {
        public string Root;
        public LauncherCore Core;
        public Dictionary<string, byte[]> Original = new Dictionary<string, byte[]>();
        public Fixture()
        {
            Root = Path.Combine(runRoot, Guid.NewGuid().ToString("N").Substring(0, 5));
            Directory.CreateDirectory(Root);
            string ra2 = "; Original commander preferences\r\n[Intro]\r\nPlay=yes\r\n\r\n[Options]\r\nGameSpeed=3\r\nScrollRate=4\r\nAutoScroll=yes\r\nUnknownOption=keep me\r\n\r\n[Video]\r\nScreenWidth = 640 ; screen width note\r\nScreenHeight=480\r\nStretchMovies=no\r\nVideoBackBuffer=no\r\n\r\n[Audio]\r\nSoundVolume=0.700000\r\nVoiceVolume=0.800000\r\nScoreVolume=0.600000\r\nInGameMusic=yes\r\n\r\n[Network]\r\nNetID=ffff,ffff,ffff,\r\n";
            string yr = ra2.Replace("\r\n", "\n").Replace("; Original commander preferences", "; Yuri preferences: \u00e9\u03a9");
            File.WriteAllBytes(Path.Combine(Root, "RA2.INI"), Encoding.GetEncoding(1252).GetBytes(ra2));
            File.WriteAllText(Path.Combine(Root, "RA2MD.INI"), yr, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(Root, "DDrawCompat.ini"), "# preserve renderer notes\r\nCpuAffinity = 1\r\nDisplayFilter = point(0)\r\nSupportedResolutions = 640x400, 640x480, 800x600, 1024x576, native\r\nVSync = off\r\nSurfacePatches = top:64, bottom:64\r\n", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(Root, "game.exe"), new byte[] { 77, 90, 1 });
            File.WriteAllBytes(Path.Combine(Root, "gamemd.exe"), new byte[] { 77, 90, 2 });
            File.WriteAllBytes(Path.Combine(Root, "ddraw.dll"), new byte[] { 77, 90, 3, 4 });
            File.WriteAllBytes(Path.Combine(Root, "SAVE0.SAV"), new byte[] { 11, 22, 33 });
            Directory.CreateDirectory(Path.Combine(Root, "SavesDir"));
            File.WriteAllBytes(Path.Combine(Root, "SavesDir", "campaign.sav"), new byte[] { 44, 55, 66 });
            foreach (string name in Configs) Original[name] = File.ReadAllBytes(Path.Combine(Root, name));
            Core = new LauncherCore(Root, Path.Combine(Root, "RA2CommandCenter"), true);
        }
        public void Dispose() { }
        public byte[] Bytes(string name) { return File.ReadAllBytes(Path.Combine(Root, name)); }
        public string Text(string name) { return File.ReadAllText(Path.Combine(Root, name)); }
        public void AssertOriginal()
        {
            foreach (string name in Configs) EqualBytes(Original[name], Bytes(name), name + " restored byte-for-byte");
        }
        public GameSettings Profile(bool yuri, int width, int height)
        {
            GameSettings s = Core.ReadSettings(yuri);
            s.Yuri = yuri; s.Width = width; s.Height = height;
            s.DisplayMode = "borderless"; s.Filter = "point";
            return s;
        }
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
    private static void EqualBytes(byte[] expected, byte[] actual, string message)
    {
        Assert(expected.SequenceEqual(actual), message);
    }
    private static void Throws(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        Assert(threw, message);
    }
    private static string SnapshotFile(string snapshot, string relative)
    {
        string direct = Path.Combine(snapshot, relative);
        if (File.Exists(direct)) return direct;
        string[] candidates = Directory.GetFiles(snapshot, Path.GetFileName(relative), SearchOption.AllDirectories);
        Assert(candidates.Length == 1, "Unique backup payload for " + relative);
        return candidates[0];
    }
    private static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.ToString()); }
    }

    public static int Main(string[] args)
    {
        runRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(runRoot);

        Test("Baseline includes configs, wrapper and saves; remains immutable", delegate {
            using (Fixture f = new Fixture()) {
                string baseline = f.Core.EnsureBaseline();
                f.Core.ValidateSnapshot(baseline);
                foreach (string n in Configs) EqualBytes(f.Original[n], File.ReadAllBytes(SnapshotFile(baseline, n)), "Initial " + n);
                Assert(File.Exists(SnapshotFile(baseline, "ddraw.dll")), "Renderer backup");
                Assert(File.Exists(SnapshotFile(baseline, "SAVE0.SAV")), "Root save backup");
                Assert(File.Exists(SnapshotFile(baseline, "SavesDir\\campaign.sav")), "Nested save backup");
                byte[] manifest = File.ReadAllBytes(Path.Combine(baseline, "manifest.json"));
                f.Core.Apply(f.Profile(false, 1280, 720));
                Assert(f.Core.EnsureBaseline() == baseline, "Same baseline path");
                EqualBytes(manifest, File.ReadAllBytes(Path.Combine(baseline, "manifest.json")), "Baseline manifest unchanged");
            }
        });
        Test("Selected-game profile and shared renderer preserve unrelated settings", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                GameSettings s = f.Profile(false, 1280, 720);
                s.ScrollRate = 5; s.PlayIntro = false; s.AutoScroll = false;
                s.SoundVolume = 0.25; s.VoiceVolume = 0.35; s.ScoreVolume = 0.45; s.InGameMusic = false;
                string before = f.Core.Apply(s);
                f.Core.ValidateSnapshot(before);
                EqualBytes(f.Original["RA2.INI"], File.ReadAllBytes(SnapshotFile(before, "RA2.INI")), "Snapshot is pre-write");
                GameSettings actual = f.Core.ReadSettings(false);
                Assert(actual.Width == 1280 && actual.Height == 720, "Target dimensions");
                Assert(actual.ScrollRate == 5 && !actual.PlayIntro && !actual.AutoScroll && !actual.InGameMusic, "Gameplay preferences");
                Assert(Math.Abs(actual.SoundVolume - 0.25) < 0.00001 && Math.Abs(actual.VoiceVolume - 0.35) < 0.00001 && Math.Abs(actual.ScoreVolume - 0.45) < 0.00001, "Audio preferences");
                EqualBytes(f.Original["RA2MD.INI"], f.Bytes("RA2MD.INI"), "Other game untouched");
                string ini = f.Text("RA2.INI");
                Assert(ini.Contains("UnknownOption=keep me") && ini.Contains("NetID=ffff,ffff,ffff,") && ini.Contains("VideoBackBuffer=no") && ini.Contains("GameSpeed=3"), "Unrelated keys preserved");
                Assert(ini.Contains("; screen width note") && ini.Contains("; Original commander preferences"), "Comments preserved");
                Assert(!ini.Replace("\r\n", "").Contains("\n"), "CRLF preserved");
                string wrapper = f.Text("DDrawCompat.ini");
                Assert(wrapper.Contains("1024x576") && wrapper.Contains("1280x720") && wrapper.Contains("1600x900") && wrapper.Contains("1920x1080"), "Existing and new modes available");
                Assert(wrapper.Contains("CpuAffinity = 1") && wrapper.Contains("SurfacePatches = top:64, bottom:64") && wrapper.Contains("# preserve renderer notes"), "Compatibility settings preserved");
                Assert(f.Core.ReadSettings(true).DisplayMode == "borderless", "Renderer shared across both games");
            }
        });
        Test("UTF-8 BOM, Unicode comments and LF survive Yuri edits", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.Apply(f.Profile(true, 1600, 900));
                byte[] data = f.Bytes("RA2MD.INI");
                Assert(data.Length > 3 && data[0] == 239 && data[1] == 187 && data[2] == 191, "UTF-8 BOM preserved");
                string text = f.Text("RA2MD.INI");
                Assert(text.Contains("\u00e9\u03a9") && !text.Contains("\r\n"), "Unicode and LF preserved");
                EqualBytes(f.Original["RA2.INI"], f.Bytes("RA2.INI"), "RA2 untouched");
            }
        });
        Test("Original restore is byte exact and snapshots the replaced profile", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                f.Core.Apply(f.Profile(false, 1920, 1080));
                byte[] changed = f.Bytes("RA2.INI");
                string snapshot = f.Core.RestoreOriginal();
                f.AssertOriginal();
                f.Core.ValidateSnapshot(snapshot);
                EqualBytes(changed, File.ReadAllBytes(SnapshotFile(snapshot, "RA2.INI")), "Restore protects outgoing profile");
                EqualBytes(new byte[] {11,22,33}, f.Bytes("SAVE0.SAV"), "Saves remain untouched");
            }
        });
        Test("Undo returns to previous applied settings", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.Apply(f.Profile(false, 1280, 720));
                byte[] first = f.Bytes("RA2.INI");
                byte[] firstWrapper = f.Bytes("DDrawCompat.ini");
                f.Core.Apply(f.Profile(false, 1920, 1080));
                Assert(f.Core.HasPreviousSnapshot, "Undo available");
                f.Core.RestorePrevious();
                EqualBytes(first, f.Bytes("RA2.INI"), "Undo exact prior game settings");
                EqualBytes(firstWrapper, f.Bytes("DDrawCompat.ini"), "Undo exact prior renderer settings");
            }
        });
        Test("Invalid settings cannot modify any configuration", delegate {
            using (Fixture f = new Fixture()) {
                GameSettings s = f.Profile(false, 0, 720);
                Throws(delegate { f.Core.Apply(s); }, "Zero width rejected");
                s = f.Profile(false, 1280, 720); s.SoundVolume = double.NaN;
                Throws(delegate { f.Core.Apply(s); }, "NaN audio rejected");
                s = f.Profile(false, 1280, 720); s.DisplayMode = "unexpected\nInjected=yes";
                Throws(delegate { f.Core.Apply(s); }, "Mode injection rejected");
                s = f.Profile(false, 1280, 720); s.Filter = "unexpected";
                Throws(delegate { f.Core.Apply(s); }, "Unknown filter rejected");
                f.AssertOriginal();
            }
        });
        Test("Corrupt backup rejected before any restoration write", delegate {
            using (Fixture f = new Fixture()) {
                string baseline = f.Core.EnsureBaseline();
                f.Core.Apply(f.Profile(false, 1280, 720));
                byte[] state = f.Bytes("RA2.INI");
                File.AppendAllText(SnapshotFile(baseline, "RA2.INI"), "tampered");
                Throws(delegate { f.Core.ValidateSnapshot(baseline); }, "Integrity validation rejects tampering");
                Throws(delegate { f.Core.RestoreOriginal(); }, "Restore rejects corruption");
                EqualBytes(state, f.Bytes("RA2.INI"), "Corrupt restore writes nothing");
            }
        });
        Test("Missing manifest is rejected", delegate {
            using (Fixture f = new Fixture()) {
                string baseline = f.Core.EnsureBaseline();
                File.Move(Path.Combine(baseline, "manifest.json"), Path.Combine(baseline, "manifest.removed-for-test"));
                Throws(delegate { f.Core.ValidateSnapshot(baseline); }, "Missing manifest rejected");
                Throws(delegate { f.Core.RestoreOriginal(); }, "Missing manifest prevents restore");
                f.AssertOriginal();
            }
        });
        Test("Manifest traversal rejected", delegate {
            using (Fixture f = new Fixture()) {
                string baseline = f.Core.EnsureBaseline();
                string manifest = Path.Combine(baseline, "manifest.json");
                string json = File.ReadAllText(manifest);
                Assert(json.Contains("RA2.INI"), "Manifest contains RA2 path");
                File.WriteAllText(manifest, json.Replace("RA2.INI", "../outside.ini"));
                Throws(delegate { f.Core.ValidateSnapshot(baseline); }, "Traversal rejected");
                f.AssertOriginal();
            }
        });
        Test("A locked target fails without partial changes", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                GameSettings requested = f.Profile(false, 1280, 720);
                using (FileStream locked = new FileStream(Path.Combine(f.Root, "DDrawCompat.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                    Throws(delegate { f.Core.Apply(requested); }, "Exclusive lock rejects apply");
                }
                f.AssertOriginal();
            }
        });
        Test("Original restore handles originally absent optional config", delegate {
            using (Fixture f = new Fixture()) {
                File.Delete(Path.Combine(f.Root, "RA2MD.INI"));
                f.Core.EnsureBaseline();
                f.Core.Apply(f.Profile(true, 1280, 720));
                Assert(File.Exists(Path.Combine(f.Root, "RA2MD.INI")), "Missing game config created");
                f.Core.RestoreOriginal();
                Assert(!File.Exists(Path.Combine(f.Root, "RA2MD.INI")), "Originally absent file removed on restore");
            }
        });
        Test("Duplicate renderer keys and adjacent comments remain effective", delegate {
            using (Fixture f = new Fixture()) {
                File.AppendAllText(Path.Combine(f.Root, "DDrawCompat.ini"), "FullscreenMode=exclusive# keep mode note\r\nSupportedResolutions=native# keep mode list note\r\nDisplayFilter=point# keep filter note\r\n");
                GameSettings s = f.Core.ReadSettings(false);
                Assert(s.DisplayMode == "exclusive" && s.Filter == "point", "Adjacent wrapper comments parsed");
                s.Width = 1280; s.Height = 720; s.DisplayMode = "borderless";
                f.Core.Apply(s);
                string wrapper = f.Text("DDrawCompat.ini");
                Assert(wrapper.Contains("# keep mode note") && wrapper.Contains("# keep mode list note") && wrapper.Contains("# keep filter note"), "Adjacent comments retained");
                foreach (string line in wrapper.Split('\n').Where(delegate(string x) { return x.TrimStart().StartsWith("SupportedResolutions", StringComparison.OrdinalIgnoreCase); })) {
                    string value = line.Split('#')[0].Split(';')[0];
                    Assert(value.Contains("1280x720") && value.Contains("1920x1080"), "Every duplicate mode declaration has effective added modes");
                }
                Assert(f.Core.ReadSettings(false).DisplayMode == "borderless", "Last duplicate remains effective");
            }
        });
        Test("Second-target write failure rolls back the first target", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                GameSettings s = f.Profile(false, 1280, 720);
                string wrapper = Path.Combine(f.Root, "DDrawCompat.ini");
                File.SetAttributes(wrapper, FileAttributes.ReadOnly);
                try { Throws(delegate { f.Core.Apply(s); }, "Read-only second target rejected"); }
                finally { File.SetAttributes(wrapper, FileAttributes.Normal); }
                f.AssertOriginal();
            }
        });
        Test("Interrupted baseline creation can be retried without replacing an original", delegate {
            using (Fixture f = new Fixture()) {
                using (FileStream locked = new FileStream(Path.Combine(f.Root, "RA2.INI"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                    Throws(delegate { f.Core.EnsureBaseline(); }, "Locked source blocks baseline publication");
                }
                Assert(!Directory.Exists(f.Core.BaselinePath), "Incomplete baseline is not published as original");
                string baseline = f.Core.EnsureBaseline();
                f.Core.ValidateSnapshot(baseline);
                f.AssertOriginal();
            }
        });
        Test("Audio-only edits preserve custom renderer values verbatim", delegate {
            using (Fixture f = new Fixture()) {
                File.AppendAllText(Path.Combine(f.Root, "DDrawCompat.ini"), "FullscreenMode = exclusive(1)# custom mode note\r\nDisplayFilter = bicubic(40); custom scaling note\r\n");
                GameSettings s = f.Core.ReadSettings(false);
                Assert(s.DisplayMode == "exclusive(1)" && s.Filter == "bicubic(40)", "Read exposes custom renderer values");
                s.PreserveDisplayMode = true; s.PreserveFilter = true; s.SoundVolume = 0.12;
                string[] originalLines = f.Text("DDrawCompat.ini").Split('\n').Where(delegate(string x) { return x.TrimStart().StartsWith("FullscreenMode", StringComparison.OrdinalIgnoreCase) || x.TrimStart().StartsWith("DisplayFilter", StringComparison.OrdinalIgnoreCase); }).ToArray();
                f.Core.Apply(s);
                string[] afterLines = f.Text("DDrawCompat.ini").Split('\n').Where(delegate(string x) { return x.TrimStart().StartsWith("FullscreenMode", StringComparison.OrdinalIgnoreCase) || x.TrimStart().StartsWith("DisplayFilter", StringComparison.OrdinalIgnoreCase); }).ToArray();
                Assert(originalLines.SequenceEqual(afterLines), "Custom mode/filter declarations retained byte-for-byte");
                Assert(Math.Abs(f.Core.ReadSettings(false).SoundVolume - 0.12) < 0.00001, "Requested audio setting saved");
                EqualBytes(f.Original["RA2MD.INI"], f.Bytes("RA2MD.INI"), "Other game remains untouched");
            }
        });
        Test("Custom-value preservation flags are independent and explicit", delegate {
            using (Fixture f = new Fixture()) {
                File.AppendAllText(Path.Combine(f.Root, "DDrawCompat.ini"), "FullscreenMode=exclusive(1)\r\nDisplayFilter=bicubic(40)\r\n");
                GameSettings s = f.Core.ReadSettings(false);
                Throws(delegate { f.Core.Apply(s); }, "Unsupported values are not accepted without preserve flags");
                s.PreserveDisplayMode = true; s.Filter = "point";
                f.Core.Apply(s);
                GameSettings actual = f.Core.ReadSettings(false);
                Assert(actual.DisplayMode == "exclusive(1)" && actual.Filter == "point", "Filter can change while custom mode stays intact");
                File.AppendAllText(Path.Combine(f.Root, "DDrawCompat.ini"), "DisplayFilter=bicubic(40)\r\n");
                s = f.Core.ReadSettings(false); s.DisplayMode = "borderless"; s.PreserveFilter = true;
                f.Core.Apply(s);
                actual = f.Core.ReadSettings(false);
                Assert(actual.DisplayMode == "borderless" && actual.Filter == "bicubic(40)", "Mode can change while custom filter stays intact");
            }
        });
        Test("Music repeat and shuffle round-trip without touching other audio settings", delegate {
            using (Fixture f = new Fixture()) {
                GameSettings s = f.Profile(false, 640, 480);
                Assert(!s.ScoreRepeat && !s.ScoreShuffle, "Missing music keys default to off");
                s.ScoreRepeat = true; s.ScoreShuffle = true;
                f.Core.Apply(s);
                GameSettings actual = f.Core.ReadSettings(false);
                Assert(actual.ScoreRepeat && actual.ScoreShuffle, "Both music options saved");
                Assert(Math.Abs(actual.SoundVolume - 0.7) < 0.0001 && actual.InGameMusic, "Other audio settings kept");
                Assert(f.Text("RA2.INI").Contains("IsScoreShuffle=yes") && f.Text("RA2.INI").Contains("UnknownOption=keep me"), "Written as the game's own keys; unknown entries kept");
                Assert(!f.Core.ReadSettings(true).ScoreShuffle, "Yuri's Revenge music settings stay separate");
            }
        });
        Test("Skirmish preset writes only [Skirmish], snapshots first and can be undone", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                Dictionary<string, string> preset = new Dictionary<string, string> { { "Credits", "20000" }, { "Slot01", "5,8,1" }, { "Slot02", "4,-2,-2" }, { "SuperWeaponsAllowed", "no" } };
                string before = f.Core.ApplySkirmish(false, preset);
                f.Core.ValidateSnapshot(before);
                EqualBytes(f.Original["RA2.INI"], File.ReadAllBytes(SnapshotFile(before, "RA2.INI")), "Snapshot taken before the preset");
                Dictionary<string, string> read = f.Core.ReadSkirmish(false);
                Assert(read["Credits"] == "20000" && read["Slot01"] == "5,8,1" && read.Count == 4, "Preset values read back");
                string text = f.Text("RA2.INI");
                Assert(text.Contains("GameSpeed=3") && text.Contains("UnknownOption=keep me") && text.Contains("ScreenWidth = 640"), "Other sections untouched");
                EqualBytes(f.Original["RA2MD.INI"], f.Bytes("RA2MD.INI"), "Yuri's Revenge settings untouched");
                Assert(LauncherCore.DescribeSkirmish(read, false).StartsWith("2 opponents: Medium (Russia), Brutal (random country)"), "Readable summary");
                f.Core.RestorePrevious();
                f.AssertOriginal();
            }
        });
        Test("Skirmish presets reject other settings and unsafe values without writing", delegate {
            using (Fixture f = new Fixture()) {
                f.Core.EnsureBaseline();
                Throws(delegate { f.Core.ApplySkirmish(false, new Dictionary<string, string> { { "ScreenWidth", "1920" } }); }, "Non-skirmish key refused");
                Throws(delegate { f.Core.ApplySkirmish(false, new Dictionary<string, string> { { "Credits", "1\r\n[Video]\r\nScreenWidth=9" } }); }, "Line-break injection refused");
                Throws(delegate { f.Core.ApplySkirmish(false, new Dictionary<string, string>()); }, "Empty preset refused");
                f.AssertOriginal();
            }
        });
        Test("Preset list round-trips per game and damaged files are reported", delegate {
            using (Fixture f = new Fixture()) {
                Assert(f.Core.LoadPresets().Count == 0, "No presets initially");
                List<SkirmishPreset> list = new List<SkirmishPreset> {
                    SkirmishPreset.From(" 1v1 Brutal ", false, new Dictionary<string, string> { { "Slot01", "4,8,1" }, { "Credits", "10000" } }),
                    SkirmishPreset.From("Yuri practice", true, new Dictionary<string, string> { { "Slot01", "6,9,2" } }) };
                f.Core.SavePresets(list);
                List<SkirmishPreset> loaded = f.Core.LoadPresets();
                Assert(loaded.Count == 2 && loaded[0].Name == "1v1 Brutal" && !loaded[0].Yuri && loaded[1].Yuri, "Names trimmed and game kept");
                Assert(loaded[0].ToDictionary()["Slot01"] == "4,8,1", "Values kept");
                Throws(delegate { f.Core.SavePresets(new List<SkirmishPreset> { SkirmishPreset.From("x", false, new Dictionary<string, string> { { "Video", "1" } }) }); }, "Invalid preset not saved");
                Assert(f.Core.LoadPresets().Count == 2, "Failed save left the list intact");
                File.WriteAllText(f.Core.PresetsPath, "{not json");
                bool reported = false;
                try { f.Core.LoadPresets(); } catch (InvalidDataException) { reported = true; }
                Assert(reported, "Damaged presets file reported");
                f.AssertOriginal();
            }
        });
        Test("Save catalog reads a save written by Windows structured storage", delegate {
            using (Fixture f = new Fixture()) {
                string path = Path.Combine(f.Root, "SAVE1A2B.SAV");
                StructuredStorage.CreateTestSave(path, "Bridge assault", "SUN.EXE");
                List<SaveInfo> saves = SaveCatalog.List(f.Root);
                SaveInfo save = saves.Single(delegate(SaveInfo s) { return s.FileName == "SAVE1A2B.SAV"; });
                Assert(save.Error == null && save.Title == "Bridge assault" && save.IsRa2 && !save.IsYuri && save.Kind == "Saved game", "Title, game and kind read");
                SaveInfo junk = saves.Single(delegate(SaveInfo s) { return s.FileName == "SAVE0.SAV"; });
                Assert(junk.Error != null, "A non-save file is reported, not misread");
                EqualBytes(new byte[] { 11, 22, 33 }, f.Bytes("SAVE0.SAV"), "Unreadable file left untouched");
                Assert(SaveCatalog.List(f.Root).Count(delegate(SaveInfo s) { return s.FileName == "campaign.sav"; }) == 1, "SavesDir is included");
            }
        });
        Test("Renaming a save keeps a verified backup and changes only its name", delegate {
            using (Fixture f = new Fixture()) {
                string path = Path.Combine(f.Root, "SAVEFFFF.SAV");
                StructuredStorage.CreateTestSave(path, "Quick Save - Skirmish Game", "GAMEMD.EXE + Ares/3.0p1");
                byte[] original = File.ReadAllBytes(path);
                string backups = Path.Combine(f.Root, "save-backups");
                string backup = SaveCatalog.Rename(path, "  Before the final push  ", backups);
                EqualBytes(original, File.ReadAllBytes(backup), "Backup holds the original save");
                SaveInfo save = SaveCatalog.Read(path);
                Assert(save.Title == "Before the final push" && save.IsYuri && save.Kind == "Quick save", "New name read back; game unchanged");
                byte[] stream = new SaveCatalog.CompoundFile(File.ReadAllBytes(path)).Stream("Scenario Description");
                Assert(Encoding.Unicode.GetString(stream).TrimEnd('\0') == "Before the final push", "Description stream updated too");
            }
        });
        Test("Invalid save names are refused without touching the file", delegate {
            using (Fixture f = new Fixture()) {
                string path = Path.Combine(f.Root, "SAVE0001.SAV");
                StructuredStorage.CreateTestSave(path, "Original", "SUN.EXE");
                byte[] before = File.ReadAllBytes(path);
                foreach (string bad in new[] { "", "   ", new string('x', 41), "two\r\nlines" })
                    Throws(delegate { SaveCatalog.Rename(path, bad, Path.Combine(f.Root, "b")); }, "Refused: " + bad.Length);
                Throws(delegate { SaveCatalog.Rename(Path.Combine(f.Root, "SAVE0.SAV"), "Name", Path.Combine(f.Root, "b")); }, "Unreadable save not renamed");
                EqualBytes(before, File.ReadAllBytes(path), "Save unchanged");
                EqualBytes(new byte[] { 11, 22, 33 }, f.Bytes("SAVE0.SAV"), "Unreadable file unchanged");
            }
        });
        Test("Deleting moves saves aside without overwriting earlier deletions", delegate {
            using (Fixture f = new Fixture()) {
                string deleted = Path.Combine(f.Root, "deleted-saves");
                string a = Path.Combine(f.Root, "SAVEFFF0.SAV");
                StructuredStorage.CreateTestSave(a, "Autosave 10:00 - Map", "SUN.EXE");
                string first = SaveCatalog.MoveToDeleted(a, deleted);
                StructuredStorage.CreateTestSave(a, "Autosave 20:00 - Map", "SUN.EXE");
                string second = SaveCatalog.MoveToDeleted(a, deleted);
                Assert(!File.Exists(a) && File.Exists(first) && File.Exists(second) && first != second, "Both copies kept");
                Assert(SaveCatalog.Read(first).Title == "Autosave 10:00 - Map" && SaveCatalog.Read(second).Title == "Autosave 20:00 - Map", "Contents preserved");
            }
        });
        Test("Blowfish tables and MIX ids match the reference values", delegate {
            uint[] pi = Blowfish.PiWords();
            Assert(pi[0] == 0x243f6a88 && pi[17] == 0x8979fb1b && pi[18] == 0xd1310ba6 && pi[pi.Length - 1] == 0x3ac372e6, "Pi words");
            uint l = 0, r = 0;
            new Blowfish(new byte[8]).Encrypt(ref l, ref r);
            Assert(l == 0x4EF99745 && r == 0x6198DD78, "Zero-key test vector");
            Assert(MixArchive.Id("local.mix") == 0xa8548fd9 && MixArchive.Id("ai.ini") == 0x9e11e49a && MixArchive.Id("rules.ini") == 0xf025a96c, "File ids");
            Assert(MixArchive.Id("a") == 0xeb978531 && MixArchive.Id("ab") == 0x421faa6e && MixArchive.Id("abc") == 0x33aff496, "Padding");
        });
        Test("AI additions insert text only into the exact expected stock file", delegate {
            byte[] stock = Encoding.ASCII.GetBytes("[TaskForces]\r\n1=A\r\n[TeamTypes]\r\n1=B\r\n");
            byte[] expected = Encoding.ASCII.GetBytes("[TaskForces]\r\n1=A\r\n2=X\r\n[TeamTypes]\r\n1=B\r\n2=Y\r\n");
            var additions = new AiAdditions {
                BaseSha256 = EditionSetup.Sha256(stock), OutputSha256 = EditionSetup.Sha256(expected),
                Insertions = new[] { new AiInsertion { Offset = stock.Length, Text = "2=Y\r\n" }, new AiInsertion { Offset = 19, Text = "2=X\r\n" } } };
            EqualBytes(expected, EditionSetup.ApplyAdditions(stock, additions), "Built");
            byte[] other = (byte[])stock.Clone(); other[3] = (byte)'x';
            Throws(delegate { EditionSetup.ApplyAdditions(other, additions); }, "Different stock refused");
            additions.OutputSha256 = EditionSetup.Sha256(stock);
            Throws(delegate { EditionSetup.ApplyAdditions(stock, additions); }, "Unexpected result refused");
        });
        Test("Setup refuses a game that is not the Steam release, and leaves nothing behind", delegate {
            using (Fixture f = new Fixture()) {
                string target = Path.Combine(f.Root, "SmartRA2");
                Throws(delegate { EditionSetup.CreateSmartRa2(f.Root, Path.Combine(f.Root, "package"), target, null); }, "Wrong game.exe refused");
                Assert(!Directory.Exists(target) && !Directory.Exists(target + ".installing"), "No folders created");
            }
        });
        string realGame = Environment.GetEnvironmentVariable("RA2_GAME_ROOT");
        if (!String.IsNullOrEmpty(realGame) && Directory.Exists(realGame))
            Test("Real game: the smarter AI rebuilt from ra2.mix equals the installed ai.ini", delegate {
                string package = Path.Combine(realGame, "RA2CommandCenter", "package", "EnhancedRA2");
                byte[] built = EditionSetup.BuildSmartAi(realGame, Path.Combine(package, "ai-additions.json"));
                string installed = Path.Combine(realGame, "RA2CommandCenter", "SmartRA2", "ai.ini");
                if (!File.Exists(installed)) installed += ".off";
                EqualBytes(File.ReadAllBytes(installed), built, "Byte-identical ai.ini");
            });
        if (!String.IsNullOrEmpty(realGame) && Directory.Exists(realGame) && Environment.GetEnvironmentVariable("RA2_SETUP_TARGET") != null)
            Test("Real game: setup creates a working Enhanced Red Alert 2 copy without touching the game folder", delegate {
                string target = Environment.GetEnvironmentVariable("RA2_SETUP_TARGET");
                string package = Path.Combine(realGame, "RA2CommandCenter", "package", "EnhancedRA2");
                Func<string> snapshot = delegate {
                    return String.Join("\n", Directory.GetFiles(realGame).OrderBy(delegate(string p) { return p; })
                        .Select(delegate(string p) { var i = new FileInfo(p); return p + "|" + i.Length + "|" + i.LastWriteTimeUtc.Ticks; }));
                };
                string before = snapshot();
                var messages = new List<string>();
                EditionSetup.CreateSmartRa2(realGame, package, target, delegate(string m) { messages.Add(m); });
                Assert(snapshot() == before, "Game folder unchanged");
                LauncherApp.VerifySmartAiPackage(target);
                Assert(!Directory.Exists(target + ".installing") && !File.Exists(Path.Combine(target, "SETUP-IN-PROGRESS.txt")), "Work folder renamed");
                foreach (string name in EditionSetup.PackageFiles)
                    EqualBytes(File.ReadAllBytes(Path.Combine(package, name)), File.ReadAllBytes(Path.Combine(target, name)), "Package file " + name);
                foreach (string name in new[] { "ra2.mix", "game.exe", "Ra2.exe", "RA2.INI", "ddraw.dll", "Blowfish.dll" })
                    Assert(new FileInfo(Path.Combine(target, name)).Length == new FileInfo(Path.Combine(realGame, name)).Length, "Copied " + name);
                Assert(Directory.Exists(Path.Combine(target, "SavesDir")) && Directory.GetFiles(target, "*.sav").Length == 0, "Empty saves");
                Assert(Directory.GetFiles(Path.Combine(target, "Taunts")).Length == Directory.GetFiles(Path.Combine(realGame, "Taunts")).Length, "Taunts copied");
                Throws(delegate { EditionSetup.CreateSmartRa2(realGame, package, target, null); }, "Existing edition not overwritten");
                LauncherApp.SetSmartAi(target, false);
                LauncherApp.SetSmartAi(target, true);
                Console.WriteLine("  " + String.Join(" / ", messages.Take(3)) + " … " + messages.Last());
                Console.WriteLine("  " + File.ReadAllLines(Path.Combine(target, "READY.json")).First(delegate(string x) { return x.Contains("gameFiles"); }).Trim());
            });
        if (!String.IsNullOrEmpty(realGame) && Directory.Exists(realGame))
            Test("Real game saves are read (read-only)", delegate {
                foreach (string folder in new[] { realGame, Path.Combine(realGame, "RA2CommandCenter", "SmartRA2"), Path.Combine(realGame, "RA2CommandCenter", "EnhancedYR") })
                {
                    Dictionary<string, byte[]> before = Directory.GetFiles(folder, "*.sav").ToDictionary(delegate(string p) { return p; }, File.ReadAllBytes);
                    foreach (SaveInfo save in SaveCatalog.List(folder))
                    {
                        Assert(save.Error == null && !String.IsNullOrEmpty(save.Title) && (save.IsRa2 || save.IsYuri), "Readable: " + save.Path);
                        Console.WriteLine("  " + save.FileName + " | " + save.Title + " | " + SaveCatalog.Describe(save) + " | " + save.Executable);
                    }
                    foreach (KeyValuePair<string, byte[]> pair in before) EqualBytes(pair.Value, File.ReadAllBytes(pair.Key), "Unchanged " + pair.Key);
                }
            });
        if (!String.IsNullOrEmpty(realGame) && Directory.Exists(realGame))
            Test("Renaming copies of real saves changes only the name; all game data is byte-identical", delegate {
                string work = Path.Combine(runRoot, "real-copies");
                Directory.CreateDirectory(work);
                foreach (string folder in new[] { realGame, Path.Combine(realGame, "RA2CommandCenter", "SmartRA2"), Path.Combine(realGame, "RA2CommandCenter", "EnhancedYR") })
                    foreach (string source in Directory.GetFiles(folder, "*.sav"))
                    {
                        string copy = Path.Combine(work, Path.GetFileName(folder) + "-" + Path.GetFileName(source));
                        File.Copy(source, copy);
                        SaveCatalog.CompoundFile was = new SaveCatalog.CompoundFile(File.ReadAllBytes(copy));
                        SaveInfo info = SaveCatalog.Read(copy);
                        SaveCatalog.Rename(copy, "Renamed copy", Path.Combine(work, "b"));
                        SaveCatalog.CompoundFile now = new SaveCatalog.CompoundFile(File.ReadAllBytes(copy));
                        SaveInfo after = SaveCatalog.Read(copy);
                        Assert(after.Title == "Renamed copy" && after.House == info.House && after.Executable == info.Executable, "Name changed, metadata kept: " + source);
                        Assert(was.StreamNames().OrderBy(delegate(string n) { return n; }).SequenceEqual(now.StreamNames().OrderBy(delegate(string n) { return n; })), "Same streams: " + source);
                        foreach (string name in was.StreamNames())
                            if (name != "\u0005SummaryInformation" && name != "Scenario Description")
                                EqualBytes(was.Stream(name), now.Stream(name), "Stream " + name + " unchanged in " + source);
                    }
            });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed");
        Console.WriteLine("Test fixtures retained at " + runRoot);
        return failed == 0 ? 0 : 1;
    }
}





