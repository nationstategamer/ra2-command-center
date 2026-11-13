using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ra2CommandCenter
{
    public static class LauncherApp
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string gameRoot = Directory.GetParent(appRoot).FullName;
            string originalRoot = gameRoot;
            bool enhanced = false;
            bool smartAi = false;
            string smokeOutput = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--ui-smoke" && i + 1 < args.Length) smokeOutput = Path.GetFullPath(args[++i]);
                else if (args[i] == "--enhanced") enhanced = true;
                // --native-ra2 opened the former UI-only edition; it now opens the merged Enhanced Red Alert 2 edition.
                else if (args[i] == "--smart-ai" || args[i] == "--native-ra2") smartAi = true;
                else { MessageBox.Show("Start Command Center from its shortcut or one of the enhanced edition buttons.", "Command Center"); return 2; }
            }
            try
            {
                if (enhanced && smartAi) throw new InvalidOperationException("Choose one enhanced edition at a time.");
                if (enhanced || smartAi)
                {
                    gameRoot = Path.Combine(appRoot, smartAi ? "SmartRA2" : "EnhancedYR");
                    appRoot = Path.Combine(gameRoot, "CommandCenterData");
                }
                if (smokeOutput != null)
                {
                    Directory.CreateDirectory(smokeOutput);
                    string sourceGameRoot = originalRoot;
                    originalRoot = Path.Combine(smokeOutput, "g-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    // Keep fixture paths short enough for .NET Framework's legacy MAX_PATH handling.
                    gameRoot = smartAi ? Path.Combine(originalRoot, "a") : enhanced ? Path.Combine(originalRoot, "e") : originalRoot;
                    appRoot = Path.Combine(gameRoot, "cc");
                    Directory.CreateDirectory(appRoot);
                    string ini = "[Intro]\r\nPlay=yes\r\n[Video]\r\nScreenWidth=640\r\nScreenHeight=480\r\nStretchMovies=no\r\n[Options]\r\nScrollRate=4\r\nAutoScroll=yes\r\n[Audio]\r\nSoundVolume=0.700000\r\nVoiceVolume=0.800000\r\nScoreVolume=0.600000\r\nInGameMusic=yes\r\n";
                    File.WriteAllText(Path.Combine(gameRoot, "RA2.INI"), ini);
                    File.WriteAllText(Path.Combine(gameRoot, "RA2MD.INI"), ini);
                    File.WriteAllText(Path.Combine(gameRoot, "DDrawCompat.ini"), "DisplayFilter = point(0)\r\nSupportedResolutions = 640x400, 640x480, 800x600, native\r\nCpuAffinity = 1\r\n");
                    File.WriteAllText(Path.Combine(gameRoot, "ddraw.dll"), "fake graphics wrapper for isolated tests");
                    if (enhanced || smartAi)
                        foreach (string name in new[] { "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" })
                            File.WriteAllText(Path.Combine(originalRoot, name), "Original installation sentinel: " + name);
                    if (enhanced)
                    {
                        foreach (string name in new[] { "Syringe.exe", "gamemd.exe", "Ares.dll", "Phobos.dll", "ddraw.dll" })
                            File.WriteAllBytes(Path.Combine(gameRoot, name), new byte[] { 0x4d, 0x5a, 0, 0 });
                        foreach (string name in new[] { "rulesmd.ini", "artmd.ini", "uimd.ini", "ra2md.csf", "ares.mix", "Ares.dll.inj" })
                            File.WriteAllText(Path.Combine(gameRoot, name), "Isolated enhanced-package test fixture");
                        File.WriteAllText(Path.Combine(gameRoot, "READY.json"), "{\"isolated\":true,\"profile\":\"Classic Command UI\",\"syringe\":\"0.1.0.2\",\"ares\":\"3.0p1\",\"phobos\":\"0.4.0.2\"}");
                    }
                    if (smartAi)
                    {
                        File.Copy(Path.Combine(sourceGameRoot, "game.exe"), Path.Combine(gameRoot, "game.exe"));
                        foreach (string name in new[] { "Ra2.exe", "RA2NativeUI.dll", "dsound.dll" })
                            File.WriteAllBytes(Path.Combine(gameRoot, name), new byte[] { 0x4d, 0x5a, 0, 0 });
                        File.WriteAllText(Path.Combine(gameRoot, "RA2NativeUI.ini"), "[NativeUI]\r\nExtendedTooltips=yes\r\n");
                        File.WriteAllText(Path.Combine(gameRoot, "READY.json"), "{\"status\":\"ready\",\"engine\":\"original-ra2\",\"version\":\"0.1.0\",\"hostSha256\":\"" + NativeHostSha256 + "\"}");
                    }
                    if (smartAi)
                    {
                        File.WriteAllText(Path.Combine(gameRoot, "ai.ini"), "[AITriggerTypes]\r\n; Isolated Smart Skirmish test fixture\r\n");
                        File.WriteAllText(Path.Combine(gameRoot, "READY.json"), "{\"status\":\"ready\",\"engine\":\"original-ra2\",\"version\":\"0.1.0\",\"hostSha256\":\"" + NativeHostSha256 + "\",\"edition\":\"smart-ai\",\"aiVersion\":\"0.1.0\",\"aiSha256\":\"" + FileSha256(Path.Combine(gameRoot, "ai.ini")) + "\"}");
                    }
                    foreach (string icon in new[] { "RA2.ICO", "RA2MD.ico" })
                        if (File.Exists(Path.Combine(sourceGameRoot, icon))) File.Copy(Path.Combine(sourceGameRoot, icon), Path.Combine(gameRoot, icon));
                    StructuredStorage.CreateTestSave(Path.Combine(gameRoot, "SAVE1111.SAV"), "Bridge assault", "SUN.EXE");
                    StructuredStorage.CreateTestSave(Path.Combine(gameRoot, "SAVEFFFF.SAV"), "Quick Save - Test map", "SUN.EXE");
                    StructuredStorage.CreateTestSave(Path.Combine(gameRoot, "SAVE2222.SAV"), "Yuri island", "GAMEMD.EXE");
                }
                else
                {
                    if (enhanced) VerifyEnhancedPackage(gameRoot);
                    if (smartAi) VerifySmartAiPackage(gameRoot);
                    if (!smartAi &&((!enhanced && !File.Exists(Path.Combine(gameRoot, "game.exe"))) || !File.Exists(Path.Combine(gameRoot, "gamemd.exe"))))
                        throw new InvalidOperationException("Keep the RA2CommandCenter folder inside your Red Alert 2 game folder, alongside game.exe and gamemd.exe.");
                    string wrapper = Path.Combine(gameRoot, "ddraw.dll");
                    if (!File.Exists(wrapper) || (FileVersionInfo.GetVersionInfo(wrapper).ProductName ?? "").IndexOf("DDrawCompat", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("This launcher is configured for the bundled DDrawCompat graphics wrapper. Restore that wrapper before applying these display settings.");
                }
                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\RA2CommandCenter_" + StableName(gameRoot), out created))
                {
                    string activationName = "Local\\RA2CommandCenter_Show_" + StableName(gameRoot);
                    if (!created)
                    {
                        try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(activationName)) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("Command Center is already open. Switch to its existing window.", "Command Center"); }
                        return 0;
                    }
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        MessageBox.Show("The operation could not finish.\n\n" + e.Exception.Message, "Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                        e.Handled = true;
                    };
                    Window window;
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CommandCenter.xaml"))
                        window = (Window)XamlReader.Load(stream);
                    Controller controller = new Controller(window, new LauncherCore(gameRoot, appRoot, smokeOutput != null), gameRoot, smokeOutput, originalRoot, enhanced, smartAi);
                    app.MainWindow = window;
                    controller.Initialize();
                    if (smokeOutput != null)
                    {
                        window.WindowStartupLocation = WindowStartupLocation.Manual;
                        window.Left = -20000; window.Top = -20000;
                        window.ShowInTaskbar = false; window.ShowActivated = false;
                        window.ContentRendered += delegate { window.Dispatcher.BeginInvoke(new Action(controller.RunSmoke), DispatcherPriority.ApplicationIdle); };
                    }
                    using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, activationName))
                    {
                        var activationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                        activationTimer.Tick += delegate
                        {
                            if (!activation.WaitOne(0)) return;
                            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                            window.Show(); window.Activate();
                        };
                        activationTimer.Start();
                        try
                        {
                            int appResult = app.Run(window);
                            return controller.SmokeFailed ? 1 : appResult;
                        }
                        finally { activationTimer.Stop(); }
                    }
                }
            }
            catch (Exception ex)
            {
                if (smokeOutput != null) File.WriteAllText(Path.Combine(smokeOutput, "ui-error.txt"), ex.ToString());
                else MessageBox.Show(ex.Message, "Command Center could not start", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        internal const string NativeHostSha256 = "73288C03B58D370BE268CA6D156B4E33BFDB2066DC980359467D8852FF3B00DF";
        internal static void VerifyNativeRa2Package(string root)
        {
            VerifyNativeRa2Core(root);
        }
        private static NativeRa2PackageInfo VerifyNativeRa2Core(string root)
        {
            if (!Directory.Exists(root)) throw new InvalidOperationException("Enhanced Red Alert 2 is not set up yet. Open Command Center and choose Enhanced Red Alert 2 to set it up.");
            for (DirectoryInfo folder = new DirectoryInfo(root); folder != null; folder = folder.Parent)
                if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Enhanced Red Alert 2 requires its own separate folder. Restore the independent copy before starting it.");
            foreach (string name in new[] { "READY.json", "game.exe", "Ra2.exe", "RA2NativeUI.dll", "dsound.dll", "RA2NativeUI.ini", "ddraw.dll", "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" })
            {
                string file = Path.Combine(root, name);
                if (!File.Exists(file) || new FileInfo(file).Length == 0 || (File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new InvalidOperationException("Enhanced Red Alert 2 is not ready: " + name + " is missing, empty or redirected. Repair the separate EnhancedRA2 copy, or use Standard games.");
            }
            NativeRa2PackageInfo info;
            try
            {
                using (var stream = File.OpenRead(Path.Combine(root, "READY.json")))
                    info = (NativeRa2PackageInfo)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(NativeRa2PackageInfo)).ReadObject(stream);
            }
            catch (System.Runtime.Serialization.SerializationException ex)
            {
                throw new InvalidOperationException("The enhanced Red Alert 2 readiness record is damaged. Repair the separate copy, or use Standard games.", ex);
            }
            if (info == null || info.Status != "ready" || info.Engine != "original-ra2" || info.Version != "0.1.0" || !String.Equals(info.HostSha256, NativeHostSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This package does not match the supported original-engine Red Alert 2 enhancement. Repair the separate copy, or use Standard games.");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(Path.Combine(root, "game.exe")))
                if (!String.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), NativeHostSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The enhanced Red Alert 2 game file does not match this UI extension. Restore its supported game.exe before launching. Standard games remain available.");
            return info;
        }
        internal static string FileSha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        internal static void VerifySmartAiPackage(string root)
        {
            NativeRa2PackageInfo info = VerifyNativeRa2Core(root);
            if (info.Edition != "smart-ai" || info.AiVersion != "0.1.0")
                throw new InvalidOperationException("This copy is not the supported Smart Skirmish edition. Repair SmartRA2, or choose another edition.");
            foreach (string name in new[] { "rules.ini", "art.ini" })
                if (File.Exists(Path.Combine(root, name)) || Directory.Exists(Path.Combine(root, name)))
                    throw new InvalidOperationException("Smart Skirmish keeps the original unit rules and artwork. Remove the added " + name + " from this separate copy before continuing.");
            string on = Path.Combine(root, SmartAiOn), off = Path.Combine(root, SmartAiOff);
            bool hasOn = File.Exists(on) || Directory.Exists(on), hasOff = File.Exists(off) || Directory.Exists(off);
            if (hasOn && hasOff)
                throw new InvalidOperationException("Enhanced Red Alert 2 found both ai.ini and ai.ini.off in SmartRA2. Keep only one of them (ai.ini turns the smarter AI on), then try again.");
            string ai = hasOff ? off : on;
            if (!File.Exists(ai) || new FileInfo(ai).Length == 0 || (File.GetAttributes(ai) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidOperationException("Enhanced Red Alert 2 is not ready: the smarter AI file (ai.ini or ai.ini.off) is missing, empty or redirected. Repair SmartRA2, or choose another edition.");
            if (!String.Equals(info.AiSha256, FileSha256(ai), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The smarter AI file does not match its readiness record. Repair this separate copy before continuing; your saved settings have not been changed.");
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(string section, string key, string value, string path);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetPrivateProfileInt(string section, string key, int defaultValue, string path);
        // [UI] switches in SmartRA2/RA2NativeUI.ini, read by the in-game extension when the game starts.
        // Missing switches count as on (1), matching the extension's own default.
        internal static readonly string[] FeatureSwitches = { "NextIdleHarvester", "QuickSave", "GameClock", "Autosave", "RangeCircles", "MatchStats" };
        internal static readonly int[] AutosaveChoices = { 5, 10, 15, 30 };
        // AutosaveMinutes: 1-120, default 10 (the extension uses 10 for anything outside that range).
        internal static int ReadAutosaveMinutes(string root)
        {
            int minutes = ReadUiSetting(root, "AutosaveMinutes", 10);
            return minutes < 1 || minutes > 120 ? 10 : minutes;
        }
        internal static void WriteAutosaveMinutes(string root, int minutes)
        {
            if (minutes < 1 || minutes > 120) throw new ArgumentOutOfRangeException("minutes");
            WriteUiSetting(root, "AutosaveMinutes", minutes);
        }
        internal static int ReadUiSetting(string root, string key, int defaultValue)
        {
            return GetPrivateProfileInt("UI", key, defaultValue, Path.Combine(root, "RA2NativeUI.ini"));
        }
        internal static void WriteUiSetting(string root, string key, int value)
        {
            string ini = Path.Combine(root, "RA2NativeUI.ini");
            if (!File.Exists(ini)) throw new InvalidOperationException("RA2NativeUI.ini is missing from SmartRA2. Repair the Enhanced Red Alert 2 copy before changing in-game settings.");
            if (!WritePrivateProfileString("UI", key, value.ToString(CultureInfo.InvariantCulture), ini))
                throw new InvalidOperationException("The " + key + " setting could not be saved to RA2NativeUI.ini.");
        }
        internal static bool ReadFeature(string root, string key) { return ReadUiSetting(root, key, 1) != 0; }
        internal static void WriteFeature(string root, string key, bool on)
        {
            if (Array.IndexOf(FeatureSwitches, key) < 0) throw new ArgumentOutOfRangeException("key");
            WriteUiSetting(root, key, on ? 1 : 0);
        }
        // HealthBars: 0 = original (selected only), 1 = damaged (default), 2 = all.
        internal static int ReadHealthBarMode(string root)
        {
            int mode = ReadUiSetting(root, "HealthBars", 1);
            return mode < 0 || mode > 2 ? 1 : mode;
        }
        internal static void WriteHealthBarMode(string root, int mode)
        {
            if (mode < 0 || mode > 2) throw new ArgumentOutOfRangeException("mode");
            WriteUiSetting(root, "HealthBars", mode);
        }
        internal const string SmartAiOn = "ai.ini", SmartAiOff = "ai.ini.off";
        // The original engine prefers a loose ai.ini over the copy inside ra2.mix, so moving it aside restores the stock AI.
        internal static bool IsSmartAiEnabled(string root) { return File.Exists(Path.Combine(root, SmartAiOn)); }
        internal static void SetSmartAi(string root, bool enable)
        {
            VerifySmartAiPackage(root);
            if (enable == IsSmartAiEnabled(root)) return;
            if (enable) File.Move(Path.Combine(root, SmartAiOff), Path.Combine(root, SmartAiOn));
            else File.Move(Path.Combine(root, SmartAiOn), Path.Combine(root, SmartAiOff));
            VerifySmartAiPackage(root);
        }
        internal static ProcessStartInfo CreateGameStartInfo(string root, bool useYuri, bool enhancedYuri, bool nativeRa2, bool smartAi = false)
        {
            if ((enhancedYuri ? 1 : 0) + (nativeRa2 ? 1 : 0) + (smartAi ? 1 : 0) > 1) throw new InvalidOperationException("Choose one enhanced edition at a time.");
            string file = (nativeRa2 || smartAi) ? "Ra2.exe" : enhancedYuri ? "Syringe.exe" : useYuri ? "RA2MD.exe" : "Ra2.exe";
            return new ProcessStartInfo(Path.Combine(root, file))
            {
                Arguments = enhancedYuri ? "gamemd.exe -i=Ares.dll -i=Phobos.dll" : "",
                WorkingDirectory = root,
                UseShellExecute = true
            };
        }
        [System.Runtime.Serialization.DataContract]
        private sealed class NativeRa2PackageInfo
        {
            [System.Runtime.Serialization.DataMember(Name = "status")] public string Status { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "engine")] public string Engine { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "version")] public string Version { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "hostSha256")] public string HostSha256 { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "edition")] public string Edition { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "aiVersion")] public string AiVersion { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "aiSha256")] public string AiSha256 { get; set; }
        }
        internal static void VerifyEnhancedPackage(string root)
        {
            if (!Directory.Exists(root)) throw new InvalidOperationException("Enhanced Yuri UI has not been installed. Finish the package in RA2CommandCenter\\EnhancedYR, or use the original games in the standard Command Center.");
            for (DirectoryInfo folder = new DirectoryInfo(root); folder != null; folder = folder.Parent)
                if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Enhanced Yuri UI requires its own separate folder. Restore the independent copy before starting it.");
            foreach (string name in new[] { "READY.json", "Syringe.exe", "gamemd.exe", "Ares.dll", "Ares.dll.inj", "Phobos.dll", "ddraw.dll", "ares.mix", "rulesmd.ini", "artmd.ini", "uimd.ini", "ra2md.csf", "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" })
            {
                string file = Path.Combine(root, name);
                if (!File.Exists(file) || new FileInfo(file).Length == 0 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Enhanced Yuri UI is not ready: " + name + " is missing, empty or redirected. Finish or repair the enhancement package in RA2CommandCenter\\EnhancedYR, then try again. Your original games remain available in the standard Command Center.");
            }
            EnhancedPackageInfo info;
            try
            {
                using (var stream = File.OpenRead(Path.Combine(root, "READY.json")))
                    info = (EnhancedPackageInfo)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(EnhancedPackageInfo)).ReadObject(stream);
            }
            catch (System.Runtime.Serialization.SerializationException ex)
            {
                throw new InvalidOperationException("The enhanced package readiness record is damaged. Repair the enhancement package before starting it; the original games are still available.", ex);
            }
            if (info == null || !info.Isolated || info.Profile != "Classic Command UI" || info.Syringe != "0.1.0.2" || info.Ares != "3.0p1" || info.Phobos != "0.4.0.2")
                throw new InvalidOperationException("This enhanced package does not match the supported Classic Command UI edition. Repair its pinned package before starting it; the original games are still available.");
            foreach (string name in new[] { "Syringe.exe", "gamemd.exe", "Ares.dll", "Phobos.dll", "ddraw.dll" })
                using (var stream = File.OpenRead(Path.Combine(root, name)))
                    if (stream.ReadByte() != 0x4d || stream.ReadByte() != 0x5a)
                        throw new InvalidOperationException("The enhanced package file " + name + " is damaged. Repair the enhancement package before starting it.");
        }
        [System.Runtime.Serialization.DataContract]
        private sealed class EnhancedPackageInfo
        {
            [System.Runtime.Serialization.DataMember(Name = "isolated")] public bool Isolated { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "profile")] public string Profile { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "syringe")] public string Syringe { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "ares")] public string Ares { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "phobos")] public string Phobos { get; set; }
        }
        private static string StableName(string value)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
        }
    }

    internal sealed class Controller
    {
        private readonly Window window;
        private readonly LauncherCore core;
        private readonly string gameRoot;
        private readonly string originalRoot;
        private readonly bool enhanced;
        private readonly bool smartAi;
        private bool aiSyncing;
        private bool IsIsolated { get { return enhanced || smartAi; } }
        private readonly string smokeOutput;
        private bool loading;
        private bool dirty;
        private bool yuri;
        private bool closingForSmoke;
        private bool busy;
        private bool externalChanges;
        private string loadedFingerprint;
        private string savedReadError;
        private int width, height;
        private DispatcherTimer processTimer;
        private GameSettings loaded;
        internal bool SmokeFailed { get; private set; }
        private T C<T>(string name) where T : FrameworkElement { return (T)window.FindName(name); }
        internal Controller(Window w, LauncherCore c, string root, string smoke, string original = null, bool enhancedMode = false, bool smartMode = false)
        { window = w; core = c; gameRoot = root; smokeOutput = smoke; originalRoot = original ?? root; enhanced = enhancedMode; smartAi = smartMode; yuri = enhanced && !smartAi; }

        internal void Initialize()
        {
            core.EnsureBaseline();
            if (enhanced)
            {
                loading = true;
                C<RadioButton>("Ra2Choice").Visibility = Visibility.Collapsed;
                C<RadioButton>("YuriChoice").IsChecked = true;
                C<RadioButton>("YuriChoice").IsEnabled = false;
                C<TextBlock>("YuriChoiceTitle").Text = "ENHANCED YURI";
                C<TextBlock>("YuriChoiceHint").Text = "Classic look · separate saves";
                C<TextBlock>("GameSelectorHeading").Text = "ENHANCED EDITION";
                C<TextBlock>("WindowSubtitle").Text = " / ENHANCED YURI UI";
                C<TextBlock>("DisplayModeLabel").Text = "Display mode · enhanced copy";
                C<TextBlock>("ScalingModeLabel").Text = "Image scaling · enhanced copy";
                C<TextBlock>("DisplayScopeHint").Text = "These display settings apply only to the enhanced copy. Original menus keep their classic layout.";
                C<TextBlock>("RestoreScopeHint").Text = "Restore resets this copy’s display, audio and gameplay settings. It keeps the UI enhancement package and enhanced saves. Use Standard games for the original experience.";
                C<TextBlock>("UndoScopeHint").Text = "Undo the most recent settings change in the enhanced copy.";
                C<Button>("RestoreOriginalButton").Content = "Restore enhanced starting settings";
                C<Button>("EnhancedModeButton").Content = "Standard games";
                C<TabItem>("EnhancedInfoTab").Visibility = Visibility.Visible;
                C<Border>("EnhancedBanner").Visibility = Visibility.Visible;
                window.Title = "Yuri’s Revenge — Enhanced UI Command Center";
                loading = false;
            }
            if (smartAi)
            {
                loading = true;
                C<RadioButton>("YuriChoice").Visibility = Visibility.Collapsed;
                C<RadioButton>("Ra2Choice").IsChecked = true;
                C<RadioButton>("Ra2Choice").IsEnabled = false;
                C<TextBlock>("Ra2ChoiceTitle").Text = "ENHANCED RA2";
                C<TextBlock>("GameSelectorHeading").Text = "ENHANCED EDITION";
                C<TextBlock>("WindowSubtitle").Text = " / ENHANCED EDITION";
                C<TextBlock>("DisplayModeLabel").Text = "Display mode · enhanced copy";
                C<TextBlock>("ScalingModeLabel").Text = "Image scaling · enhanced copy";
                C<TextBlock>("DisplayScopeHint").Text = "These settings apply only to the separate Enhanced Red Alert 2 copy. Your other editions keep their own settings. The original menus keep their classic layout.";
                C<TextBlock>("RestoreScopeHint").Text = "Restore resets this copy’s display, audio and gameplay settings. It keeps the in-game UI extension, the AI choice and this copy’s saves. Use Standard games to return to the original installation.";
                C<TextBlock>("UndoScopeHint").Text = "Undo the most recent settings change in the Enhanced Red Alert 2 copy.";
                C<Button>("RestoreOriginalButton").Content = "Restore enhanced starting settings";
                C<Button>("EnhancedModeButton").Content = "Standard games";
                C<Button>("NativeModeButton").Content = "Enhanced Yuri UI";
                C<TabItem>("EnhancedInfoTab").Visibility = Visibility.Visible;
                C<Border>("EnhancedBanner").Visibility = Visibility.Visible;
                C<TabItem>("EnhancedInfoTab").Header = "UI & AI";
                System.Windows.Automation.AutomationProperties.SetName(C<TabItem>("EnhancedInfoTab"), "In-game UI and AI");
                C<TextBlock>("EnhancedInfoHeading").Text = "A sharper interface. Optional smarter opponents.";
                C<TextBlock>("EnhancedInfoIntro").Text = "One Red Alert 2 edition on the original engine, with its original units and artwork, the in-game UI improvements, and a switchable smarter skirmish AI.";
                C<StackPanel>("SmartAiInfo").Visibility = Visibility.Visible;
                C<Border>("Ra2FeatureInfo").Visibility = Visibility.Visible;
                C<TextBlock>("EnhancedReturnHint").Text = "This edition has its own settings and saves; keep existing saves in the edition where they were made. Use Standard games or Enhanced Yuri UI to switch at any time. Backup & Restore resets this copy’s settings while keeping the UI extension, the AI choice and saves.";
                window.Title = "Red Alert 2 — Enhanced Edition (Experimental)";
                SyncSmartAi();
                loading = false;
            }
            C<Button>("MinimizeButton").Click += delegate { window.WindowState = WindowState.Minimized; };
            C<Button>("MaximizeButton").Click += delegate { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            C<Button>("CloseButton").Click += delegate { window.Close(); };
            C<Border>("TitleBar").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
                if (e.ClickCount == 2) window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.LeftButton == MouseButtonState.Pressed) { try { window.DragMove(); } catch (InvalidOperationException) { } }
            };
            C<RadioButton>("Ra2Choice").Checked += delegate { SwitchGame(false); };
            C<RadioButton>("YuriChoice").Checked += delegate { SwitchGame(true); };
            ConnectProfile("ClassicProfile", 640, 480);
            ConnectProfile("ReadableProfile", 1280, 720);
            ConnectProfile("BalancedProfile", 1600, 900);
            ConnectProfile("WideProfile", 1920, 1080);
            C<ComboBox>("DisplayMode").SelectionChanged += delegate { Changed(); };
            C<ComboBox>("ScalingMode").SelectionChanged += delegate { Changed(); };
            foreach (string id in new[] { "SoundSlider", "VoiceSlider", "MusicSlider", "ScrollSlider" })
                C<Slider>(id).ValueChanged += delegate { UpdateSliderText(); Changed(); };
            foreach (string id in new[] { "MusicCheck", "ShuffleCheck", "RepeatCheck", "AutoScrollCheck", "IntroCheck", "StretchCheck" })
            {
                C<CheckBox>(id).Checked += delegate { Changed(); };
                C<CheckBox>(id).Unchecked += delegate { Changed(); };
            }
            // Enhanced Yuri UI is not part of the public download; hide its button unless that copy exists.
            if (smokeOutput == null && !Directory.Exists(Path.Combine(CommandCenterRoot, "EnhancedYR")))
            {
                if (!IsIsolated) C<Button>("EnhancedModeButton").Visibility = Visibility.Collapsed;
                if (smartAi) C<Button>("NativeModeButton").Visibility = Visibility.Collapsed;
            }
            C<Button>("ApplyButton").Click += delegate { Apply(); };
            C<Button>("DiscardButton").Click += delegate { Load(); SetStatus("Changes discarded.", "Your saved settings have been reloaded."); };
            C<Button>("RestoreOriginalButton").Click += delegate { Restore(true); };
            C<Button>("UndoButton").Click += delegate { Restore(false); };
            C<Button>("LaunchButton").Click += delegate { Launch(); };
            C<Button>("EnhancedModeButton").Click += delegate { OpenOtherMode(IsIsolated ? "" : "--enhanced"); };
            C<Button>("NativeModeButton").Click += delegate { OpenOtherMode(smartAi ? "--enhanced" : "--smart-ai"); };
            C<CheckBox>("SmartAiCheck").Checked += delegate { SetSmartAi(true); };
            C<CheckBox>("SmartAiCheck").Unchecked += delegate { SetSmartAi(false); };
            C<ComboBox>("HealthBarMode").SelectionChanged += delegate { SetHealthBars(C<ComboBox>("HealthBarMode").SelectedIndex); };
            C<ComboBox>("AutosaveInterval").SelectionChanged += delegate { SetAutosaveInterval(C<ComboBox>("AutosaveInterval").SelectedIndex); };
            for (int i = 0; i < FeatureChecks.Length; i++)
            {
                int index = i;
                C<CheckBox>(FeatureChecks[i]).Checked += delegate { SetFeature(index, true); };
                C<CheckBox>(FeatureChecks[i]).Unchecked += delegate { SetFeature(index, false); };
            }
            C<Button>("EditorButton").Click += delegate { RunUtility("FinalAlert2\\FinalAlert2YR.exe"); };
            C<Button>("ManualButton").Click += delegate
            {
                RunUtility(yuri ? "Manuals\\Yuri_s Revenge - English - Manual.pdf" : "Manuals\\Red Alert 2 - English 2 - Manual.pdf");
            };
            C<ListBox>("SaveList").SelectionChanged += delegate
            {
                SaveInfo save = SelectedSave();
                C<TextBox>("SaveName").Text = save != null && save.Error == null ? save.Title : "";
                RefreshButtons();
            };
            C<Button>("RenameSaveButton").Click += delegate { RenameSave(); };
            C<Button>("DeleteSaveButton").Click += delegate { DeleteSave(); };
            C<Button>("OpenDeletedSavesButton").Click += delegate { TryAction(delegate { Directory.CreateDirectory(DeletedSavesPath); Process.Start(new ProcessStartInfo(DeletedSavesPath) { UseShellExecute = true }); }); };
            C<TabControl>("SettingsTabs").SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (e.OriginalSource == C<TabControl>("SettingsTabs") && C<TabControl>("SettingsTabs").SelectedItem == C<TabItem>("SavesTab")) RefreshSaves();
            };
            C<Button>("SavePresetButton").Click += delegate { SavePreset(); };
            C<Button>("UsePresetButton").Click += delegate { UsePreset(); };
            C<Button>("DeletePresetButton").Click += delegate { DeletePreset(); };
            C<ListBox>("PresetList").SelectionChanged += delegate { RefreshButtons(); };
            C<ListBox>("PresetList").MouseDoubleClick += delegate { UsePreset(); };
            C<Button>("OpenBackupsButton").Click += delegate { TryAction(delegate { Process.Start(new ProcessStartInfo(core.BackupsPath) { UseShellExecute = true }); }); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!closingForSmoke && !ResolveDraft()) e.Cancel = true;
                else if (processTimer != null) processTimer.Stop();
            };
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { if (dirty) Apply(); e.Handled = true; }
            };
            Load();
            processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            processTimer.Tick += delegate { RefreshSavedSettings(); SyncSmartAi(); RefreshButtons(); };
            processTimer.Start();
            window.Activated += delegate { RefreshSavedSettings(); SyncSmartAi(); };
            SetStatus(smartAi ? "Experimental Enhanced Red Alert 2 is ready." : enhanced ? "Enhanced Yuri UI is ready." : "Ready. Your original files are backed up.", IsIsolated ? "Independent settings and saves. Standard games opens the original experience." : "Choose a profile, then apply your settings.  •  Ctrl+S to apply");
        }
        private void ConnectProfile(string name, int w, int h)
        {
            C<RadioButton>(name).Checked += delegate { if (loading) return; width = w; height = h; UpdateProfileSummary(); Changed(); };
        }
        private void Changed()
        {
            if (loading || loaded == null) return;
            dirty = !EqualSettings(Capture(), loaded);
            if (externalChanges)
            {
                if (!dirty) RefreshSavedSettings();
                else ShowExternalSettingsNotice();
                RefreshButtons();
                return;
            }
            SetStatus(dirty ? "Unsaved settings for " + GameName() + "." : "Your settings are up to date.",
                dirty ? "Apply to save. A backup is created automatically." : "Ready to launch with the saved settings.");
            RefreshButtons();
        }
        private void SyncSmartAi()
        {
            if (!smartAi) return;
            bool on = LauncherApp.IsSmartAiEnabled(gameRoot);
            aiSyncing = true;
            try
            {
                C<CheckBox>("SmartAiCheck").IsChecked = on;
                for (int i = 0; i < FeatureChecks.Length; i++)
                    C<CheckBox>(FeatureChecks[i]).IsChecked = LauncherApp.ReadFeature(gameRoot, LauncherApp.FeatureSwitches[i]);
                C<ComboBox>("AutosaveInterval").SelectedIndex = Array.IndexOf(LauncherApp.AutosaveChoices, LauncherApp.ReadAutosaveMinutes(gameRoot));
                int bars = LauncherApp.ReadHealthBarMode(gameRoot);
                if (C<ComboBox>("HealthBarMode").SelectedIndex != bars) C<ComboBox>("HealthBarMode").SelectedIndex = bars;
            }
            finally { aiSyncing = false; }
            C<TextBlock>("Ra2ChoiceHint").Text = on ? "In-game UI · smarter AI on" : "In-game UI · original AI";
            C<TextBlock>("EnhancedBannerText").Text = "EXPERIMENTAL ENHANCED RED ALERT 2  ·  Original engine, units and artwork with in-game UI improvements.  Smarter skirmish AI: " + (on ? "ON" : "OFF") + ".";
        }
        private void SetSmartAi(bool enable)
        {
            if (aiSyncing) return;
            if (busy || (smokeOutput == null && core.IsGameRunning()))
            {
                SetStatus("Close the game before changing the AI.", "The AI is chosen when a new skirmish starts.");
                SyncSmartAi();
                return;
            }
            try
            {
                LauncherApp.SetSmartAi(gameRoot, enable);
                SetStatus(enable ? "Smarter skirmish AI turned on." : "Original skirmish AI restored.", "Takes effect when you start the next skirmish. The in-game UI improvements stay on either way.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SyncSmartAi(); }
        }
        // Launcher checkboxes, in the same order as LauncherApp.FeatureSwitches.
        private static readonly string[] FeatureChecks = { "IdleHarvesterCheck", "QuickSaveCheck", "GameClockCheck", "AutosaveCheck", "RangeCirclesCheck", "MatchStatsCheck" };
        private static readonly string[] FeatureNames = { "Next Idle Harvester", "Quick Save", "Game clock", "Autosave", "Range circles", "Match stats" };
        private void SetFeature(int index, bool on)
        {
            if (aiSyncing || !smartAi) return;
            string key = LauncherApp.FeatureSwitches[index];
            if (on == LauncherApp.ReadFeature(gameRoot, key)) return;
            if (busy || (smokeOutput == null && core.IsGameRunning()))
            {
                SetStatus("Close the game before changing in-game features.", "These settings are read when the game starts.");
                SyncSmartAi();
                return;
            }
            try
            {
                LauncherApp.WriteFeature(gameRoot, key, on);
                SetStatus(FeatureNames[index] + (on ? " turned on." : " turned off."), "Applies the next time Enhanced Red Alert 2 starts.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SyncSmartAi(); }
        }
        private void SetAutosaveInterval(int index)
        {
            if (aiSyncing || !smartAi || index < 0 || index >= LauncherApp.AutosaveChoices.Length) return;
            int minutes = LauncherApp.AutosaveChoices[index];
            if (minutes == LauncherApp.ReadAutosaveMinutes(gameRoot)) return;
            if (busy || (smokeOutput == null && core.IsGameRunning()))
            {
                SetStatus("Close the game before changing autosave.", "The setting is read when the game starts.");
                SyncSmartAi();
                return;
            }
            try
            {
                LauncherApp.WriteAutosaveMinutes(gameRoot, minutes);
                SetStatus("Autosave every " + minutes + " minutes.", "Applies the next time Enhanced Red Alert 2 starts.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SyncSmartAi(); }
        }
        private void SetHealthBars(int mode)
        {
            if (aiSyncing || !smartAi || mode < 0) return;
            if (mode == LauncherApp.ReadHealthBarMode(gameRoot)) return;
            if (busy || (smokeOutput == null && core.IsGameRunning()))
            {
                SetStatus("Close the game before changing health bars.", "The setting is read when the game starts.");
                SyncSmartAi();
                return;
            }
            try
            {
                LauncherApp.WriteHealthBarMode(gameRoot, mode);
                SetStatus(mode == 0 ? "Health bars: selected units only." : mode == 1 ? "Health bars: damaged units and buildings." : "Health bars: all units and buildings.",
                    "Applies the next time Enhanced Red Alert 2 starts.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SyncSmartAi(); }
        }
        private string GameName() { return smartAi ? "Enhanced Red Alert 2" : enhanced ? "Enhanced Yuri" : yuri ? "Yuri’s Revenge" : "Red Alert 2"; }
        private void SwitchGame(bool useYuri)
        {
            if (loading || IsIsolated || useYuri == yuri) return;
            if (!ResolveDraft())
            {
                loading = true;
                C<RadioButton>(yuri ? "YuriChoice" : "Ra2Choice").IsChecked = true;
                loading = false;
                return;
            }
            yuri = useYuri;
            Load();
            SetStatus(GameName() + " selected.", "Settings are loaded from this game’s saved configuration.");
        }
        private bool ResolveDraft()
        {
            RefreshSavedSettings();
            if (!dirty) return true;
            if (externalChanges)
            {
                return MessageBox.Show(window, "Saved settings changed outside Command Center.\n\nDiscard your pending edits and continue? The newer saved settings will be kept.", "Settings changed", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
            }
            MessageBoxResult answer = MessageBox.Show(window, "Apply your unsaved settings for " + GameName() + "?\n\nYes: apply and continue\nNo: discard and continue\nCancel: stay here", "Unsaved settings", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return false;
            return answer == MessageBoxResult.No || Apply();
        }
        private void Load()
        {
            loading = true;
            try
            {
                // Confirm the settings stayed unchanged while reading, including entries not shown by this UI.
                string before = ReadConfigurationFingerprint();
                GameSettings next = core.ReadSettings(yuri);
                string after = ReadConfigurationFingerprint();
                if (before != after) throw new IOException("The game settings are still changing. Wait a moment, then reload them.");
                loaded = next;
                width = loaded.Width; height = loaded.Height;
                string[] ids = { "ClassicProfile", "ReadableProfile", "BalancedProfile", "WideProfile" };
                int[] ws = { 640, 1280, 1600, 1920 }; int[] hs = { 480, 720, 900, 1080 };
                for (int i = 0; i < ids.Length; i++) C<RadioButton>(ids[i]).IsChecked = width == ws[i] && height == hs[i];
                ComboBox display = C<ComboBox>("DisplayMode"), scaling = C<ComboBox>("ScalingMode");
                while (display.Items.Count > 2) display.Items.RemoveAt(2);
                while (scaling.Items.Count > 2) scaling.Items.RemoveAt(2);
                if (loaded.DisplayMode != "borderless" && loaded.DisplayMode != "exclusive")
                {
                    display.Items.Add(new ComboBoxItem { Content = "Keep existing custom mode" });
                    display.SelectedIndex = 2;
                }
                else display.SelectedIndex = loaded.DisplayMode == "exclusive" ? 1 : 0;
                if (NormalizeFilter(loaded.Filter) != "point" && loaded.Filter != "bilinear(0)")
                {
                    scaling.Items.Add(new ComboBoxItem { Content = "Keep existing custom scaling" });
                    scaling.SelectedIndex = 2;
                }
                else scaling.SelectedIndex = loaded.Filter == "bilinear(0)" ? 1 : 0;
                C<Slider>("SoundSlider").Value = loaded.SoundVolume * 100;
                C<Slider>("VoiceSlider").Value = loaded.VoiceVolume * 100;
                C<Slider>("MusicSlider").Value = loaded.ScoreVolume * 100;
                C<Slider>("ScrollSlider").Value = 6 - loaded.ScrollRate;
                C<CheckBox>("MusicCheck").IsChecked = loaded.InGameMusic;
                C<CheckBox>("ShuffleCheck").IsChecked = loaded.ScoreShuffle;
                C<CheckBox>("RepeatCheck").IsChecked = loaded.ScoreRepeat;
                C<CheckBox>("IntroCheck").IsChecked = loaded.PlayIntro;
                C<CheckBox>("AutoScrollCheck").IsChecked = loaded.AutoScroll;
                C<CheckBox>("StretchCheck").IsChecked = loaded.StretchMovies;
                C<TextBlock>("SavedResolution").Text = width + " × " + height;
                C<TextBlock>("GameHeading").Text = GameName().ToUpperInvariant() + " / CONFIGURATION";
                C<TextBlock>("AudioGameHint").Text = "Audio and gameplay settings for " + GameName() + ".";
                C<TextBlock>("MonitorHint").Text = "Lower resolutions are enlarged to fit your screen. Higher resolutions show more map; units and the sidebar appear smaller.";
                C<TextBlock>("BaselineInfo").Text = IsIsolated ? "This copy’s starting settings and graphics wrapper are backed up separately. The original installation remains untouched." : "Original settings, graphics wrapper and existing saves are backed up.";
                LoadEmblem();
                RefreshSkirmish();
                RefreshSaves();
                UpdateSliderText(); UpdateProfileSummary();
                dirty = false;
                externalChanges = false;
                savedReadError = null;
                loadedFingerprint = after;
            }
            finally { loading = false; }
            RefreshButtons();
        }
        // ---- Saved games ----
        private string DeletedSavesPath { get { return Path.Combine(Path.GetDirectoryName(core.BackupsPath), "deleted-saves"); } }
        private string SaveBackupsPath { get { return Path.Combine(core.BackupsPath, "save-renames"); } }
        private void RefreshSaves()
        {
            ListBox list = C<ListBox>("SaveList");
            string selected = list.SelectedItem is ListBoxItem ? ((SaveInfo)((ListBoxItem)list.SelectedItem).Tag).Path : null;
            List<SaveInfo> saves;
            try { saves = SaveCatalog.List(gameRoot); }
            catch (Exception ex) { saves = new List<SaveInfo>(); SetStatus("Saved games could not be listed.", ex.Message); }
            saves = saves.Where(delegate(SaveInfo s) { return s.Error != null || (yuri ? s.IsYuri : !s.IsYuri); }).ToList();
            list.Items.Clear();
            foreach (SaveInfo save in saves)
            {
                StackPanel row = new StackPanel();
                row.Children.Add(new TextBlock { Text = save.Error == null ? save.Title : save.FileName, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = save.Error ?? SaveCatalog.Describe(save), Style = (Style)window.FindResource("Hint"), Margin = new Thickness(0, 3, 0, 0) });
                ListBoxItem item = new ListBoxItem { Content = row, Tag = save };
                System.Windows.Automation.AutomationProperties.SetName(item, save.Title ?? save.FileName);
                list.Items.Add(item);
                if (save.Path == selected) list.SelectedItem = item;
            }
            long bytes = saves.Sum(delegate(SaveInfo s) { return s.Size; });
            C<TextBlock>("SavesSummary").Text = saves.Count == 0 ? "Saves for " + GameName() + " appear here, newest first."
                : saves.Count + (saves.Count == 1 ? " save" : " saves") + " for " + GameName() + ", " + (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB, newest first. Quick saves and autosaves are labelled.";
            C<TextBlock>("SavesEmpty").Visibility = saves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private SaveInfo SelectedSave()
        {
            ListBoxItem item = C<ListBox>("SaveList").SelectedItem as ListBoxItem;
            return item == null ? null : (SaveInfo)item.Tag;
        }
        private bool SavesLocked()
        {
            if (busy) return true;
            if (smokeOutput == null && core.IsGameRunning()) { SetStatus("Close the game before changing saved games.", "The game may be writing a save."); return true; }
            return false;
        }
        private void RenameSave()
        {
            SaveInfo save = SelectedSave();
            if (save == null || save.Error != null || SavesLocked()) return;
            string name = (C<TextBox>("SaveName").Text ?? "").Trim();
            TryAction(delegate
            {
                SaveCatalog.Rename(save.Path, name, SaveBackupsPath);
                RefreshSaves();
                SetStatus("Save renamed to “" + name + "”.", "The game’s Load menu shows the new name. A copy of the previous version is in Command Center’s backups.");
            });
            RefreshButtons();
        }
        private void DeleteSave()
        {
            SaveInfo save = SelectedSave();
            if (save == null || SavesLocked()) return;
            string label = save.Error == null ? save.Title : save.FileName;
            if (smokeOutput == null && MessageBox.Show(window, "Move “" + label + "” to the Deleted saves folder?\n\nIt will no longer appear in the game’s Load menu. You can move the file back from that folder at any time.", "Delete save", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            TryAction(delegate
            {
                SaveCatalog.MoveToDeleted(save.Path, DeletedSavesPath);
                C<TextBox>("SaveName").Text = "";
                RefreshSaves();
                SetStatus("“" + label + "” moved to Deleted saves.", "Move it back from that folder to restore it.");
            });
            RefreshButtons();
        }

        // ---- Skirmish presets ----
        private List<SkirmishPreset> presets = new List<SkirmishPreset>();
        private bool presetsDamaged;
        private void RefreshSkirmish()
        {
            Dictionary<string, string> current = core.ReadSkirmish(yuri);
            C<TextBlock>("SkirmishCurrent").Text = current.Count == 0
                ? "No skirmish has been set up in " + GameName() + " yet. Choose one on the game’s Skirmish screen, then come back to save it."
                : LauncherCore.DescribeSkirmish(current, yuri);
            try { presets = core.LoadPresets(); presetsDamaged = false; }
            catch (InvalidDataException ex) { presets = new List<SkirmishPreset>(); presetsDamaged = true; SetStatus("Skirmish presets could not be read.", ex.Message); }
            ListBox list = C<ListBox>("PresetList");
            SkirmishPreset selected = list.SelectedItem is ListBoxItem ? (SkirmishPreset)((ListBoxItem)list.SelectedItem).Tag : null;
            list.Items.Clear();
            foreach (SkirmishPreset preset in presets.Where(delegate(SkirmishPreset p) { return p.Yuri == yuri; }))
            {
                StackPanel row = new StackPanel();
                row.Children.Add(new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new TextBlock { Text = LauncherCore.DescribeSkirmish(preset.ToDictionary(), yuri), Style = (Style)window.FindResource("Hint"), Margin = new Thickness(0, 3, 0, 0) });
                ListBoxItem item = new ListBoxItem { Content = row, Tag = preset };
                System.Windows.Automation.AutomationProperties.SetName(item, preset.Name);
                list.Items.Add(item);
                if (selected != null && selected.Name == preset.Name) list.SelectedItem = item;
            }
            C<TextBlock>("PresetEmpty").Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private SkirmishPreset SelectedPreset()
        {
            ListBoxItem item = C<ListBox>("PresetList").SelectedItem as ListBoxItem;
            return item == null ? null : (SkirmishPreset)item.Tag;
        }
        private void SavePreset()
        {
            string name = (C<TextBox>("PresetName").Text ?? "").Trim();
            if (name.Length == 0) { SetStatus("Name the preset first.", "For example “1v1 Brutal”."); return; }
            if (presetsDamaged) { SetStatus("Skirmish presets could not be read.", "Fix or remove the damaged presets file before saving new presets."); return; }
            TryAction(delegate
            {
                Dictionary<string, string> current = core.ReadSkirmish(yuri);
                if (current.Count == 0) { SetStatus("There is no skirmish setup to save yet.", "Set up a skirmish in the game, exit it, then save the setup here."); return; }
                List<SkirmishPreset> next = presets.Where(delegate(SkirmishPreset p) { return !(p.Yuri == yuri && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }).ToList();
                bool replaced = next.Count != presets.Count;
                next.Add(SkirmishPreset.From(name, yuri, current));
                core.SavePresets(next);
                C<TextBox>("PresetName").Text = "";
                RefreshSkirmish();
                SetStatus(replaced ? "Preset “" + name + "” updated." : "Preset “" + name + "” saved.", LauncherCore.DescribeSkirmish(current, yuri));
            });
            RefreshButtons();
        }
        private void UsePreset()
        {
            SkirmishPreset preset = SelectedPreset();
            if (preset == null || busy) return;
            if (smokeOutput == null && core.IsGameRunning()) { SetStatus("Close the game before using a preset.", "The game reads its skirmish setup when it starts."); return; }
            if (dirty || externalChanges) { SetStatus("Apply or discard your setting changes first.", "Then use the preset."); return; }
            busy = true;
            try
            {
                if (smartAi) LauncherApp.VerifySmartAiPackage(gameRoot);
                core.ApplySkirmish(yuri, preset.ToDictionary());
                Load();
                SetStatus("Preset “" + preset.Name + "” is ready.", "Launch the game and open Skirmish to play it. Undo in Backup & Restore reverses this.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; RefreshButtons(); }
        }
        private void DeletePreset()
        {
            SkirmishPreset preset = SelectedPreset();
            if (preset == null || presetsDamaged) return;
            if (smokeOutput == null && MessageBox.Show(window, "Delete the skirmish preset “" + preset.Name + "”?\n\nThis removes only the saved preset; the game’s current setup is unchanged.", "Delete preset", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            TryAction(delegate
            {
                core.SavePresets(presets.Where(delegate(SkirmishPreset p) { return !(p.Yuri == preset.Yuri && p.Name == preset.Name); }).ToList());
                RefreshSkirmish();
                SetStatus("Preset “" + preset.Name + "” deleted.", "The game’s current skirmish setup is unchanged.");
            });
            RefreshButtons();
        }
        private string ReadConfigurationFingerprint()
        {
            var values = new System.Text.StringBuilder();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (string name in new[] { "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" })
                {
                    string path = Path.Combine(gameRoot, name);
                    values.Append(name).Append('=');
                    values.Append(File.Exists(path) ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))) : "missing");
                    values.Append('\n');
                }
            }
            return values.ToString();
        }
        private bool RefreshSavedSettings()
        {
            if (loading || busy || loaded == null) return !externalChanges;
            // The games can rewrite their configuration while exiting; inspect it only after they stop.
            if (smokeOutput == null && core.IsGameRunning()) return false;
            try
            {
                string fingerprint = ReadConfigurationFingerprint();
                if (fingerprint == loadedFingerprint)
                {
                    if (externalChanges)
                    {
                        externalChanges = false;
                        savedReadError = null;
                        SetStatus(dirty ? "Your pending settings are ready to apply." : "Your saved settings are up to date.",
                            dirty ? "A backup will be created before applying." : "Ready to launch with the saved settings.");
                        RefreshButtons();
                    }
                    return true;
                }
                if (dirty)
                {
                    bool notify = !externalChanges || savedReadError != null;
                    externalChanges = true;
                    savedReadError = null;
                    if (notify) ShowExternalSettingsNotice();
                    RefreshButtons();
                    return false;
                }
                Load();
                SetStatus("Saved settings refreshed.", "Changes made by the game or another app are now shown.");
                return true;
            }
            catch (IOException ex) { return SavedSettingsUnavailable(ex.Message); }
            catch (UnauthorizedAccessException ex) { return SavedSettingsUnavailable(ex.Message); }
        }
        private bool SavedSettingsUnavailable(string reason)
        {
            bool notify = !externalChanges || savedReadError != reason;
            externalChanges = true;
            savedReadError = reason;
            if (notify) ShowExternalSettingsNotice();
            RefreshButtons();
            return false;
        }
        private void ShowExternalSettingsNotice()
        {
            if (savedReadError != null)
                SetStatus("Saved settings could not be checked.", "Your edits are still here. Close any app using the settings files, then choose Discard changes to reload.");
            else
                SetStatus("Saved settings changed outside Command Center.", "Your edits are still here. Choose Discard changes to reload before applying.");
        }
        private void LoadEmblem()
        {
            try
            {
                string file = Path.Combine(gameRoot, yuri ? "RA2MD.ico" : "RA2.ICO");
                if (!File.Exists(file)) return;
                BitmapDecoder decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                C<Image>("GameEmblem").Source = frame;
                window.Icon = frame;
            }
            catch { C<Image>("GameEmblem").Source = null; }
        }
        private void UpdateProfileSummary()
        {
            string text;
            if (width == 640 && height == 480) text = "Classic / Original 4:3 image. Side borders preserve the proportions.";
            else if (width == 1280 && height == 720) text = "Readable / Larger units and sidebar on a 1080p screen.";
            else if (width == 1600 && height == 900) text = "Balanced / More battlefield space with comfortable detail.";
            else if (width == 1920 && height == 1080) text = "Wide view / More map at once. Units and interface appear smaller.";
            else text = "Existing custom resolution / " + width + " × " + height + ". Select a profile to change it.";
            C<TextBlock>("ProfileSummary").Text = text;
        }
        private void UpdateSliderText()
        {
            C<TextBlock>("SoundValue").Text = Math.Round(C<Slider>("SoundSlider").Value) + "%";
            C<TextBlock>("VoiceValue").Text = Math.Round(C<Slider>("VoiceSlider").Value) + "%";
            C<TextBlock>("MusicValue").Text = Math.Round(C<Slider>("MusicSlider").Value) + "%";
            C<TextBlock>("ScrollValue").Text = Math.Round(C<Slider>("ScrollSlider").Value) + " / 6";
        }
        private GameSettings Capture()
        {
            return new GameSettings
            {
                Yuri = yuri, Width = width, Height = height,
                DisplayMode = C<ComboBox>("DisplayMode").SelectedIndex == 2 ? loaded.DisplayMode : C<ComboBox>("DisplayMode").SelectedIndex == 1 ? "exclusive" : "borderless",
                Filter = C<ComboBox>("ScalingMode").SelectedIndex == 2 ? loaded.Filter : C<ComboBox>("ScalingMode").SelectedIndex == 1 ? "bilinear(0)" : "point",
                PreserveDisplayMode = C<ComboBox>("DisplayMode").SelectedIndex == 2,
                PreserveFilter = C<ComboBox>("ScalingMode").SelectedIndex == 2,
                SoundVolume = C<Slider>("SoundSlider").Value / 100.0,
                VoiceVolume = C<Slider>("VoiceSlider").Value / 100.0,
                ScoreVolume = C<Slider>("MusicSlider").Value / 100.0,
                ScrollRate = 6 - (int)Math.Round(C<Slider>("ScrollSlider").Value),
                InGameMusic = C<CheckBox>("MusicCheck").IsChecked == true,
                ScoreShuffle = C<CheckBox>("ShuffleCheck").IsChecked == true,
                ScoreRepeat = C<CheckBox>("RepeatCheck").IsChecked == true,
                AutoScroll = C<CheckBox>("AutoScrollCheck").IsChecked == true,
                PlayIntro = C<CheckBox>("IntroCheck").IsChecked == true,
                StretchMovies = C<CheckBox>("StretchCheck").IsChecked == true
            };
        }
        private static bool EqualSettings(GameSettings a, GameSettings b)
        {
            return a.Yuri == b.Yuri && a.Width == b.Width && a.Height == b.Height && a.DisplayMode == b.DisplayMode
                && NormalizeFilter(a.Filter) == NormalizeFilter(b.Filter)
                && Math.Abs(a.SoundVolume - b.SoundVolume) < 0.005 && Math.Abs(a.VoiceVolume - b.VoiceVolume) < 0.005 && Math.Abs(a.ScoreVolume - b.ScoreVolume) < 0.005
                && a.ScrollRate == b.ScrollRate && a.InGameMusic == b.InGameMusic && a.ScoreShuffle == b.ScoreShuffle && a.ScoreRepeat == b.ScoreRepeat && a.AutoScroll == b.AutoScroll && a.PlayIntro == b.PlayIntro && a.StretchMovies == b.StretchMovies;
        }
        private static string NormalizeFilter(string f) { return f == "point(0)" ? "point" : f; }
        private bool Apply()
        {
            if (busy) return false;
            if (smokeOutput == null && core.IsGameRunning())
            {
                SetStatus("Close the game before applying settings.", "Your pending edits are still here.");
                return false;
            }
            // Also check here: an editor may write a file between the last timer tick and this click.
            if (!RefreshSavedSettings()) return false;
            busy = true;
            try
            {
                if (smartAi) LauncherApp.VerifySmartAiPackage(gameRoot);
                core.Apply(Capture());
                Load();
                SetStatus("Settings applied to " + GameName() + ".", "Previous settings backed up. You can undo this from Backup & Restore.");
                return true;
            }
            catch (Exception ex) { ShowError(ex); return false; }
            finally { busy = false; RefreshButtons(); }
        }
        private void Restore(bool original)
        {
            if (busy) return;
            string question = IsIsolated
                ? (original ? "Restore the enhanced copy’s starting display, audio and gameplay settings?" : "Undo the most recent settings change in the enhanced copy?")
                : (original ? "Restore the original settings for both games?" : "Undo the most recent settings change for both games?");
            if (MessageBox.Show(window, question + "\n\nYour current saved settings will be backed up first. Unsaved edits in this window will be discarded. Saves and game binaries will not be changed.", "Restore settings", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            busy = true;
            try
            {
                if (smartAi) LauncherApp.VerifySmartAiPackage(gameRoot);
                if (original) core.RestoreOriginal(); else core.RestorePrevious();
                Load();
                SetStatus(IsIsolated ? (original ? "Enhanced starting settings restored." : "Previous enhanced settings restored.") : (original ? "Original settings restored for both games." : "Previous settings restored for both games."), "The settings replaced by this restore are available with Undo last settings change.");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; RefreshButtons(); }
        }
        private void RefreshButtons()
        {
            bool running = smokeOutput == null && core.IsGameRunning();
            C<Button>("ApplyButton").IsEnabled = dirty && !running && !busy && !externalChanges;
            C<Button>("DiscardButton").IsEnabled = (dirty || externalChanges) && !busy;
            C<Button>("RestoreOriginalButton").IsEnabled = !running && !busy;
            C<Button>("UndoButton").IsEnabled = core.HasPreviousSnapshot && !running && !busy;
            C<Button>("LaunchButton").IsEnabled = !running && !busy && !externalChanges;
            C<Button>("EnhancedModeButton").IsEnabled = !running && !busy;
            C<Button>("NativeModeButton").IsEnabled = !running && !busy;
            C<CheckBox>("SmartAiCheck").IsEnabled = !running && !busy;
            C<ComboBox>("HealthBarMode").IsEnabled = !running && !busy;
            C<ComboBox>("AutosaveInterval").IsEnabled = !running && !busy;
            SaveInfo chosenSave = SelectedSave();
            C<Button>("RenameSaveButton").IsEnabled = chosenSave != null && chosenSave.Error == null && !running && !busy;
            C<Button>("DeleteSaveButton").IsEnabled = chosenSave != null && !running && !busy;
            bool chosen = C<ListBox>("PresetList").SelectedItem != null;
            C<Button>("UsePresetButton").IsEnabled = chosen && !running && !busy && !presetsDamaged;
            C<Button>("DeletePresetButton").IsEnabled = chosen && !busy && !presetsDamaged;
            C<Button>("SavePresetButton").IsEnabled = !busy && !presetsDamaged;
            foreach (string check in FeatureChecks) C<CheckBox>(check).IsEnabled = !running && !busy;
            C<Button>("LaunchButton").Content = running ? "GAME IS RUNNING" : dirty ? "▶   APPLY & LAUNCH" : "▶   LAUNCH GAME";
            C<TextBlock>("LaunchHint").Text = running ? "Exit the game before applying settings." : externalChanges ? "Reload the saved settings to continue." : dirty ? "Saves your changes before launch." : "Launches with your saved settings.";
        }
        private void Launch()
        {
            if (smokeOutput != null) throw new InvalidOperationException("Game launch is disabled in UI smoke tests.");
            if (core.IsGameRunning()) { SetStatus("A game is already running.", "Exit it before launching another session."); return; }
            if (!RefreshSavedSettings()) return;
            try { if (enhanced) LauncherApp.VerifyEnhancedPackage(gameRoot); if (smartAi) LauncherApp.VerifySmartAiPackage(gameRoot); VerifyGamePrerequisite(); }
            catch (Exception ex) { ShowError(ex); return; }
            if (dirty && !Apply()) return;
            TryAction(delegate
            {
                // Use the installed official entry points; retain Steam's existing launch behavior.
                ProcessStartInfo start = LauncherApp.CreateGameStartInfo(gameRoot, yuri, enhanced, false, smartAi);
                if (!File.Exists(start.FileName)) throw new FileNotFoundException("The selected game launcher was not found.", start.FileName);
                Process p = Process.Start(start);
                if (p == null) throw new InvalidOperationException("Windows did not start the game launcher.");
                p.Dispose();
                SetStatus("Launch requested for " + GameName() + ".", IsIsolated ? "The enhanced copy uses its own settings and saves." : "Steam or the game may take a moment to open.");
                window.WindowState = WindowState.Minimized;
            });
        }
        private void OpenOtherMode(string mode)
        {
            if (smokeOutput != null) throw new InvalidOperationException("Opening another launcher is disabled in UI smoke tests.");
            if (core.IsGameRunning() || !ResolveDraft()) return;
            string smartRoot = Path.Combine(CommandCenterRoot, "SmartRA2");
            if (mode == "--smart-ai" && !Directory.Exists(smartRoot))
            {
                SetUpEnhancedRa2(smartRoot, delegate { OpenOtherMode(mode); });
                return;
            }
            TryAction(delegate
            {
                if (mode == "--enhanced") LauncherApp.VerifyEnhancedPackage(Path.Combine(CommandCenterRoot, "EnhancedYR"));
                else if (mode == "--smart-ai") LauncherApp.VerifySmartAiPackage(smartRoot);
                string executable = Assembly.GetExecutingAssembly().Location;
                Process p = Process.Start(new ProcessStartInfo(executable) { Arguments = mode, WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = true });
                if (p == null) throw new InvalidOperationException("Windows did not start Command Center.");
                p.Dispose();
                window.WindowState = WindowState.Minimized;
            });
        }
        // The folder holding RA2CommandCenter.exe; the edition folders live beside it whatever it is called.
        private static string CommandCenterRoot { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        private void SetUpEnhancedRa2(string target, Action then)
        {
            // No full path here: Windows message boxes treat '&' (as in "Command & Conquer") as a shortcut marker.
            string message = "Enhanced Red Alert 2 is not set up on this PC yet.\n\nSet it up now? Command Center will create a separate copy of the game in a folder named " +
                Path.GetFileName(target) + ", inside the folder that holds RA2CommandCenter.exe." +
                "\n\nYour original game files and saves are not changed. When the copy is on the same drive as the game, the large game archives are shared using hard links, so it needs little extra space. Otherwise it needs about 3 GB.";
            if (MessageBox.Show(window, message, "Set up Enhanced Red Alert 2", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            busy = true;
            RefreshButtons();
            window.Cursor = Cursors.Wait;
            SetStatus("Setting up Enhanced Red Alert 2…", "Checking your game files.");
            string package = EditionSetup.PackagePath(CommandCenterRoot);
            var worker = new Thread(delegate()
            {
                Exception failure = null;
                try
                {
                    EditionSetup.CreateSmartRa2(originalRoot, package, target, delegate(string text)
                    {
                        window.Dispatcher.BeginInvoke(new Action(delegate { SetStatus("Setting up Enhanced Red Alert 2…", text); }));
                    });
                }
                catch (Exception ex) { failure = ex; }
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    busy = false;
                    window.Cursor = null;
                    RefreshButtons();
                    if (failure != null) { ShowError(failure); return; }
                    SetStatus("Enhanced Red Alert 2 is ready.", "Opening its Command Center…");
                    then();
                }));
            });
            worker.IsBackground = true;
            worker.Start();
        }
        private static void VerifyGamePrerequisite()
        {
            // The native games are 32-bit even when Command Center runs as a 64-bit process.
            using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.ClassesRoot, Microsoft.Win32.RegistryView.Registry32))
            using (var key = root.OpenSubKey(@"CLSID\{1440AD10-6AA8-11D1-B6F9-00A024DDAFD1}\InprocServer32"))
            {
                string library = key == null ? null : key.GetValue("") as string;
                if (String.IsNullOrWhiteSpace(library) || !File.Exists(Environment.ExpandEnvironmentVariables(library.Trim('"'))))
                    throw new InvalidOperationException("The game's Blowfish support component is not registered correctly. Launch the game once from Steam to complete its first-time setup, then return to Command Center. Your settings have not been applied by this launch attempt.");
            }
        }
        private void RunUtility(string relative)
        {
            TryAction(delegate
            {
                string file = Path.Combine(originalRoot, relative);
                if (!File.Exists(file)) throw new FileNotFoundException("This tool or manual is missing from the game installation.", file);
                Process.Start(new ProcessStartInfo(file) { WorkingDirectory = Path.GetDirectoryName(file), UseShellExecute = true });
            });
        }
        private void TryAction(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
        private void ShowError(Exception ex)
        {
            SetStatus("The operation could not finish.", ex.Message);
            if (smokeOutput == null) MessageBox.Show(window, ex.Message, "Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
            else throw new InvalidOperationException("UI smoke operation failed.", ex);
        }
        private void SetStatus(string title, string detail) { C<TextBlock>("StatusText").Text = title; C<TextBlock>("StatusDetail").Text = detail; }

        internal void RunSmoke()
        {
            try
            {
                string[] names = { "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" };
                byte[][] original = names.Select(n => File.ReadAllBytes(Path.Combine(gameRoot, n))).ToArray();
                byte[][] standardOriginal = IsIsolated ? names.Select(n => File.ReadAllBytes(Path.Combine(originalRoot, n))).ToArray() : null;
                ProcessStartInfo[] requests = {
                    LauncherApp.CreateGameStartInfo(gameRoot, false, false, false),
                    LauncherApp.CreateGameStartInfo(gameRoot, true, false, false),
                    LauncherApp.CreateGameStartInfo(gameRoot, true, true, false),
                    LauncherApp.CreateGameStartInfo(gameRoot, true, false, true),
                    LauncherApp.CreateGameStartInfo(gameRoot, true, false, false, true)
                };
                string[] expectedLaunchers = { "Ra2.exe", "RA2MD.exe", "Syringe.exe", "Ra2.exe", "Ra2.exe" };
                for (int i = 0; i < requests.Length; ++i)
                {
                    if (requests[i].FileName != Path.Combine(gameRoot, expectedLaunchers[i]) || requests[i].WorkingDirectory != gameRoot || !requests[i].UseShellExecute ||
                        requests[i].Arguments != (i == 2 ? "gamemd.exe -i=Ares.dll -i=Phobos.dll" : ""))
                        throw new Exception("Game launch target, arguments or working folder changed for mode " + i);
                }
                bool mixedModesBlocked = false;
                try { LauncherApp.CreateGameStartInfo(gameRoot, true, true, true); }
                catch (InvalidOperationException) { mixedModesBlocked = true; }
                if (!mixedModesBlocked) throw new Exception("Conflicting enhanced modes were accepted.");
                foreach (bool useNative in new[] { false, true })
                {
                    bool blocked = false;
                    try { LauncherApp.CreateGameStartInfo(gameRoot, false, !useNative, useNative, true); }
                    catch (InvalidOperationException) { blocked = true; }
                    if (!blocked) throw new Exception("Smart AI accepted a conflicting enhanced edition.");
                }
                if (C<Button>("SmartModeButton").Visibility != Visibility.Collapsed || C<Button>("NativeModeButton").Visibility != Visibility.Visible)
                    throw new Exception("The sidebar does not show exactly two edition switches.");
                if (C<Button>("NativeModeButton").Content.ToString() != (smartAi ? "Enhanced Yuri UI" : "Enhanced Red Alert 2") ||
                    C<Button>("EnhancedModeButton").Content.ToString() != (IsIsolated ? "Standard games" : "Enhanced Yuri UI"))
                    throw new Exception("Edition switch labels do not match the consolidated editions.");
                if (enhanced)
                {
                    if (!yuri || C<RadioButton>("YuriChoice").IsChecked != true || C<RadioButton>("YuriChoice").IsEnabled
                        || C<Border>("Ra2FeatureInfo").Visibility != Visibility.Collapsed
                        || C<RadioButton>("Ra2Choice").Visibility != Visibility.Collapsed
                        || C<TabItem>("EnhancedInfoTab").Visibility != Visibility.Visible
                        || window.Title.IndexOf("Enhanced", StringComparison.Ordinal) < 0)
                        throw new Exception("Enhanced mode was not clearly identified and locked to Yuri.");
                    SwitchGame(false);
                    if (!yuri) throw new Exception("Enhanced mode allowed switching to the original game.");
                    LauncherApp.VerifyEnhancedPackage(gameRoot);
                    foreach (string name in new[] { "READY.json", "Syringe.exe", "gamemd.exe", "Ares.dll", "Ares.dll.inj", "Phobos.dll", "ddraw.dll", "ares.mix", "rulesmd.ini", "artmd.ini", "uimd.ini", "ra2md.csf" })
                    {
                        string file = Path.Combine(gameRoot, name), held = file + ".held";
                        File.Move(file, held);
                        bool blocked = false;
                        try
                        {
                            try { LauncherApp.VerifyEnhancedPackage(gameRoot); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.Move(held, file); }
                        if (!blocked) throw new Exception("An enhanced package missing " + name + " was accepted.");
                    }
                    string marker = Path.Combine(gameRoot, "READY.json");
                    byte[] markerBytes = File.ReadAllBytes(marker);
                    bool unsafeMarkerBlocked = false;
                    try
                    {
                        File.WriteAllText(marker, "{\"isolated\":false}");
                        try { LauncherApp.VerifyEnhancedPackage(gameRoot); }
                        catch (InvalidOperationException) { unsafeMarkerBlocked = true; }
                    }
                    finally { File.WriteAllBytes(marker, markerBytes); }
                    if (!unsafeMarkerBlocked) throw new Exception("A mismatched enhanced package readiness record was accepted.");
                }
                else if (smartAi)
                {
                    Action verifyOriginalEdition = delegate { LauncherApp.VerifySmartAiPackage(gameRoot); };
                    if (yuri || C<RadioButton>("Ra2Choice").IsChecked != true || C<RadioButton>("Ra2Choice").IsEnabled ||
                        C<RadioButton>("YuriChoice").Visibility != Visibility.Collapsed ||
                        C<TabItem>("EnhancedInfoTab").Visibility != Visibility.Visible ||
                        window.Title.IndexOf("Experimental", StringComparison.Ordinal) < 0 ||
                        C<Button>("EnhancedModeButton").Content.ToString() != "Standard games" ||
                        C<Button>("NativeModeButton").Content.ToString() != "Enhanced Yuri UI")
                        throw new Exception("Native RA2 mode was not clearly identified, locked or reversible from its UI.");
                    SwitchGame(true);
                    if (yuri) throw new Exception("Native RA2 mode allowed switching to Yuri inside its copy.");
                    verifyOriginalEdition();
                    foreach (string name in new[] { "READY.json", "game.exe", "Ra2.exe", "RA2NativeUI.dll", "dsound.dll", "RA2NativeUI.ini", "ddraw.dll", "RA2.INI", "RA2MD.INI", "DDrawCompat.ini" }.Concat(smartAi ? new[] { "ai.ini" } : new string[0]))
                    {
                        string file = Path.Combine(gameRoot, name), held = file + ".held";
                        File.Move(file, held);
                        bool blocked = false;
                        try
                        {
                            try { verifyOriginalEdition(); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.Move(held, file); }
                        if (!blocked) throw new Exception("A native RA2 package missing " + name + " was accepted.");
                    }
                    string marker = Path.Combine(gameRoot, "READY.json");
                    byte[] markerBytes = File.ReadAllBytes(marker);
                    foreach (string wrong in new[] { "{\"status\":\"pending\"}", "{\"status\":\"ready\",\"engine\":\"yuri\",\"version\":\"0.1.0\",\"hostSha256\":\"" + LauncherApp.NativeHostSha256 + "\"}" })
                    {
                        bool blocked = false;
                        try
                        {
                            File.WriteAllText(marker, wrong);
                            try { verifyOriginalEdition(); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.WriteAllBytes(marker, markerBytes); }
                        if (!blocked) throw new Exception("An unsupported native readiness marker was accepted.");
                    }
                    string host = Path.Combine(gameRoot, "game.exe");
                    byte[] hostBytes = File.ReadAllBytes(host);
                    bool wrongHostBlocked = false;
                    try
                    {
                        byte[] changedHost = (byte[])hostBytes.Clone();
                        changedHost[changedHost.Length - 1] ^= 1;
                        File.WriteAllBytes(host, changedHost);
                        try { verifyOriginalEdition(); }
                        catch (InvalidOperationException) { wrongHostBlocked = true; }
                    }
                    finally { File.WriteAllBytes(host, hostBytes); }
                    if (!wrongHostBlocked) throw new Exception("An unsupported native game executable was accepted.");
                    if (File.Exists(Path.Combine(gameRoot, "Syringe.exe")) || File.Exists(Path.Combine(gameRoot, "Phobos.dll")))
                        throw new Exception("Native smoke fixture unexpectedly depends on the Yuri extension package.");
                }
                else if (C<TabItem>("EnhancedInfoTab").Visibility != Visibility.Collapsed || yuri)
                    throw new Exception("Standard mode unexpectedly enabled the enhanced interface.");
                if (smartAi)
                {
                    if (C<TextBlock>("Ra2ChoiceTitle").Text != "ENHANCED RA2" ||
                        C<StackPanel>("SmartAiInfo").Visibility != Visibility.Visible ||
                        C<Border>("Ra2FeatureInfo").Visibility != Visibility.Visible ||
                        C<TabItem>("EnhancedInfoTab").Header.ToString() != "UI & AI")
                        throw new Exception("Enhanced Red Alert 2 information is not visible.");
                    string ai = Path.Combine(gameRoot, "ai.ini");
                    byte[] aiBytes = File.ReadAllBytes(ai);
                    string aiOff = Path.Combine(gameRoot, "ai.ini.off");
                    CheckBox aiCheck = C<CheckBox>("SmartAiCheck");
                    if (aiCheck.IsChecked != true || C<TextBlock>("EnhancedBannerText").Text.IndexOf("AI: ON", StringComparison.Ordinal) < 0)
                        throw new Exception("The smarter AI switch does not show the installed ai.ini as on.");
                    aiCheck.IsChecked = false;
                    if (File.Exists(ai) || !File.Exists(aiOff) || !aiBytes.SequenceEqual(File.ReadAllBytes(aiOff)) ||
                        C<TextBlock>("EnhancedBannerText").Text.IndexOf("AI: OFF", StringComparison.Ordinal) < 0 || C<TextBlock>("Ra2ChoiceHint").Text.IndexOf("original AI", StringComparison.Ordinal) < 0)
                        throw new Exception("Turning the smarter AI off did not set ai.ini aside intact.");
                    LauncherApp.VerifySmartAiPackage(gameRoot);
                    C<TabControl>("SettingsTabs").SelectedItem = C<TabItem>("EnhancedInfoTab");
                    CaptureWindow("07-ai-off.png");
                    C<TabControl>("SettingsTabs").SelectedIndex = 0;
                    foreach (Action tamper in new Action[] { delegate { File.WriteAllBytes(ai, aiBytes); }, delegate { File.WriteAllText(aiOff, "Changed disabled AI fixture"); } })
                    {
                        bool blocked = false;
                        try
                        {
                            tamper();
                            try { LauncherApp.VerifySmartAiPackage(gameRoot); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { if (File.Exists(ai)) File.Delete(ai); File.WriteAllBytes(aiOff, aiBytes); }
                        if (!blocked) throw new Exception("Both AI files, or a changed disabled AI file, were accepted.");
                    }
                    aiCheck.IsChecked = true;
                    if (!File.Exists(ai) || File.Exists(aiOff) || !aiBytes.SequenceEqual(File.ReadAllBytes(ai)) || aiCheck.IsChecked != true)
                        throw new Exception("Turning the smarter AI back on did not restore ai.ini intact.");
                    ComboBox bars = C<ComboBox>("HealthBarMode");
                    string uiIni = Path.Combine(gameRoot, "RA2NativeUI.ini");
                    byte[] uiBytes = File.ReadAllBytes(uiIni);
                    if (bars.SelectedIndex != 1 || LauncherApp.ReadHealthBarMode(gameRoot) != 1)
                        throw new Exception("Health bars did not default to damaged units and buildings.");
                    foreach (int mode in new[] { 2, 0, 1 })
                    {
                        bars.SelectedIndex = mode;
                        if (LauncherApp.ReadHealthBarMode(gameRoot) != mode)
                            throw new Exception("Health bar mode " + mode + " was not saved.");
                    }
                    if (File.ReadAllText(uiIni).IndexOf("ExtendedTooltips=yes", StringComparison.Ordinal) < 0)
                        throw new Exception("Saving the health bar mode lost other extension settings.");
                    File.WriteAllText(uiIni, File.ReadAllText(uiIni).Replace("HealthBars=1", "HealthBars=2"));
                    SyncSmartAi();
                    if (bars.SelectedIndex != 2) throw new Exception("An external health bar change was not shown.");
                    File.WriteAllBytes(uiIni, uiBytes);
                    SyncSmartAi();
                    if (bars.SelectedIndex != 1) throw new Exception("Health bar display did not follow the restored settings file.");
                    for (int i = 0; i < FeatureChecks.Length; i++)
                    {
                        CheckBox check = C<CheckBox>(FeatureChecks[i]);
                        string key = LauncherApp.FeatureSwitches[i];
                        if (check.IsChecked != true || !LauncherApp.ReadFeature(gameRoot, key))
                            throw new Exception(FeatureNames[i] + " did not default to on.");
                        check.IsChecked = false;
                        if (LauncherApp.ReadUiSetting(gameRoot, key, 1) != 0)
                            throw new Exception(FeatureNames[i] + " was not switched off in RA2NativeUI.ini.");
                        check.IsChecked = true;
                        if (LauncherApp.ReadUiSetting(gameRoot, key, 0) != 1)
                            throw new Exception(FeatureNames[i] + " was not switched back on.");
                    }
                    File.WriteAllText(uiIni, File.ReadAllText(uiIni).Replace("GameClock=1", "GameClock=0"));
                    SyncSmartAi();
                    if (C<CheckBox>("GameClockCheck").IsChecked != false || C<CheckBox>("QuickSaveCheck").IsChecked != true)
                        throw new Exception("Feature switches did not follow an external settings change.");
                    if (File.ReadAllText(uiIni).IndexOf("ExtendedTooltips=yes", StringComparison.Ordinal) < 0)
                        throw new Exception("Feature switches lost other extension settings.");
                    ComboBox interval = C<ComboBox>("AutosaveInterval");
                    if (interval.SelectedIndex != 1 || LauncherApp.ReadAutosaveMinutes(gameRoot) != 10)
                        throw new Exception("Autosave did not default to every 10 minutes.");
                    interval.SelectedIndex = 0;
                    if (LauncherApp.ReadAutosaveMinutes(gameRoot) != 5) throw new Exception("The autosave interval was not saved.");
                    interval.SelectedIndex = 3;
                    if (LauncherApp.ReadAutosaveMinutes(gameRoot) != 30) throw new Exception("The longest autosave interval was not saved.");
                    File.WriteAllBytes(uiIni, uiBytes);
                    SyncSmartAi();
                    C<TabControl>("SettingsTabs").SelectedItem = C<TabItem>("EnhancedInfoTab");
                    CaptureWindow("08-feature-switches.png");
                    C<TabControl>("SettingsTabs").SelectedIndex = 0;
                    foreach (byte[] invalid in new[] { new byte[0], System.Text.Encoding.UTF8.GetBytes("Changed AI fixture") })
                    {
                        bool blocked = false;
                        try
                        {
                            File.WriteAllBytes(ai, invalid);
                            try { LauncherApp.VerifySmartAiPackage(gameRoot); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.WriteAllBytes(ai, aiBytes); }
                        if (!blocked) throw new Exception("Empty or changed AI content was accepted.");
                    }
                    foreach (string name in new[] { "rules.ini", "art.ini" })
                    {
                        string added = Path.Combine(gameRoot, name);
                        bool blocked = false;
                        try
                        {
                            File.WriteAllText(added, "; Isolated forbidden balance-file fixture");
                            try { LauncherApp.VerifySmartAiPackage(gameRoot); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.Delete(added); }
                        if (!blocked) throw new Exception("Smart AI accepted an added " + name);
                    }
                    string marker = Path.Combine(gameRoot, "READY.json");
                    string validMarker = File.ReadAllText(marker);
                    foreach (string badMarker in new[] {
                        validMarker.Replace("\"edition\":\"smart-ai\"", "\"edition\":\"native-ui\""),
                        validMarker.Replace("\"aiVersion\":\"0.1.0\"", "\"aiVersion\":\"9.9.9\""),
                        validMarker.Replace(LauncherApp.FileSha256(ai), new string('0', 64)) })
                    {
                        bool blocked = false;
                        try
                        {
                            File.WriteAllText(marker, badMarker);
                            try { LauncherApp.VerifySmartAiPackage(gameRoot); }
                            catch (InvalidOperationException) { blocked = true; }
                        }
                        finally { File.WriteAllText(marker, validMarker); }
                        if (!blocked) throw new Exception("Smart AI accepted a wrong edition, AI version or checksum record.");
                    }
                    C<Slider>("SoundSlider").Value = 35;
                    int beforeBackups = Directory.GetFileSystemEntries(core.BackupsPath).Length;
                    bool applyBlocked = false;
                    try
                    {
                        File.AppendAllText(ai, "; Invalid checksum before Apply");
                        try { applyBlocked = !Apply(); }
                        catch (InvalidOperationException) { applyBlocked = true; }
                    }
                    finally { File.WriteAllBytes(ai, aiBytes); }
                    if (!applyBlocked || Directory.GetFileSystemEntries(core.BackupsPath).Length != beforeBackups)
                        throw new Exception("Invalid AI content allowed Apply or created a settings snapshot.");
                    for (int i = 0; i < names.Length; ++i)
                        if (!original[i].SequenceEqual(File.ReadAllBytes(Path.Combine(gameRoot, names[i]))))
                            throw new Exception("Invalid AI content allowed a write to " + names[i]);
                    C<Button>("DiscardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    LauncherApp.VerifySmartAiPackage(gameRoot);
                }
                if (C<Slider>("ScrollSlider").Value != 2) throw new Exception("Stored scroll setting 4 must map to UI speed 2.");
                CaptureWindow("01-display-classic.png");
                C<RadioButton>("ReadableProfile").IsChecked = true;
                if (!dirty || !C<Button>("ApplyButton").IsEnabled) throw new Exception("Profile selection did not mark the draft dirty.");
                C<Button>("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (dirty || core.ReadSettings(enhanced).Width != 1280) throw new Exception("Apply action failed to save the chosen profile.");
                if (!IsIsolated)
                {
                    C<RadioButton>("YuriChoice").IsChecked = true;
                    if (!yuri || width != 640) throw new Exception("Game selection did not load independent settings.");
                }
                C<RadioButton>("BalancedProfile").IsChecked = true;
                C<ComboBox>("DisplayMode").SelectedIndex = 1;
                C<ComboBox>("ScalingMode").SelectedIndex = 1;
                C<Slider>("SoundSlider").Value = 45;
                C<Slider>("ScrollSlider").Value = 6;
                C<CheckBox>("IntroCheck").IsChecked = false;
                C<Button>("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (core.ReadSettings(yuri).Width != 1600 || core.ReadSettings(!yuri).Width != (IsIsolated ? 640 : 1280)) throw new Exception("Per-game settings isolation failed.");
                if (core.ReadSettings(yuri).ScrollRate != 0) throw new Exception("Maximum UI scroll speed must save stored setting 0.");
                CaptureWindow("02-display-balanced.png");
                C<TabControl>("SettingsTabs").SelectedIndex = 1;
                CaptureWindow("03-audio-gameplay.png");
                C<TabControl>("SettingsTabs").SelectedIndex = 2;
                CaptureWindow("04-backup-restore.png");
                core.RestorePrevious(); Load();
                if (core.ReadSettings(yuri).Width != (IsIsolated ? 1280 : 640) || core.ReadSettings(!yuri).Width != (IsIsolated ? 640 : 1280)) throw new Exception("Undo did not restore the previous full configuration snapshot.");
                core.RestoreOriginal(); Load();
                for (int i = 0; i < names.Length; i++) if (!original[i].SequenceEqual(File.ReadAllBytes(Path.Combine(gameRoot, names[i])))) throw new Exception("Restore was not byte-exact for " + names[i]);
                string activeConfig = Path.Combine(gameRoot, yuri ? "RA2MD.INI" : "RA2.INI");
                File.WriteAllText(activeConfig, File.ReadAllText(activeConfig).Replace("ScreenWidth=640", "ScreenWidth=1280").Replace("ScreenHeight=480", "ScreenHeight=720"));
                if (!RefreshSavedSettings() || dirty || width != 1280 || C<TextBlock>("SavedResolution").Text != "1280 × 720")
                    throw new Exception("Clean settings did not reload after an external edit.");
                C<Slider>("SoundSlider").Value = 35;
                string sharedConfig = Path.Combine(gameRoot, "DDrawCompat.ini");
                File.AppendAllText(sharedConfig, "# External note with no visible settings change\r\n");
                byte[] editedShared = File.ReadAllBytes(sharedConfig);
                byte[] editedActive = File.ReadAllBytes(activeConfig);
                if (RefreshSavedSettings() || !dirty || !externalChanges || C<Slider>("SoundSlider").Value != 35 || C<Button>("ApplyButton").IsEnabled || C<Button>("LaunchButton").IsEnabled)
                    throw new Exception("An external comment edit did not protect the pending draft.");
                if (Apply() || !editedShared.SequenceEqual(File.ReadAllBytes(sharedConfig)) || !editedActive.SequenceEqual(File.ReadAllBytes(activeConfig)))
                    throw new Exception("Apply overwrote externally changed settings.");
                C<Button>("DiscardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (dirty || externalChanges || C<Slider>("SoundSlider").Value != 70 || width != 1280)
                    throw new Exception("Discard did not reload the externally changed settings.");
                C<Slider>("SoundSlider").Value = 40;
                string otherConfig = Path.Combine(gameRoot, yuri ? "RA2.INI" : "RA2MD.INI");
                File.AppendAllText(otherConfig, "# Changed immediately before Apply\r\n");
                byte[] editedOther = File.ReadAllBytes(otherConfig);
                if (Apply() || !externalChanges || C<Slider>("SoundSlider").Value != 40 || !editedOther.SequenceEqual(File.ReadAllBytes(otherConfig)))
                    throw new Exception("The Apply click failed to detect a change before the next timer tick.");
                C<Button>("DiscardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                core.RestoreOriginal(); Load();
                for (int i = 0; i < names.Length; i++) if (!original[i].SequenceEqual(File.ReadAllBytes(Path.Combine(gameRoot, names[i])))) throw new Exception("Post-conflict restore was not byte-exact for " + names[i]);
                // Music options use the staged Apply path.
                bool shuffle = core.ReadSettings(yuri).ScoreShuffle;
                C<CheckBox>("ShuffleCheck").IsChecked = !shuffle;
                if (!dirty) throw new Exception("Changing music shuffle did not mark the draft dirty.");
                if (!Apply() || core.ReadSettings(yuri).ScoreShuffle == shuffle || core.ReadSettings(!yuri).ScoreShuffle != shuffle)
                    throw new Exception("Music shuffle was not applied to the selected game only.");
                // Skirmish presets: summary, save, use (with backup), undo and delete.
                string skirmishConfig = Path.Combine(gameRoot, yuri ? "RA2MD.INI" : "RA2.INI");
                if (C<TextBlock>("SkirmishCurrent").Text.IndexOf("No skirmish", StringComparison.Ordinal) < 0)
                    throw new Exception("An empty skirmish setup was not explained.");
                File.AppendAllText(skirmishConfig, "\r\n[Skirmish]\r\nCredits=20000\r\nSlot01=5,8,1\r\nSlot02=4,-2,-2\r\nSuperWeaponsAllowed=no\r\n");
                if (!RefreshSavedSettings() || !C<TextBlock>("SkirmishCurrent").Text.StartsWith("2 opponents: Medium (Russia), Brutal (random country)", StringComparison.Ordinal))
                    throw new Exception("The current skirmish setup was not summarised: " + C<TextBlock>("SkirmishCurrent").Text);
                C<TextBox>("PresetName").Text = "  1v1 test  ";
                C<Button>("SavePresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (C<ListBox>("PresetList").Items.Count != 1 || core.LoadPresets()[0].Name != "1v1 test" || core.LoadPresets()[0].Yuri != yuri)
                    throw new Exception("Saving the current skirmish setup as a preset failed.");
                File.WriteAllText(skirmishConfig, File.ReadAllText(skirmishConfig).Replace("Credits=20000", "Credits=5000"));
                if (!RefreshSavedSettings()) throw new Exception("External skirmish change was not reloaded.");
                C<ListBox>("PresetList").SelectedIndex = 0;
                if (!C<Button>("UsePresetButton").IsEnabled) throw new Exception("Use preset is unavailable for a selected preset.");
                C<TabControl>("SettingsTabs").SelectedItem = C<TabItem>("SkirmishTab");
                CaptureWindow(IsIsolated ? "09-skirmish-presets.png" : "09-skirmish-presets-standard.png");
                C<TabControl>("SettingsTabs").SelectedIndex = 0;
                C<Button>("UsePresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (core.ReadSkirmish(yuri)["Credits"] != "20000" || !core.HasPreviousSnapshot)
                    throw new Exception("Using a preset did not restore its setup with a backup.");
                core.RestorePrevious(); Load();
                if (core.ReadSkirmish(yuri)["Credits"] != "5000") throw new Exception("Undo did not reverse the preset.");
                C<ListBox>("PresetList").SelectedIndex = 0;
                C<Button>("DeletePresetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (C<ListBox>("PresetList").Items.Count != 0 || core.LoadPresets().Count != 0) throw new Exception("Deleting a preset failed.");
                core.RestoreOriginal(); Load();
                for (int i = 0; i < names.Length; i++) if (!original[i].SequenceEqual(File.ReadAllBytes(Path.Combine(gameRoot, names[i])))) throw new Exception("Restore after presets was not byte-exact for " + names[i]);
                // Saved games: per-game list, rename with backup, delete to a recoverable folder.
                C<TabControl>("SettingsTabs").SelectedItem = C<TabItem>("SavesTab");
                ListBox saveList = C<ListBox>("SaveList");
                int expected = yuri ? 1 : 2;
                if (saveList.Items.Count != expected) throw new Exception("Expected " + expected + " saves for " + GameName() + ", found " + saveList.Items.Count + ".");
                saveList.SelectedIndex = 0;
                SaveInfo chosenForRename = SelectedSave();
                if (C<TextBox>("SaveName").Text != chosenForRename.Title || !C<Button>("RenameSaveButton").IsEnabled)
                    throw new Exception("Selecting a save did not offer its name for renaming.");
                C<TextBox>("SaveName").Text = "Renamed by smoke test";
                C<Button>("RenameSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (SaveCatalog.Read(chosenForRename.Path).Title != "Renamed by smoke test" || !Directory.Exists(SaveBackupsPath) || Directory.GetFiles(SaveBackupsPath).Length != 1)
                    throw new Exception("Renaming a save failed or kept no backup.");
                if (!yuri)
                {
                    bool labelled = saveList.Items.Cast<ListBoxItem>().Any(delegate(ListBoxItem i) { return ((SaveInfo)i.Tag).Kind == "Quick save"; });
                    if (!labelled) throw new Exception("The quick save was not labelled.");
                }
                CaptureWindow(IsIsolated ? "10-saves.png" : "10-saves-standard.png");
                saveList.SelectedIndex = 0;
                string deletedPath = SelectedSave().Path;
                C<Button>("DeleteSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (File.Exists(deletedPath) || !File.Exists(Path.Combine(DeletedSavesPath, Path.GetFileName(deletedPath))) || saveList.Items.Count != expected - 1)
                    throw new Exception("Deleting a save did not move it to the Deleted saves folder.");
                C<TabControl>("SettingsTabs").SelectedIndex = 0;
                if (IsIsolated)
                {
                    for (int i = 0; i < names.Length; i++) if (!standardOriginal[i].SequenceEqual(File.ReadAllBytes(Path.Combine(originalRoot, names[i])))) throw new Exception("Enhanced mode modified original installation file " + names[i]);
                    C<TabControl>("SettingsTabs").SelectedItem = C<TabItem>("EnhancedInfoTab");
                    CaptureWindow("06-ingame-ui.png");
                }
                C<TabControl>("SettingsTabs").SelectedIndex = 0;
                window.Width = 980; window.Height = 690;
                CaptureWindow("05-minimum-window.png");
                File.WriteAllText(Path.Combine(smokeOutput, "ui-results.txt"), "PASS: WPF initialization; classic rendering; profile selection; Apply button; per-game switching; display mode/scaling; audio and checkbox editing; exact original restoration; previous-snapshot restoration; music shuffle staged per game; skirmish presets summarise, save, apply with backup, undo and delete; saved games listed per game with rename (backed up) and delete to a recoverable folder; clean external-settings reload; draft preservation and Apply/Launch blocking after an external comment edit; Discard reload; minimum-size layout.\r\nNo installed game settings or saves were changed.\r\n");
                File.AppendAllText(Path.Combine(smokeOutput, "ui-results.txt"), "PASS: All game-mode launch targets, arguments and working folders; conflicting mode rejection.\r\n");
                if (smartAi) File.AppendAllText(Path.Combine(smokeOutput, "ui-results.txt"), "PASS: Enhanced Red Alert 2 original-engine launch and lock; two edition switches with correct labels; smarter AI switch off/on keeps ai.ini byte-exact; health bar modes, the six feature switches and the autosave interval save, keep other extension settings and follow external edits; both-files and changed-disabled-file rejection; missing package, wrong host/marker and missing/empty/changed AI rejection; wrong edition/version/checksum marker rejection; loose rules/art rejection; invalid AI blocks Apply without settings or backup writes; no Syringe or Phobos dependency; original installation sentinel files unchanged through apply/undo/restore.\r\n");
                if (enhanced) File.AppendAllText(Path.Combine(smokeOutput, "ui-results.txt"), "PASS: Enhanced mode title, selected Yuri choice and original-game switch lock; incomplete package rejection; enhanced information tab; original installation sentinel files unchanged through enhanced apply/undo/restore.\r\n");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(smokeOutput, "ui-error.txt"), ex.ToString()); SmokeFailed = true; }
            finally { closingForSmoke = true; window.Close(); }
        }
        private void CaptureWindow(string name)
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            FrameworkElement content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(smokeOutput, name))) encoder.Save(stream);
        }
    }
}
