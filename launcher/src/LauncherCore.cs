using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ra2CommandCenter
{
    public sealed class GameSettings
    {
        public bool Yuri { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ScrollRate { get; set; }
        public bool StretchMovies { get; set; }
        public bool PlayIntro { get; set; }
        public bool AutoScroll { get; set; }
        public bool InGameMusic { get; set; }
        public bool ScoreRepeat { get; set; }
        public bool ScoreShuffle { get; set; }
        public double SoundVolume { get; set; }
        public double VoiceVolume { get; set; }
        public double ScoreVolume { get; set; }
        public string DisplayMode { get; set; }
        public string Filter { get; set; }
        public bool PreserveDisplayMode { get; set; }
        public bool PreserveFilter { get; set; }
    }

    [DataContract]
    public sealed class SkirmishSetting
    {
        [DataMember] public string Key { get; set; }
        [DataMember] public string Value { get; set; }
    }

    [DataContract]
    public sealed class SkirmishPreset
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public bool Yuri { get; set; }
        [DataMember] public List<SkirmishSetting> Values { get; set; }
        public Dictionary<string, string> ToDictionary()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Values != null) foreach (SkirmishSetting item in Values) if (item != null && item.Key != null) result[item.Key] = item.Value;
            return result;
        }
        public static SkirmishPreset From(string name, bool yuri, IDictionary<string, string> values)
        {
            return new SkirmishPreset { Name = name.Trim(), Yuri = yuri, Values = values.Select(delegate(KeyValuePair<string, string> p) { return new SkirmishSetting { Key = p.Key, Value = p.Value }; }).ToList() };
        }
    }

    [DataContract]
    public sealed class BackupFile
    {
        [DataMember] public string RelativePath { get; set; }
        [DataMember] public bool Exists { get; set; }
        [DataMember] public long Length { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }

    [DataContract]
    public sealed class BackupManifest
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public string CreatedUtc { get; set; }
        [DataMember] public string Purpose { get; set; }
        [DataMember] public List<BackupFile> Files { get; set; }
    }

    // All writes are limited to three configuration files; game binaries and saves are never restored here.
    public sealed class LauncherCore
    {
        private static readonly string[] ConfigNames = { "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" };
        private readonly string gameRoot;
        private readonly string appRoot;
        private readonly bool suppressProcessCheck;
        public string BackupsPath { get; private set; }
        public string BaselinePath { get { return Path.Combine(BackupsPath, "original"); } }

        public LauncherCore(string gameRoot, string appRoot, bool suppressProcessCheck = false)
        {
            this.gameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.appRoot = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.suppressProcessCheck = suppressProcessCheck;
            if (!Directory.Exists(this.gameRoot)) throw new DirectoryNotFoundException("The game installation folder was not found.");
            RequireChild(this.gameRoot, this.appRoot);
            CheckNoReparse(this.gameRoot, this.appRoot);
            Directory.CreateDirectory(this.appRoot);
            BackupsPath = Path.Combine(this.appRoot, "backups");
            CheckNoReparse(this.gameRoot, BackupsPath);
            Directory.CreateDirectory(BackupsPath);
        }

        public string LastSnapshotPath
        {
            get
            {
                string pointer = Path.Combine(appRoot, "previous-snapshot.txt");
                CheckNoReparse(gameRoot, pointer);
                if (!File.Exists(pointer)) return null;
                string name = File.ReadAllText(pointer).Trim();
                if (!Regex.IsMatch(name, @"^snapshot-[0-9A-Za-z-]+$")) throw new InvalidDataException("The restore-history reference is invalid.");
                string result = SafeChild(BackupsPath, name);
                CheckNoReparse(gameRoot, result);
                return result;
            }
        }

        public bool HasPreviousSnapshot
        {
            get
            {
                try { string value = LastSnapshotPath; return value != null && File.Exists(Path.Combine(value, "manifest.json")); }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        public bool IsGameRunning()
        {
            foreach (string name in new[] { "game", "gamemd", "gamemd-spawn", "ra2", "ra2md" })
            {
                Process[] processes = Process.GetProcessesByName(name);
                bool found = processes.Length > 0;
                foreach (Process process in processes) process.Dispose();
                if (found) return true;
            }
            return false;
        }

        private void RequireStopped()
        {
            if (!suppressProcessCheck && IsGameRunning())
                throw new InvalidOperationException("Close Red Alert 2 and Yuri's Revenge before applying or restoring settings.");
        }

        public GameSettings ReadSettings(bool yuri)
        {
            IniDocument ini = IniDocument.Load(ConfigPath(yuri ? "RA2MD.INI" : "RA2.INI"));
            IniDocument draw = IniDocument.Load(ConfigPath("DDrawCompat.ini"));
            string mode = draw.Get(null, "FullscreenMode", "borderless").Trim();
            string filter = draw.Get(null, "DisplayFilter", "bilinear(0)").Trim();
            if (filter.Equals("point(0)", StringComparison.OrdinalIgnoreCase)) filter = "point";
            return new GameSettings
            {
                Yuri = yuri,
                Width = ReadInt(ini, "Video", "ScreenWidth", 640, 640, 7680),
                Height = ReadInt(ini, "Video", "ScreenHeight", 480, 400, 4320),
                ScrollRate = ReadInt(ini, "Options", "ScrollRate", 4, 0, 6),
                StretchMovies = ReadBool(ini, "Video", "StretchMovies", false),
                PlayIntro = ReadBool(ini, "Intro", "Play", true),
                AutoScroll = ReadBool(ini, "Options", "AutoScroll", true),
                InGameMusic = ReadBool(ini, "Audio", "InGameMusic", true),
                ScoreRepeat = ReadBool(ini, "Audio", "IsScoreRepeat", false),
                ScoreShuffle = ReadBool(ini, "Audio", "IsScoreShuffle", false),
                SoundVolume = ReadVolume(ini, "SoundVolume", 0.7),
                VoiceVolume = ReadVolume(ini, "VoiceVolume", 0.8),
                ScoreVolume = ReadVolume(ini, "ScoreVolume", 0.6),
                DisplayMode = mode,
                Filter = filter
            };
        }

        public static void ValidateSettings(GameSettings value)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Width < 640 || value.Width > 7680 || value.Height < 400 || value.Height > 4320)
                throw new ArgumentException("Choose a resolution between 640 by 400 and 7680 by 4320.");
            if (value.ScrollRate < 0 || value.ScrollRate > 6) throw new ArgumentException("Scroll speed must be between 0 and 6.");
            foreach (double volume in new[] { value.SoundVolume, value.VoiceVolume, value.ScoreVolume })
                if (Double.IsNaN(volume) || Double.IsInfinity(volume) || volume < 0 || volume > 1)
                    throw new ArgumentException("Audio levels must be between 0 and 100 percent.");
            if (!value.PreserveDisplayMode && value.DisplayMode != "borderless" && value.DisplayMode != "exclusive")
                throw new ArgumentException("Choose borderless or fullscreen display mode.");
            if (!value.PreserveFilter && value.Filter != "point" && value.Filter != "point(0)" && value.Filter != "bilinear(0)")
                throw new ArgumentException("Choose crisp pixels or smooth scaling.");
        }

        public string EnsureBaseline()
        {
            using (AcquireLock()) { RequireStopped(); return EnsureBaselineInternal(); }
        }

        private string EnsureBaselineInternal()
        {
            if (Directory.Exists(BaselinePath))
            {
                ValidateSnapshot(BaselinePath);
                return BaselinePath;
            }
            List<string> names = new List<string>(ConfigNames);
            names.Add("ddraw.dll");
            foreach (string file in Directory.GetFiles(gameRoot, "*", SearchOption.TopDirectoryOnly))
                if (Path.GetExtension(file).Equals(".sav", StringComparison.OrdinalIgnoreCase)) names.Add(Path.GetFileName(file));
            string saves = Path.Combine(gameRoot, "SavesDir");
            if (Directory.Exists(saves)) CollectSaveFiles(saves, names);
            CreateSnapshotAt(BaselinePath, "Original installation settings, renderer and saves", names);
            Log("Original backup created and verified.");
            return BaselinePath;
        }

        private void CollectSaveFiles(string folder, List<string> names)
        {
            CheckNoReparse(gameRoot, folder);
            foreach (string file in Directory.GetFiles(folder))
            {
                CheckNoReparse(gameRoot, file);
                names.Add(file.Substring(gameRoot.Length + 1));
            }
            foreach (string child in Directory.GetDirectories(folder)) CollectSaveFiles(child, names);
        }

        public string Apply(GameSettings value)
        {
            ValidateSettings(value);
            using (AcquireLock())
            {
                RequireStopped();
                EnsureBaselineInternal();
                string before = CreateSnapshot("Before applying settings");
                BackupManifest previous = LoadAndValidateSnapshot(before);
                string config = value.Yuri ? "RA2MD.INI" : "RA2.INI";
                IniDocument ini = IniDocument.FromBytes(SnapshotBytes(before, previous, config));
                ini.Set("Video", "ScreenWidth", value.Width.ToString(CultureInfo.InvariantCulture));
                ini.Set("Video", "ScreenHeight", value.Height.ToString(CultureInfo.InvariantCulture));
                ini.Set("Video", "StretchMovies", YesNo(value.StretchMovies));
                ini.Set("Intro", "Play", YesNo(value.PlayIntro));
                ini.Set("Options", "ScrollRate", value.ScrollRate.ToString(CultureInfo.InvariantCulture));
                ini.Set("Options", "AutoScroll", YesNo(value.AutoScroll));
                ini.Set("Audio", "SoundVolume", value.SoundVolume.ToString("F6", CultureInfo.InvariantCulture));
                ini.Set("Audio", "VoiceVolume", value.VoiceVolume.ToString("F6", CultureInfo.InvariantCulture));
                ini.Set("Audio", "ScoreVolume", value.ScoreVolume.ToString("F6", CultureInfo.InvariantCulture));
                ini.Set("Audio", "InGameMusic", YesNo(value.InGameMusic));
                ini.Set("Audio", "IsScoreRepeat", YesNo(value.ScoreRepeat));
                ini.Set("Audio", "IsScoreShuffle", YesNo(value.ScoreShuffle));
                IniDocument draw = IniDocument.FromBytes(SnapshotBytes(before, previous, "DDrawCompat.ini"));
                if (!value.PreserveDisplayMode) draw.Set(null, "FullscreenMode", value.DisplayMode);
                draw.Set(null, "DisplayResolution", "desktop");
                draw.Set(null, "DisplayAspectRatio", "app");
                if (!value.PreserveFilter) draw.Set(null, "DisplayFilter", value.Filter == "point(0)" ? "point" : value.Filter);
                List<string> modes = draw.Get(null, "SupportedResolutions", "native, 640x480, 800x600, 1024x768")
                    .Split(',').Select(delegate(string x) { return x.Trim(); }).Where(delegate(string x) { return x.Length > 0; }).ToList();
                foreach (string mode in new[] { "640x480", "800x600", "1280x720", "1600x900", "1920x1080", value.Width + "x" + value.Height })
                    if (!modes.Contains(mode, StringComparer.OrdinalIgnoreCase)) modes.Add(mode);
                draw.Set(null, "SupportedResolutions", String.Join(", ", modes.ToArray()));
                Dictionary<string, byte[]> changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                changes.Add(config, ini.ToBytes());
                changes.Add("DDrawCompat.ini", draw.ToBytes());
                Commit(changes, before, previous);
                Log("Settings applied for " + (value.Yuri ? "Yuri's Revenge" : "Red Alert 2") + ".");
                return before;
            }
        }

        // The game's own [Skirmish] setup: map position, speed, credits, options and the
        // seven opponent slots ("difficulty,country,color"; difficulty 1 = closed,
        // 4 = Brutal/Hard, 5 = Medium/Normal, 6 = Easy; negative country/color = random).
        public static readonly string[] SkirmishKeys = {
            "GameMode", "ScenIndex", "GameSpeed", "Credits", "UnitCount", "ShortGame", "SuperWeaponsAllowed",
            "BuildOffAlly", "MCVRepacks", "CratesAppear", "Slot01", "Slot02", "Slot03", "Slot04", "Slot05", "Slot06", "Slot07" };

        public Dictionary<string, string> ReadSkirmish(bool yuri)
        {
            IniDocument ini = IniDocument.Load(ConfigPath(yuri ? "RA2MD.INI" : "RA2.INI"));
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in SkirmishKeys)
            {
                string value = ini.Get("Skirmish", key, null);
                if (value != null) result[key] = value.Trim();
            }
            return result;
        }

        public static void ValidateSkirmish(IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0) throw new ArgumentException("The skirmish setup is empty.");
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!SkirmishKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("A skirmish preset may only contain the game's own skirmish settings (found " + pair.Key + ").");
                if (pair.Value == null || !Regex.IsMatch(pair.Value, @"^[-0-9A-Za-z, ]{1,32}$"))
                    throw new ArgumentException("The skirmish setting " + pair.Key + " has an invalid value.");
            }
        }

        public string ApplySkirmish(bool yuri, IDictionary<string, string> values)
        {
            ValidateSkirmish(values);
            using (AcquireLock())
            {
                RequireStopped();
                EnsureBaselineInternal();
                string before = CreateSnapshot("Before applying a skirmish preset");
                BackupManifest previous = LoadAndValidateSnapshot(before);
                string config = yuri ? "RA2MD.INI" : "RA2.INI";
                IniDocument ini = IniDocument.FromBytes(SnapshotBytes(before, previous, config));
                foreach (string key in SkirmishKeys)
                    foreach (KeyValuePair<string, string> pair in values)
                        if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ini.Set("Skirmish", key, pair.Value);
                Dictionary<string, byte[]> changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                changes.Add(config, ini.ToBytes());
                Commit(changes, before, previous);
                Log("Skirmish preset applied for " + (yuri ? "Yuri's Revenge" : "Red Alert 2") + ".");
                return before;
            }
        }

        // Presets are stored beside this edition's backups, never inside the game's own files.
        public string PresetsPath { get { return Path.Combine(appRoot, "skirmish-presets.json"); } }

        public List<SkirmishPreset> LoadPresets()
        {
            string path = PresetsPath;
            CheckNoReparse(gameRoot, path);
            if (!File.Exists(path)) return new List<SkirmishPreset>();
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    List<SkirmishPreset> list = (List<SkirmishPreset>)new DataContractJsonSerializer(typeof(List<SkirmishPreset>)).ReadObject(stream);
                    if (list == null) return new List<SkirmishPreset>();
                    foreach (SkirmishPreset preset in list) ValidatePreset(preset);
                    return list;
                }
            }
            catch (SerializationException ex) { throw new InvalidDataException("The skirmish presets file is damaged. Rename or remove " + path + " to start a new list.", ex); }
            catch (ArgumentException ex) { throw new InvalidDataException("The skirmish presets file contains an invalid preset: " + ex.Message, ex); }
        }

        public void SavePresets(List<SkirmishPreset> presets)
        {
            if (presets == null) throw new ArgumentNullException("presets");
            foreach (SkirmishPreset preset in presets) ValidatePreset(preset);
            string path = PresetsPath;
            CheckNoReparse(gameRoot, path);
            string temp = path + ".tmp";
            using (FileStream stream = File.Create(temp))
                new DataContractJsonSerializer(typeof(List<SkirmishPreset>)).WriteObject(stream, presets);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static void ValidatePreset(SkirmishPreset preset)
        {
            if (preset == null) throw new ArgumentException("Missing preset.");
            string name = (preset.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40) throw new ArgumentException("Preset names must be 1 to 40 characters.");
            if (preset.Values == null) throw new ArgumentException("Preset " + name + " has no settings.");
            ValidateSkirmish(preset.ToDictionary());
        }

        private static readonly string[] Ra2Countries = { "America", "Korea", "France", "Germany", "Great Britain", "Libya", "Iraq", "Cuba", "Russia" };
        private static readonly string[] YuriCountries = { "America", "Korea", "France", "Germany", "Great Britain", "Libya", "Iraq", "Cuba", "Russia", "Yuri" };

        public static string DescribeSkirmish(IDictionary<string, string> values, bool yuri)
        {
            List<string> opponents = new List<string>();
            for (int i = 1; i <= 7; i++)
            {
                string raw;
                if (!values.TryGetValue("Slot" + i.ToString("00", CultureInfo.InvariantCulture), out raw)) continue;
                string[] parts = raw.Split(',');
                int level, country;
                if (parts.Length < 2 || !Int32.TryParse(parts[0].Trim(), out level) || level == 1) continue;
                string difficulty = level == 6 ? "Easy" : level == 5 ? "Medium" : level == 4 ? "Brutal" : "Slot type " + level;
                string[] names = yuri ? YuriCountries : Ra2Countries;
                string where = Int32.TryParse(parts[1].Trim(), out country) && country >= 0 && country < names.Length ? names[country] : "random country";
                opponents.Add(difficulty + " (" + where + ")");
            }
            List<string> parts2 = new List<string>();
            parts2.Add(opponents.Count == 0 ? "No opponents" : opponents.Count + (opponents.Count == 1 ? " opponent: " : " opponents: ") + String.Join(", ", opponents.ToArray()));
            string credits;
            int money;
            if (values.TryGetValue("Credits", out credits) && Int32.TryParse(credits, out money)) parts2.Add("$" + money.ToString("N0", CultureInfo.InvariantCulture));
            string flag;
            if (values.TryGetValue("SuperWeaponsAllowed", out flag)) parts2.Add(IsYes(flag) ? "superweapons on" : "superweapons off");
            if (values.TryGetValue("ShortGame", out flag) && IsYes(flag)) parts2.Add("short game");
            if (values.TryGetValue("CratesAppear", out flag)) parts2.Add(IsYes(flag) ? "crates on" : "crates off");
            string map;
            if (values.TryGetValue("ScenIndex", out map)) parts2.Add("map #" + map);
            return String.Join("  ·  ", parts2.ToArray());
        }
        private static bool IsYes(string value) { value = (value ?? "").Trim().ToLowerInvariant(); return value == "yes" || value == "true" || value == "1" || value == "on"; }

        public string RestoreOriginal()
        {
            using (AcquireLock()) { RequireStopped(); return RestoreSnapshot(BaselinePath, "Restore original settings"); }
        }

        public string RestorePrevious()
        {
            using (AcquireLock())
            {
                RequireStopped();
                string target = LastSnapshotPath;
                if (target == null) throw new InvalidOperationException("There are no earlier launcher changes to undo.");
                return RestoreSnapshot(target, "Undo last settings change");
            }
        }

        private string RestoreSnapshot(string target, string purpose)
        {
            BackupManifest source = LoadAndValidateSnapshot(target);
            Dictionary<string, byte[]> changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in ConfigNames) changes.Add(name, SnapshotBytes(target, source, name));
            string before = CreateSnapshot("Before " + purpose.ToLowerInvariant());
            BackupManifest previous = LoadAndValidateSnapshot(before);
            Commit(changes, before, previous);
            Log(purpose + " completed; current settings were backed up first.");
            return before;
        }

        private void Commit(Dictionary<string, byte[]> changes, string before, BackupManifest previous)
        {
            RequireStopped();
            // Refuse to overwrite changes made by another process while preparing this transaction.
            foreach (string name in ConfigNames)
            {
                BackupFile expected = previous.Files.Single(delegate(BackupFile x) { return x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase); });
                string file = ConfigPath(name);
                if (File.Exists(file) != expected.Exists || (expected.Exists && !Hash(file).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Settings changed outside the launcher. Reload them and try again.");
            }
            List<string> written = new List<string>();
            try
            {
                foreach (KeyValuePair<string, byte[]> change in changes)
                {
                    RequireStopped();
                    WriteOrRemove(ConfigPath(change.Key), change.Value);
                    written.Add(change.Key);
                }
                WriteAtomic(Path.Combine(appRoot, "previous-snapshot.txt"), Encoding.UTF8.GetBytes(Path.GetFileName(before)));
            }
            catch (Exception failure)
            {
                List<Exception> rollbackErrors = new List<Exception>();
                written.Reverse();
                foreach (string name in written)
                {
                    try { WriteOrRemove(ConfigPath(name), SnapshotBytes(before, previous, name)); }
                    catch (Exception rollback) { rollbackErrors.Add(rollback); }
                }
                Log("Settings operation failed; rollback " + (rollbackErrors.Count == 0 ? "completed." : "requires recovery from " + Path.GetFileName(before) + "."));
                if (rollbackErrors.Count != 0)
                    throw new IOException("The change failed and automatic rollback was incomplete. Your verified backup is at " + before + ". Close the game and use Restore original settings, or recover the configurations from that backup.", failure);
                throw new IOException("The change could not be completed. Any settings already written were restored. " + failure.Message, failure);
            }
        }

        private string CreateSnapshot(string purpose)
        {
            string name = "snapshot-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string folder = SafeChild(BackupsPath, name);
            CreateSnapshotAt(folder, purpose, new List<string>(ConfigNames));
            return folder;
        }

        private void CreateSnapshotAt(string folder, string purpose, IList<string> names)
        {
            CheckNoReparse(gameRoot, folder);
            if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("A backup with that name already exists; it will not be overwritten.");
            // Publish only a complete, verified backup. A transient copy failure must not
            // leave an incomplete 'original' folder that prevents the next safe retry.
            // Failed pending folders are retained for diagnosis and never used for restore.
            string pending = SafeChild(BackupsPath, "pending-" + Guid.NewGuid().ToString("N"));
            CheckNoReparse(gameRoot, pending);
            Directory.CreateDirectory(pending);
            BackupManifest manifest = new BackupManifest { Version = 1, CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Purpose = purpose, Files = new List<BackupFile>() };
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ValidateRelativeName(name);
                string source = SafeChild(gameRoot, name);
                CheckNoReparse(gameRoot, source);
                bool exists = File.Exists(source);
                BackupFile entry = new BackupFile { RelativePath = name, Exists = exists, Length = 0, Sha256 = null };
                if (exists)
                {
                    string destination = SafeChild(pending, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, false);
                    entry.Length = new FileInfo(destination).Length;
                    entry.Sha256 = Hash(destination);
                    if (new FileInfo(source).Length != entry.Length || !Hash(source).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A file changed during backup. No game settings were modified.");
                }
                manifest.Files.Add(entry);
            }
            using (MemoryStream buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(BackupManifest)).WriteObject(buffer, manifest);
                WriteAtomic(Path.Combine(pending, "manifest.json"), buffer.ToArray());
            }
            ValidateSnapshot(pending);
            CheckNoReparse(gameRoot, folder);
            Directory.Move(pending, folder);
        }

        public void ValidateSnapshot(string directory) { LoadAndValidateSnapshot(directory); }

        private BackupManifest LoadAndValidateSnapshot(string directory)
        {
            directory = Path.GetFullPath(directory);
            RequireChild(BackupsPath, directory);
            CheckNoReparse(gameRoot, directory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            CheckNoReparse(gameRoot, manifestPath);
            if (!File.Exists(manifestPath)) throw new InvalidDataException("The backup manifest is missing. No settings have been restored.");
            if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("The backup manifest is too large.");
            BackupManifest manifest;
            try
            {
                using (FileStream stream = File.OpenRead(manifestPath))
                    manifest = (BackupManifest)new DataContractJsonSerializer(typeof(BackupManifest)).ReadObject(stream);
            }
            catch (Exception ex) { throw new InvalidDataException("The backup manifest cannot be read.", ex); }
            if (manifest == null || manifest.Version != 1 || manifest.Files == null || manifest.Files.Count > 10000)
                throw new InvalidDataException("This backup format is not supported.");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BackupFile entry in manifest.Files)
            {
                if (entry == null) throw new InvalidDataException("The backup manifest contains an empty entry.");
                ValidateRelativeName(entry.RelativePath);
                if (!seen.Add(entry.RelativePath.Replace('/', '\\'))) throw new InvalidDataException("The backup contains duplicate file entries.");
                string file = SafeChild(directory, entry.RelativePath);
                CheckNoReparse(gameRoot, file);
                if (entry.Exists)
                {
                    if (!File.Exists(file) || new FileInfo(file).Length != entry.Length || String.IsNullOrEmpty(entry.Sha256) || !Hash(file).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup verification failed for " + entry.RelativePath + ". No settings have been restored.");
                }
                else if (File.Exists(file)) throw new InvalidDataException("An unexpected file exists in the backup: " + entry.RelativePath);
            }
            foreach (string required in ConfigNames)
                if (!seen.Contains(required)) throw new InvalidDataException("The backup is missing a configuration entry: " + required);
            return manifest;
        }

        private static byte[] SnapshotBytes(string folder, BackupManifest manifest, string name)
        {
            BackupFile entry = manifest.Files.Single(delegate(BackupFile x) { return x.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase); });
            return entry.Exists ? File.ReadAllBytes(SafeChild(folder, name)) : null;
        }

        private FileStream AcquireLock()
        {
            string file = Path.Combine(appRoot, "operation.lock");
            CheckNoReparse(gameRoot, file);
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("Another launcher operation is active. Wait for it to finish and try again.", ex); }
        }

        private string ConfigPath(string name)
        {
            if (!ConfigNames.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Only game configuration files may be changed.");
            string path = SafeChild(gameRoot, name);
            CheckNoReparse(gameRoot, path);
            return path;
        }

        private void WriteOrRemove(string file, byte[] bytes)
        {
            CheckNoReparse(gameRoot, file);
            if (bytes == null) { if (File.Exists(file)) File.Delete(file); }
            else WriteAtomic(file, bytes);
        }

        private void WriteAtomic(string destination, byte[] bytes)
        {
            CheckNoReparse(gameRoot, destination);
            string temporary = destination + ".cc-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                CheckNoReparse(gameRoot, destination);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void Log(string message)
        {
            try
            {
                string path = Path.Combine(appRoot, "launcher.log");
                CheckNoReparse(gameRoot, path);
                File.AppendAllText(path, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Hash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
        }

        private static void ValidateRelativeName(string name)
        {
            if (String.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0)
                throw new InvalidDataException("An unsafe file path was found in the backup.");
            string normalized = name.Replace('/', '\\');
            string[] segments = normalized.Split('\\');
            if (segments.Any(delegate(string x) { return x == "." || x == ".." || x.Length == 0 || x.TrimEnd(' ', '.') != x; }))
                throw new InvalidDataException("An unsafe file path was found in the backup.");
            bool allowed = ConfigNames.Contains(normalized, StringComparer.OrdinalIgnoreCase) || normalized.Equals("ddraw.dll", StringComparison.OrdinalIgnoreCase)
                || (segments.Length == 1 && Path.GetExtension(normalized).Equals(".sav", StringComparison.OrdinalIgnoreCase))
                || (segments.Length > 1 && segments[0].Equals("SavesDir", StringComparison.OrdinalIgnoreCase));
            if (!allowed) throw new InvalidDataException("The backup contains an unsupported file path.");
        }

        private static string SafeChild(string parent, string relative)
        {
            string result = Path.GetFullPath(Path.Combine(parent, relative));
            RequireChild(parent, result);
            return result;
        }

        private static void RequireChild(string parent, string candidate)
        {
            string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The requested path is outside the launcher backup or game folder.");
        }

        private static void CheckNoReparse(string anchor, string path)
        {
            string current = Path.GetFullPath(path);
            string boundary = Path.GetFullPath(anchor).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!current.Equals(boundary, StringComparison.OrdinalIgnoreCase)) RequireChild(boundary, current);
            while (current != null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked folders or files are not supported for settings backups.");
                if (current.Equals(boundary, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        private static string YesNo(bool value) { return value ? "yes" : "no"; }
        private static int ReadInt(IniDocument ini, string section, string key, int fallback, int min, int max)
        { int value; return Int32.TryParse(ini.Get(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max ? value : fallback; }
        private static bool ReadBool(IniDocument ini, string section, string key, bool fallback)
        {
            string value = ini.Get(section, key, "").ToLowerInvariant();
            if (value == "yes" || value == "true" || value == "1" || value == "on") return true;
            if (value == "no" || value == "false" || value == "0" || value == "off") return false;
            return fallback;
        }
        private static double ReadVolume(IniDocument ini, string key, double fallback)
        { double value; return Double.TryParse(ini.Get("Audio", key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsNaN(value) && !Double.IsInfinity(value) ? Math.Max(0, Math.Min(1, value)) : fallback; }
    }

    // This editor retains every untouched line, its line ending, BOM and encoding.
    internal sealed class IniDocument
    {
        private sealed class Line { public string Text; public string Ending; }
        private readonly List<Line> lines = new List<Line>();
        private Encoding encoding;
        private byte[] preamble;
        private string newline = "\r\n";

        public static IniDocument Load(string path)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The game configuration is unexpectedly large.");
            return FromBytes(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        public static IniDocument FromBytes(byte[] data)
        {
            if (data == null) data = new byte[0];
            if (data.Length > 8 * 1024 * 1024) throw new InvalidDataException("The game configuration is unexpectedly large.");
            IniDocument doc = new IniDocument();
            int offset = 0;
            if (data.Length >= 4 && data[0] == 255 && data[1] == 254 && data[2] == 0 && data[3] == 0) { doc.encoding = new UTF32Encoding(false, false); offset = 4; }
            else if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 254 && data[3] == 255) { doc.encoding = new UTF32Encoding(true, false); offset = 4; }
            else if (data.Length >= 3 && data[0] == 239 && data[1] == 187 && data[2] == 191) { doc.encoding = new UTF8Encoding(false); offset = 3; }
            else if (data.Length >= 2 && data[0] == 255 && data[1] == 254) { doc.encoding = new UnicodeEncoding(false, false); offset = 2; }
            else if (data.Length >= 2 && data[0] == 254 && data[1] == 255) { doc.encoding = new UnicodeEncoding(true, false); offset = 2; }
            else
            {
                try { new UTF8Encoding(false, true).GetString(data); doc.encoding = new UTF8Encoding(false); }
                catch (DecoderFallbackException) { doc.encoding = Encoding.Default; }
            }
            doc.preamble = data.Take(offset).ToArray();
            string text = doc.encoding.GetString(data, offset, data.Length - offset);
            foreach (Match match in Regex.Matches(text, @"([^\r\n]*)(\r\n|\r|\n|$)"))
            {
                if (match.Length == 0) continue;
                doc.lines.Add(new Line { Text = match.Groups[1].Value, Ending = match.Groups[2].Value });
            }
            Line first = doc.lines.FirstOrDefault(delegate(Line x) { return x.Ending.Length > 0; });
            if (first != null) doc.newline = first.Ending;
            return doc;
        }

        public string Get(string section, string key, string fallback)
        {
            string current = null;
            string result = fallback;
            foreach (Line line in lines)
            {
                string found;
                if (TrySection(line.Text, out found)) { current = found; continue; }
                int equal;
                if (String.Equals(current, section, StringComparison.OrdinalIgnoreCase) && TryKey(line.Text, key, out equal))
                {
                    string value = line.Text.Substring(equal + 1).Trim();
                    result = Regex.Replace(value, @"[;#].*$", "").Trim();
                }
            }
            return result;
        }

        public void Set(string section, string key, string value)
        {
            string current = null;
            bool matched = false;
            bool sectionSeen = section == null;
            int insertAt = section == null ? 0 : -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string found;
                if (TrySection(lines[i].Text, out found))
                {
                    if (String.Equals(current, section, StringComparison.OrdinalIgnoreCase)) insertAt = i;
                    current = found;
                    if (String.Equals(current, section, StringComparison.OrdinalIgnoreCase)) { sectionSeen = true; insertAt = i + 1; }
                    continue;
                }
                if (!String.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                insertAt = i + 1;
                int equal;
                if (!TryKey(lines[i].Text, key, out equal)) continue;
                string suffix = lines[i].Text.Substring(equal + 1);
                Match whitespace = Regex.Match(suffix, @"^([ \t]*)(.*?)([ \t]*[;#].*|[ \t]*)$");
                lines[i].Text = lines[i].Text.Substring(0, equal + 1) + whitespace.Groups[1].Value + value + whitespace.Groups[3].Value;
                matched = true;
            }
            if (matched) return;
            if (!sectionSeen)
            {
                AppendLine("[" + section + "]");
                AppendLine(key + "=" + value);
                return;
            }
            if (insertAt < 0) insertAt = lines.Count;
            if (insertAt > 0 && lines[insertAt - 1].Ending.Length == 0) lines[insertAt - 1].Ending = newline;
            lines.Insert(insertAt, new Line { Text = key + "=" + value, Ending = newline });
        }

        private void AppendLine(string text)
        {
            if (lines.Count > 0 && lines[lines.Count - 1].Ending.Length == 0) lines[lines.Count - 1].Ending = newline;
            lines.Add(new Line { Text = text, Ending = newline });
        }

        public byte[] ToBytes()
        {
            StringBuilder text = new StringBuilder();
            foreach (Line line in lines) text.Append(line.Text).Append(line.Ending);
            byte[] content = encoding.GetBytes(text.ToString());
            byte[] output = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, output, preamble.Length, content.Length);
            return output;
        }

        private static bool TrySection(string text, out string section)
        {
            Match match = Regex.Match(text, @"^\s*\[([^\]]+)\]\s*(?:[;#].*)?$");
            section = match.Success ? match.Groups[1].Value.Trim() : null;
            return match.Success;
        }

        private static bool TryKey(string text, string key, out int equal)
        {
            equal = text.IndexOf('=');
            string start = text.TrimStart();
            return equal >= 0 && !start.StartsWith(";") && !start.StartsWith("#") && text.Substring(0, equal).Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
