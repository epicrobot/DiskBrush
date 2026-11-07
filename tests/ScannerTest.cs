using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace DiskBrush.Tests
{
    /// <summary>Builds a known folder tree in %TEMP%, scans it and checks the results. Exit code 0 = all passed.</summary>
    static class ScannerTest
    {
        static int failures;

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "apps") return AppsReport();
            if (args.Length > 1 && args[0] == "scan") return ScanReport(args[1]);
            if (args.Length > 0 && args[0] == "memory") return MemoryCheck();

            CheckExclusionRules();
            CheckScanOfTestTree();
            CheckFileTable();
            CheckFolderTable();
            CheckAppCatalog();

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool condition, string what)
        {
            Console.WriteLine((condition ? "PASS  " : "FAIL  ") + what);
            if (!condition) failures++;
        }

        static void CheckExclusionRules()
        {
            Check(Exclusions.IsExcludedFolder(@"C:\Windows"), "C:\\Windows excluded");
            Check(Exclusions.IsExcludedFolder(@"D:\Program Files (x86)"), "Program Files (x86) excluded on any drive");
            Check(!Exclusions.IsExcludedFolder(@"C:\Users\x\Windows"), "nested folder named Windows not excluded");
            Check(Exclusions.IsExcludedFile(@"C:\pagefile.sys", FileAttributes.Normal), "pagefile.sys excluded");
            Check(!Exclusions.IsExcludedFile(@"C:\data\pagefile.sys", FileAttributes.Normal), "nested pagefile.sys not excluded");
            Check(Exclusions.IsExcludedFile(@"C:\data\x.bin", FileAttributes.System), "System attribute file excluded");
        }

        static void CheckScanOfTestTree()
        {
            string root = Path.Combine(Path.GetTempPath(), "DiskBrushTest_" + Guid.NewGuid().ToString("N"));
            string denied = Path.Combine(root, "denied");
            try
            {
                Directory.CreateDirectory(root);
                WriteFile(Path.Combine(root, "big.bin"), 5000000);
                WriteFile(Path.Combine(root, "small.bin"), 10000);
                WriteFile(Path.Combine(root, "tiny.bin"), 1);
                Directory.CreateDirectory(Path.Combine(root, "sub"));
                WriteFile(Path.Combine(root, "sub", "mid.bin"), 70000);

                string sys = Path.Combine(root, "system.bin");
                WriteFile(sys, 1234);
                File.SetAttributes(sys, FileAttributes.System);

                // Long path (> 300 chars).
                string longDir = root;
                for (int i = 0; i < 7; i++) longDir = Path.Combine(longDir, new string((char)('a' + i), 50));
                Directory.CreateDirectory(PathUtil.ToLong(longDir));
                string longFile = Path.Combine(longDir, "deep.bin");
                WriteFile(PathUtil.ToLong(longFile), 2048);

                // Junction loop: sub\loop -> root.
                Run("cmd", "/c mklink /J \"" + Path.Combine(root, "sub", "loop") + "\" \"" + root + "\"");

                // Compressible file, compressed with NTFS compression.
                bool ntfs = new DriveInfo(root.Substring(0, 3)).DriveFormat == "NTFS";
                string zeros = Path.Combine(root, "zeros.bin");
                WriteFile(zeros, 1048576, 0);
                if (ntfs) Run("compact", "/c /q \"" + zeros + "\"");

                // Folder the current user may not list.
                Directory.CreateDirectory(denied);
                WriteFile(Path.Combine(denied, "secret.bin"), 100);
                string sid = WindowsIdentity.GetCurrent().User.Value;
                Run("icacls", "\"" + denied + "\" /deny *" + sid + ":(OI)(CI)(RX)");

                var stopwatch = Stopwatch.StartNew();
                ScanResult result = Scanner.Scan(root, CancellationToken.None, null);
                stopwatch.Stop();
                long cluster = SizeOnDisk.ClusterSize(root);

                Func<string, FileEntry> find = name => result.Files.FirstOrDefault(f => f.Name == name);
                Check(stopwatch.Elapsed.TotalSeconds < 30, "scan finished (no infinite loop through junction)");
                Check(result.Files.Count(f => f.Name == "mid.bin") == 1, "file behind junction counted once");
                Check(find("big.bin") != null && find("big.bin").Size == 5000000, "big.bin logical size correct");
                Check(find("big.bin") != null && find("big.bin").SizeOnDisk == SizeOnDisk.RoundUp(5000000, cluster), "big.bin size on disk = rounded to cluster");
                Check(find("tiny.bin") != null && find("tiny.bin").SizeOnDisk % cluster == 0, "tiny.bin size on disk is whole clusters");
                Check(find("system.bin") == null, "System attribute file not listed");
                FileEntry deep = find("deep.bin");
                Check(deep != null && deep.FullPath.Length > 300 && deep.Size == 2048, "file in >300-char path listed");
                Check(find("secret.bin") == null, "file inside denied folder not listed");
                Check(result.Unreadable.Any(u => u.Path.Equals(denied, StringComparison.OrdinalIgnoreCase) && u.Reason == "Access denied"),
                    "denied folder reported as unreadable with reason");
                if (ntfs)
                    Check(find("zeros.bin") != null && find("zeros.bin").SizeOnDisk < 1048576, "compressed file size on disk < logical size");
                else
                    Console.WriteLine("SKIP  compression check (volume is not NTFS)");
                Check(!result.Cancelled, "scan not marked cancelled");

                var cts = new CancellationTokenSource();
                cts.Cancel();
                Check(Scanner.Scan(root, cts.Token, null).Cancelled, "cancelled scan reports Cancelled");
            }
            finally
            {
                if (Directory.Exists(denied)) Run("icacls", "\"" + denied + "\" /reset /t /q");
                // Remove the junction itself first (rmdir deletes the link, not its target).
                Run("cmd", "/c rmdir \"" + Path.Combine(root, "sub", "loop") + "\"");
                try { Directory.Delete(PathUtil.ToLong(root), true); }
                catch (Exception ex) { Console.WriteLine("WARN  could not delete test tree " + root + ": " + ex.Message); }
            }
        }

        static void CheckFolderTable()
        {
            var files = new System.Collections.Generic.List<FileEntry>
            {
                new FileEntry("a", @"C:\Users\me\Games\Big", 1000, 4096),
                new FileEntry("b", @"C:\Users\me\Games\Big", 2000, 4096),
                new FileEntry("c", @"C:\Users\me\Games", 10, 4096),
                new FileEntry("d", @"C:\Users\me\Docs", 10, 4096),
                new FileEntry("e", @"C:\", 5, 4096),
            };
            var totals = FolderTable.Totals(files, @"C:\");
            Func<string, FolderEntry> at = p => totals.FirstOrDefault(t => t.Path == p);
            Check(at(@"C:\Users\me\Games\Big").SizeOnDisk == 8192 && at(@"C:\Users\me\Games\Big").FileCount == 2, "folder's own files counted");
            Check(at(@"C:\Users\me\Games").SizeOnDisk == 12288 && at(@"C:\Users\me\Games").Size == 3010, "parent includes subfolder totals");
            Check(at(@"C:\Users").SizeOnDisk == 16384 && at(@"C:\Users").FileCount == 4, "rolls up to top-level folder");
            Check(at(@"C:\") != null && at(@"C:\").FileCount == 5, "drive root holds the grand total");
            Check(at(@"C:\Users\me\Games").Name == "Games" && at(@"C:\").Name == @"C:\", "folder names shown correctly");

            var sub = FolderTable.Totals(files.Where(f => f.Folder.StartsWith(@"C:\Users\me\Games")), @"C:\Users\me\Games\");
            Check(sub.Count == 2 && at(@"C:\Users") != null && !sub.Any(t => t.Path == @"C:\Users\me"), "folder scan stops at the chosen folder");

            var table = new FolderTable();
            table.Load(totals);
            bool largestFirst = table[0].Path == @"C:\";
            for (int i = 1; i < table.Count; i++) largestFirst &= table[i - 1].SizeOnDisk >= table[i].SizeOnDisk;
            Check(largestFirst, "folders listed largest first");
            table.SetFilter("big");
            Check(table.Count == 1 && table[0].Name == "Big", "folder name filter works");

            Check(Exclusions.IsInsideExcludedFolder(@"C:\Windows\System32"), "folder inside Windows is blocked");
            Check(!Exclusions.IsInsideExcludedFolder(@"C:\Users\me"), "user folder is allowed");
            Check(!Exclusions.IsInsideExcludedFolder(@"D:\"), "drive root is allowed");
        }

        static void CheckFileTable()
        {
            var files = new System.Collections.Generic.List<FileEntry>
            {
                new FileEntry("b.txt", @"C:\x", 100, 4096),
                new FileEntry("a.iso", @"C:\y", 9000000, 9003008),
                new FileEntry("c.log", @"C:\a", 5000, 8192),
            };
            var table = new FileTable();
            table.Load(files);
            Check(table.Count == 3 && table[0].Name == "a.iso" && table[2].Name == "b.txt", "default view is largest size on disk first");
            bool descending = true;
            for (int i = 1; i < table.Count; i++) descending &= table[i - 1].SizeOnDisk >= table[i].SizeOnDisk;
            Check(descending, "view is in descending size-on-disk order");
            table.SortBy(FileColumn.SizeOnDisk);
            Check(table[0].Name == "b.txt", "clicking size column again sorts smallest first");
            table.SortBy(FileColumn.Folder);
            Check(table[0].Folder == @"C:\a", "folder column sorts A-Z");
            table.SetFilter("ISO");
            Check(table.Count == 1 && table[0].Name == "a.iso", "name filter is case-insensitive");
            table.SetFilter("");
            Check(table.Count == 3, "clearing filter restores all rows");
        }

        /// <summary>Prints installed apps largest first (tab-separated), for comparing against Windows.</summary>
        static int AppsReport()
        {
            var apps = AppCatalog.DesktopApps(AppCatalog.ReadRegistry());
            System.Collections.Generic.Dictionary<AppInfo, System.Collections.Generic.List<string>> store;
            apps.AddRange(AppCatalog.StoreApps(out store));
            foreach (AppInfo a in apps)
            {
                if (a.InstallFolder == null || a.Problem) continue;
                System.Collections.Generic.List<string> folders;
                AppCatalog.Measure(a, store.TryGetValue(a, out folders) ? folders : new System.Collections.Generic.List<string> { a.InstallFolder }, CancellationToken.None);
            }
            foreach (AppInfo a in apps.OrderByDescending(x => x.SizeOnDisk))
                Console.WriteLine(a.SizeOnDisk + "\t" + a.Name + "\t" + (a.InstallFolder ?? "") + "\t" + (a.Note ?? ""));
            return 0;
        }

        /// <summary>Prints file count, total size and total size on disk for a folder.</summary>
        static int ScanReport(string folder)
        {
            var sw = Stopwatch.StartNew();
            ScanResult r = Scanner.Scan(folder, CancellationToken.None, null);
            Console.WriteLine("files=" + r.Files.Count + " size=" + r.Files.Sum(f => f.Size) + " ondisk=" + r.TotalSizeOnDisk
                + " unreadable=" + r.Unreadable.Count + " seconds=" + sw.Elapsed.TotalSeconds.ToString("0.0"));
            var folders = new FolderTable();
            folders.Load(FolderTable.Totals(r.Files, folder));
            for (int i = 0; i < Math.Min(6, folders.Count); i++)
                Console.WriteLine("  " + folders[i].SizeOnDisk + "\t" + folders[i].FileCount + "\t" + folders[i].Path);
            return 0;
        }

        /// <summary>1,000,000 synthetic entries with realistic paths, sorted and filtered, must stay under 1 GB.</summary>
        static int MemoryCheck()
        {
            const int count = 1000000;
            var rng = new Random(1);
            var files = new System.Collections.Generic.List<FileEntry>(count);
            string folder = null;
            for (int i = 0; i < count; i++)
            {
                if (i % 40 == 0)
                    folder = @"C:\Users\someone\Documents\Projects\project-" + (i / 40) + @"\src\components\subfolder-" + rng.Next(1000);
                files.Add(new FileEntry("file-name-" + i + "-" + rng.Next() + ".dat", folder, rng.Next(), rng.Next()));
            }
            var sw = Stopwatch.StartNew();
            var table = new FileTable();
            table.Load(files);
            table.SortBy(FileColumn.Folder);
            table.SetFilter("99");
            sw.Stop();
            long managed = GC.GetTotalMemory(true);
            long privateBytes = Process.GetCurrentProcess().PrivateMemorySize64;
            Console.WriteLine("entries=" + count + " managed=" + PathUtil.FormatSize(managed) + " private=" + PathUtil.FormatSize(privateBytes)
                + " sort+filter seconds=" + sw.Elapsed.TotalSeconds.ToString("0.0") + " filtered=" + table.Count);
            bool ok = privateBytes < 1024L * 1024 * 1024;
            Console.WriteLine(ok ? "PASS  1M entries under 1 GB" : "FAIL  1M entries over 1 GB");
            GC.KeepAlive(files);
            return ok ? 0 : 1;
        }

        static void CheckAppCatalog()
        {
            string temp = Path.GetTempPath().TrimEnd('\\');
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var entries = new[]
            {
                new UninstallEntry { KeyName = "App1", DisplayName = "Real App", InstallLocation = "\"" + temp + "\\\"" },
                new UninstallEntry { KeyName = "App1dup", DisplayName = "Real App", InstallLocation = temp },
                new UninstallEntry { KeyName = "Comp", DisplayName = "Hidden Component", SystemComponent = 1 },
                new UninstallEntry { KeyName = "KB5034441", DisplayName = "Security Update for Windows" },
                new UninstallEntry { KeyName = "Upd", DisplayName = "Some Update", ReleaseType = "Update" },
                new UninstallEntry { KeyName = "Child", DisplayName = "Child Patch", ParentKeyName = "App1" },
                new UninstallEntry { KeyName = "Drv", DisplayName = "Windows Driver Package - Intel (x) System" },
                new UninstallEntry { KeyName = "NoName" },
                new UninstallEntry { KeyName = "NoFolder", DisplayName = "No Folder App" },
                new UninstallEntry { KeyName = "WinApp", DisplayName = "Inside Windows", InstallLocation = windows + "\\System32" },
                new UninstallEntry { KeyName = "PF", DisplayName = "Bad Entry", InstallLocation = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            };
            var apps = AppCatalog.DesktopApps(entries);
            Func<string, AppInfo> app = n => apps.FirstOrDefault(a => a.Name == n);
            Check(apps.Count(a => a.Name == "Real App") == 1, "duplicate app entries merged");
            Check(app("Real App") != null && app("Real App").InstallFolder == temp, "install folder cleaned of quotes and trailing slash");
            Check(AppCatalog.ExecutablePath("\"C:\\A B\\app.exe\",0") == @"C:\A B\app.exe", "icon path with quotes and index parsed");
            Check(AppCatalog.ExecutablePath("C:\\A B\\unins000.exe /SILENT") == @"C:\A B\unins000.exe", "uninstaller path with arguments parsed");
            Check(AppCatalog.ExecutablePath("MsiExec.exe /X{1234}") == null, "MsiExec uninstaller gives no folder");
            Check(AppCatalog.GuessFolder(new UninstallEntry { DisplayIcon = temp + "\\bin\\x.exe" }) == temp, "guess climbs out of a bin folder");
            Check(AppCatalog.GuessFolder(new UninstallEntry { UninstallString = windows + "\\Installer\\x.msi" }) == null, "guess never uses Windows Installer folder");
            var guessed = AppCatalog.DesktopApps(new[] { new UninstallEntry { KeyName = "G", DisplayName = "Guessed App", DisplayIcon = "\"" + temp + "\\g.exe\",0" } });
            Check(guessed.Count == 1 && guessed[0].InstallFolder == temp && guessed[0].Note.Contains("guessed"), "app without InstallLocation gets a labelled guessed folder");
            string reason;
            Check(AppCatalog.UsableFolder(temp.Replace('\\', '/') + "/", out reason) == temp, "forward-slash install folder normalized");
            Check(app("Hidden Component") == null, "system component hidden");
            Check(app("Security Update for Windows") == null && app("Some Update") == null && app("Child Patch") == null, "updates hidden");
            Check(app("Windows Driver Package - Intel (x) System") == null, "driver package hidden");
            Check(app("No Folder App") != null && app("No Folder App").InstallFolder == null, "app without folder listed as unknown");
            Check(app("No Folder App") != null && app("No Folder App").PossiblyUnused && app("No Folder App").Note.Contains("possibly unused"), "app without folder marked possibly unused");
            Check(app("Real App") != null && !app("Real App").PossiblyUnused, "app with folder not marked unused");
            Check(app("Bad Entry") != null && !app("Bad Entry").PossiblyUnused, "shared-folder app not marked unused");
            Check(app("Inside Windows") != null && app("Inside Windows").InstallFolder == null, "folder inside Windows never measured");
            Check(app("Bad Entry") != null && app("Bad Entry").InstallFolder == null, "shared Program Files folder never measured as one app");

            string pkg;
            Check(AppCatalog.StoreFamily("SpotifyAB.SpotifyMusic_1.2.3.0_x64__zpdnekdrzrea0", out pkg) == "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Store package family parsed");
            Check(AppCatalog.StoreFamily("SpotifyAB.SpotifyMusic_1.2.3.0_neutral_split.scale-100_zpdnekdrzrea0", out pkg) == "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "resource package joins its app's family");
            Check(AppCatalog.StoreFamily("Microsoft.VCLibs.140.00_14.0.33519.0_x64__8wekyb3d8bbwe", out pkg) == null, "framework package hidden");
            Check(AppCatalog.StoreFamily("Microsoft.WindowsCalculator_11.0.0.0_x64__8wekyb3d8bbwe", out pkg) == null, "Windows package hidden");
            Check(AppCatalog.StoreFamily("Deleted", out pkg) == null, "non-package folder ignored");

            var measured = new AppInfo { Name = "t" };
            string dir = Path.Combine(Path.GetTempPath(), "DiskBrushApp_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                WriteFile(Path.Combine(dir, "app.exe"), 300000);
                AppCatalog.Measure(measured, new[] { dir }, CancellationToken.None);
                Check(measured.SizeOnDisk == SizeOnDisk.RoundUp(300000, SizeOnDisk.ClusterSize(dir)), "app folder measured");
            }
            finally { Directory.Delete(dir, true); }
        }

        static void WriteFile(string path, int length, byte fill = 0x5A)
        {
            var data = new byte[length];
            if (fill != 0) for (int i = 0; i < length; i++) data[i] = (byte)(fill + i);
            File.WriteAllBytes(path, data);
        }

        static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) Console.WriteLine("WARN  '" + exe + " " + args + "' exited " + p.ExitCode);
            }
        }
    }
}
