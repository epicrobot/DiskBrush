using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace DiskBrush
{
    /// <summary>One raw entry from the registry's Uninstall list.</summary>
    public sealed class UninstallEntry
    {
        public string KeyName, DisplayName, Publisher, InstallLocation, ParentKeyName, ReleaseType, DisplayIcon, UninstallString;
        public int SystemComponent;
    }

    /// <summary>One row of the Apps tab. SizeOnDisk is -1 while unknown.</summary>
    public sealed class AppInfo
    {
        public string Name, Publisher, InstallFolder, Note;
        public bool IsStoreApp;
        public bool Problem;          // shown with a red "!"
        public bool PossiblyUnused;   // shown with an orange "?": no folder of its own on disk
        public long SizeOnDisk = -1;
    }

    /// <summary>Finds installed desktop and Store apps, hiding Windows' own components, and measures their folders.</summary>
    public static class AppCatalog
    {
        const string NotRecorded = "Install folder not recorded";
        const string NoLongerExists = "Install folder no longer exists";
        const string UninstallPath =@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        static readonly Regex KbKey = new Regex(@"KB\d{6,}", RegexOptions.IgnoreCase);
        static readonly string[] FrameworkPrefixes =
        {
            "Microsoft.VCLibs", "Microsoft.NET.", "Microsoft.UI.Xaml", "Microsoft.WindowsAppRuntime",
            "Microsoft.Services.Store", "Microsoft.DirectXRuntime", "Microsoft.Windows", "MicrosoftWindows."
        };

        // ---------- Desktop apps ----------

        public static List<UninstallEntry> ReadRegistry()
        {
            var entries = new List<UninstallEntry>();
            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, entries);
            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, entries);
            ReadHive(RegistryHive.CurrentUser, RegistryView.Default, entries);
            return entries;
        }

        static void ReadHive(RegistryHive hive, RegistryView view, List<UninstallEntry> entries)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstall = baseKey.OpenSubKey(UninstallPath))
                {
                    if (uninstall == null) return;
                    foreach (string keyName in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey k = uninstall.OpenSubKey(keyName))
                        {
                            if (k == null) continue;
                            object sys = k.GetValue("SystemComponent");
                            entries.Add(new UninstallEntry
                            {
                                KeyName = keyName,
                                DisplayName = k.GetValue("DisplayName") as string,
                                Publisher = k.GetValue("Publisher") as string,
                                InstallLocation = k.GetValue("InstallLocation") as string,
                                ParentKeyName = k.GetValue("ParentKeyName") as string,
                                ReleaseType = k.GetValue("ReleaseType") as string,
                                DisplayIcon = k.GetValue("DisplayIcon") as string,
                                UninstallString = k.GetValue("UninstallString") as string,
                                SystemComponent = sys is int ? (int)sys : 0
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // A hive or view that can't be opened just contributes no apps.
            }
        }

        /// <summary>False for Windows components, updates, drivers and nameless entries.</summary>
        public static bool ShouldShow(UninstallEntry e)
        {
            if (string.IsNullOrWhiteSpace(e.DisplayName)) return false;
            if (e.SystemComponent == 1) return false;
            if (!string.IsNullOrEmpty(e.ParentKeyName)) return false;
            if (!string.IsNullOrEmpty(e.ReleaseType)) return false;   // Update, Hotfix, Security Update, Service Pack
            if (e.KeyName != null && KbKey.IsMatch(e.KeyName)) return false;
            if (e.DisplayName.StartsWith("Windows Driver Package", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>Returns a folder that is safe to measure for one app, or null with a reason.</summary>
        public static string UsableFolder(string installLocation, out string reason)
        {
            reason = null;
            string folder = (installLocation ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
            if (folder.Length <= 2 || !Path.IsPathRooted(folder)) { reason = NotRecorded; return null; }
            if (IsSharedFolder(folder)) { reason = "Install folder is a shared folder (" + folder + ")"; return null; }
            if (!Directory.Exists(folder)) { reason = NoLongerExists; return null; }
            return folder;
        }

        /// <summary>For entries without an install folder: the folder of the app's icon or uninstaller, if usable.</summary>
        public static string GuessFolder(UninstallEntry e)
        {
            foreach (string source in new[] { e.DisplayIcon, e.UninstallString })
            {
                string exe = ExecutablePath(source);
                if (exe == null) continue;
                if (exe.IndexOf(@"\Package Cache\", StringComparison.OrdinalIgnoreCase) >= 0
                    || exe.IndexOf(@"\Installer\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string dir;
                try { dir = Path.GetDirectoryName(exe); }
                catch (ArgumentException) { continue; }
                if (string.IsNullOrEmpty(dir)) continue;
                // Apps often keep their exe in a "bin" subfolder; measure the app folder above it.
                if (string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
                    dir = Path.GetDirectoryName(dir);
                string reason;
                string usable = UsableFolder(dir, out reason);
                if (usable != null) return usable;
            }
            return null;
        }

        /// <summary>Extracts the file path from strings like "C:\x\app.exe",0 or "C:\x\unins.exe" /SILENT.</summary>
        public static string ExecutablePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string s = value.Trim();
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                s = end > 1 ? s.Substring(1, end - 1) : s.Trim('"');
            }
            else
            {
                int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe > 0) s = s.Substring(0, exe + 4);
                else
                {
                    int comma = s.LastIndexOf(',');
                    if (comma > 0) s = s.Substring(0, comma);
                }
            }
            s = s.Replace('/', '\\');
            return s.Length > 3 && s[1] == ':' ? s : null;
        }

        static bool IsSharedFolder(string folder)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (folder.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)
                || folder.Equals(windows, StringComparison.OrdinalIgnoreCase)) return true;
            Environment.SpecialFolder[] shared =
            {
                Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86
            };
            foreach (Environment.SpecialFolder f in shared)
            {
                string path = Environment.GetFolderPath(f).TrimEnd('\\');
                if (path.Length > 0 && folder.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return folder.Length <= 3; // a drive root
        }

        /// <summary>Filters registry entries and merges duplicates (same name and folder).</summary>
        public static List<AppInfo> DesktopApps(IEnumerable<UninstallEntry> entries)
        {
            var apps = new List<AppInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UninstallEntry e in entries)
            {
                if (!ShouldShow(e)) continue;
                string reason;
                string folder = UsableFolder(e.InstallLocation, out reason);
                if (folder == null)
                {
                    string guessed = GuessFolder(e);
                    if (guessed != null)
                    {
                        folder = guessed;
                        reason = "Folder guessed from the app's icon/uninstaller";
                    }
                }
                string name = e.DisplayName.Trim();
                if (!seen.Add(name + "|" + (folder ?? ""))) continue;
                bool unused = folder == null && (reason == NotRecorded || reason == NoLongerExists);
                if (unused)
                {
                    reason = (reason == NoLongerExists ? "Leftover entry: install folder is gone" : "No folder of its own (files may be in Windows folders)")
                        + " - possibly unused/unwanted";
                }
                apps.Add(new AppInfo { Name = name, Publisher = e.Publisher ?? "", InstallFolder = folder, Note = reason, PossiblyUnused = unused });
            }
            return apps;
        }

        // ---------- Store apps ----------

        public static string StoreAppsRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"); }
        }

        /// <summary>
        /// Package folder names look like Name_Version_Arch_ResourceId_PublisherId.
        /// Returns the family key (Name_PublisherId) and package name, or null for folders that aren't packages
        /// or are Windows / framework packages.
        /// </summary>
        public static string StoreFamily(string folderName, out string packageName)
        {
            packageName = null;
            string[] parts = folderName.Split('_');
            if (parts.Length != 5 || parts[0].Length == 0 || parts[4].Length == 0) return null;
            foreach (string prefix in FrameworkPrefixes)
                if (parts[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            packageName = parts[0];
            return parts[0] + "_" + parts[4];
        }

        /// <summary>Lists Store apps grouped by family, each with all its package folders. Needs Administrator.</summary>
        public static List<AppInfo> StoreApps(out Dictionary<AppInfo, List<string>> folders)
        {
            folders = new Dictionary<AppInfo, List<string>>();
            var apps = new List<AppInfo>();
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(StoreAppsRoot);
            }
            catch (Exception)
            {
                apps.Add(new AppInfo
                {
                    Name = "Microsoft Store apps", Publisher = "", InstallFolder = StoreAppsRoot, IsStoreApp = true,
                    Problem = true, Note = "Run as Administrator to measure Store apps"
                });
                return apps;
            }

            var byFamily = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                string packageName;
                string family = StoreFamily(Path.GetFileName(dir), out packageName);
                if (family == null) continue;
                AppInfo app;
                if (!byFamily.TryGetValue(family, out app))
                {
                    int dot = packageName.IndexOf('.');
                    app = new AppInfo
                    {
                        Name = packageName, Publisher = dot > 0 ? packageName.Substring(0, dot) : "",
                        InstallFolder = dir, IsStoreApp = true
                    };
                    byFamily.Add(family, app);
                    apps.Add(app);
                    folders[app] = new List<string>();
                }
                folders[app].Add(dir);
            }
            return apps;
        }

        // ---------- Measuring ----------

        /// <summary>Sums size on disk of the given folders into the app; notes unreadable folders.</summary>
        public static void Measure(AppInfo app, IEnumerable<string> appFolders, CancellationToken token)
        {
            long total = 0;
            int unreadable = 0;
            foreach (string folder in appFolders)
            {
                ScanResult r = Scanner.Scan(folder, token, null);
                total += r.TotalSizeOnDisk;
                unreadable += r.Unreadable.Count;
                if (r.Cancelled) return;
            }
            app.SizeOnDisk = total;
            if (unreadable > 0)
            {
                app.Problem = true;
                app.Note = unreadable + " unreadable folder(s); size may be low";
            }
        }
    }
}
