using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DiskBrush
{
    sealed class MainForm : Form
    {
        static readonly string[] FileHeaders = { "Name", "Size on disk", "Size", "Folder", "Full path" };

        readonly ComboBox driveBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly Button scanButton = new Button { Text = "Scan", AutoSize = true };
        readonly Button scanFolderButton = new Button { Text = "Scan a folder...", AutoSize = true };
        readonly Button cancelButton = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        readonly TextBox filterBox = new TextBox { Width = 200 };
        readonly Button exportButton = new Button { Text = "Export CSV...", AutoSize = true, Enabled = false };
        readonly Button adminButton = new Button { Text = "Restart as Administrator", AutoSize = true };
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly TabPage filesTab = new TabPage("Files");
        readonly TabPage foldersTab = new TabPage("Folders");
        readonly TabPage unreadableTab = new TabPage("Unreadable folders");
        readonly ListView filesList = new ListView();
        readonly ListView foldersList = new ListView();
        readonly ListView unreadableList = new ListView();
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly System.Windows.Forms.Timer filterDelay = new System.Windows.Forms.Timer { Interval = 300 };

        readonly AppsPanel appsPanel = new AppsPanel();
        static readonly string[] FolderHeaders = { "Folder", "Size on disk", "Size", "Files", "Full path" };
        readonly FileTable table = new FileTable();
        readonly FolderTable folderTable = new FolderTable();
        ScanResult lastResult;
        CancellationTokenSource scanCancel;
        readonly Stopwatch scanClock = new Stopwatch();

        public MainForm()
        {
            Text = "DiskBrush" + (IsAdministrator() ? " (Administrator)" : "");
            Size = new Size(1200, 750);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); // the icon built into the exe

            BuildLayout();
            LoadDrives();

            scanButton.Click += delegate
            {
                var drive = driveBox.SelectedItem as DriveItem;
                if (drive != null) StartScan(drive.Root);
            };
            scanFolderButton.Click += delegate { PickAndScanFolder(); };
            cancelButton.Click += delegate { if (scanCancel != null) scanCancel.Cancel(); };
            exportButton.Click += delegate { ExportCsv(); };
            adminButton.Click += delegate { RestartAsAdministrator(); };
            adminButton.Visible = !IsAdministrator();
            filterBox.TextChanged += delegate { filterDelay.Stop(); filterDelay.Start(); };
            filterDelay.Tick += delegate
            {
                filterDelay.Stop();
                table.SetFilter(filterBox.Text);
                folderTable.SetFilter(filterBox.Text);
                RefreshFiles();
                RefreshFolders();
            };
            foldersList.RetrieveVirtualItem += OnRetrieveFolderItem;
            foldersList.ColumnClick += (s, e) => { folderTable.SortBy((FolderColumn)e.Column); RefreshFolders(); };
            foldersList.DoubleClick += delegate
            {
                if (foldersList.SelectedIndices.Count > 0) ShowInExplorer(folderTable[foldersList.SelectedIndices[0]].Path);
            };
            filesList.RetrieveVirtualItem += OnRetrieveFileItem;
            filesList.ColumnClick += (s, e) => { table.SortBy((FileColumn)e.Column); RefreshFiles(); };
            filesList.DoubleClick += delegate { OpenSelectedInExplorer(); };
            unreadableList.DoubleClick += delegate
            {
                if (unreadableList.SelectedItems.Count > 0) ShowInExplorer(((UnreadableFolder)unreadableList.SelectedItems[0].Tag).Path);
            };
            FormClosing += delegate { if (scanCancel != null) scanCancel.Cancel(); appsPanel.CancelWork(); };
        }

        void BuildLayout()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            top.Controls.Add(new Label { Text = "Drive:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            top.Controls.Add(driveBox);
            top.Controls.Add(scanButton);
            top.Controls.Add(scanFolderButton);
            top.Controls.Add(cancelButton);
            top.Controls.Add(new Label { Text = "Filter by name:", AutoSize = true, Margin = new Padding(15, 8, 3, 3) });
            top.Controls.Add(filterBox);
            top.Controls.Add(exportButton);
            top.Controls.Add(adminButton);

            SetupList(filesList, true);
            foreach (string h in FileHeaders) filesList.Columns.Add(h);
            filesList.Columns[0].Width = 260;
            filesList.Columns[1].Width = 100;
            filesList.Columns[2].Width = 100;
            filesList.Columns[3].Width = 330;
            filesList.Columns[4].Width = 380;
            filesList.Columns[1].TextAlign = HorizontalAlignment.Right;
            filesList.Columns[2].TextAlign = HorizontalAlignment.Right;
            filesTab.Controls.Add(filesList);

            SetupList(foldersList, true);
            foreach (string h in FolderHeaders) foldersList.Columns.Add(h);
            foldersList.Columns[0].Width = 260;
            foldersList.Columns[1].Width = 100;
            foldersList.Columns[2].Width = 100;
            foldersList.Columns[3].Width = 80;
            foldersList.Columns[4].Width = 600;
            foldersList.Columns[1].TextAlign = HorizontalAlignment.Right;
            foldersList.Columns[2].TextAlign = HorizontalAlignment.Right;
            foldersList.Columns[3].TextAlign = HorizontalAlignment.Right;
            foldersTab.Controls.Add(foldersList);

            SetupList(unreadableList, false);
            unreadableList.Columns.Add("", 30);
            unreadableList.Columns.Add("Folder", 650);
            unreadableList.Columns.Add("Reason", 400);
            unreadableTab.Controls.Add(unreadableList);

            tabs.TabPages.Add(filesTab);
            tabs.TabPages.Add(foldersTab);
            tabs.TabPages.Add(unreadableTab);
            var appsTab = new TabPage("Apps");
            appsTab.Controls.Add(appsPanel);
            tabs.TabPages.Add(appsTab);

            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(status);
            status.Text = "Pick a drive and press Scan. Windows system files are never shown.";

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(statusStrip);
        }

        static void SetupList(ListView list, bool virtualMode)
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.VirtualMode = virtualMode;
        }

        void LoadDrives()
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                driveBox.Items.Add(new DriveItem(d));
            }
            if (driveBox.Items.Count > 0) driveBox.SelectedIndex = 0;
            else status.Text = "No local drives found. Network and mapped drives are not offered.";
        }

        void PickAndScanFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose a folder to scan", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (Exclusions.IsInsideExcludedFolder(dialog.SelectedPath))
                {
                    MessageBox.Show(this, "That folder holds Windows system files, which this app never shows.",
                        "DiskBrush", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                StartScan(dialog.SelectedPath);
            }
        }

        void StartScan(string root)
        {
            SetScanning(true);
            table.Load(new List<FileEntry>());
            folderTable.Load(new List<FolderEntry>());
            RefreshFiles();
            RefreshFolders();
            unreadableList.Items.Clear();
            unreadableTab.Text = "Unreadable folders";

            scanCancel = new CancellationTokenSource();
            CancellationToken token = scanCancel.Token;
            scanClock.Restart();
            status.Text = "Scanning " + root + " ...";

            var worker = new Thread(() =>
            {
                ScanResult result = Scanner.Scan(root, token, count =>
                {
                    if (IsHandleCreated) BeginInvoke((Action)(() =>
                        status.Text = "Scanning " + root + " ... " + count.ToString("N0") + " files found"));
                });
                if (IsHandleCreated) BeginInvoke((Action)(() => status.Text = "Adding up folder sizes..."));
                List<FolderEntry> folders = FolderTable.Totals(result.Files, root);
                if (IsHandleCreated) BeginInvoke((Action)(() => ShowResult(result, folders)));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void ShowResult(ScanResult result, List<FolderEntry> folders)
        {
            scanClock.Stop();
            lastResult = result;
            status.Text = "Sorting " + result.Files.Count.ToString("N0") + " files...";
            Refresh();

            table.Load(result.Files);
            RefreshFiles();
            folderTable.Load(folders);
            RefreshFolders();

            unreadableList.BeginUpdate();
            var bold = new Font(unreadableList.Font, FontStyle.Bold);
            foreach (UnreadableFolder u in result.Unreadable)
            {
                unreadableList.Items.Add(new ListViewItem(new[] { "!", u.Path, u.Reason })
                {
                    ForeColor = Color.Red, Font = bold, Tag = u, UseItemStyleForSubItems = true
                });
            }
            unreadableList.EndUpdate();
            unreadableTab.Text = "! Unreadable folders (" + result.Unreadable.Count.ToString("N0") + ")";

            status.Text = string.Format("{0}{1:N0} files, {2} on disk. {3:N0} unreadable folders. Took {4:0.0} s.{5}",
                result.Cancelled ? "Cancelled - partial results: " : "",
                result.Files.Count, PathUtil.FormatSize(result.TotalSizeOnDisk), result.Unreadable.Count,
                scanClock.Elapsed.TotalSeconds,
                result.Unreadable.Count > 0 && !IsAdministrator() ? " Restart as Administrator to read more folders." : "");
            SetScanning(false);
        }

        void SetScanning(bool scanning)
        {
            scanButton.Enabled = !scanning;
            scanFolderButton.Enabled = !scanning;
            driveBox.Enabled = !scanning;
            cancelButton.Enabled = scanning;
            exportButton.Enabled = !scanning && lastResult != null;
        }

        void RefreshFiles()
        {
            filesList.VirtualListSize = table.Count;
            filesList.Invalidate();
            filesTab.Text = "Files (" + table.Count.ToString("N0") + ")";
            for (int i = 0; i < FileHeaders.Length; i++)
            {
                string arrow = i == (int)table.SortColumn ? (table.Descending ? " ▼" : " ▲") : "";
                filesList.Columns[i].Text = FileHeaders[i] + arrow;
            }
        }

        void RefreshFolders()
        {
            foldersList.VirtualListSize = folderTable.Count;
            foldersList.Invalidate();
            foldersTab.Text = "Folders (" + folderTable.Count.ToString("N0") + ")";
            for (int i = 0; i < FolderHeaders.Length; i++)
            {
                string arrow = i == (int)folderTable.SortColumn ? (folderTable.Descending ? " ▼" : " ▲") : "";
                foldersList.Columns[i].Text = FolderHeaders[i] + arrow;
            }
        }

        void OnRetrieveFolderItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            FolderEntry f = folderTable[e.ItemIndex];
            e.Item = new ListViewItem(new[]
            {
                f.Name, PathUtil.FormatSize(f.SizeOnDisk), PathUtil.FormatSize(f.Size), f.FileCount.ToString("N0"), f.Path
            });
        }

        void OnRetrieveFileItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            FileEntry f = table[e.ItemIndex];
            e.Item = new ListViewItem(new[]
            {
                f.Name, PathUtil.FormatSize(f.SizeOnDisk), PathUtil.FormatSize(f.Size), f.Folder, f.FullPath
            });
        }

        void OpenSelectedInExplorer()
        {
            if (filesList.SelectedIndices.Count == 0) return;
            FileEntry f = table[filesList.SelectedIndices[0]];
            Process.Start("explorer.exe", "/select,\"" + f.FullPath + "\"");
        }

        static void ShowInExplorer(string folder)
        {
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        void ExportCsv()
        {
            bool unreadable = tabs.SelectedTab == unreadableTab;
            bool folders = tabs.SelectedTab == foldersTab;
            string fileName = unreadable ? "unreadable-folders.csv" : folders ? "folders.csv" : "files.csv";
            using (var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = fileName })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (folders)
                        Csv.Write(dialog.FileName, new[] { "Folder", "Size on disk (bytes)", "Size (bytes)", "Files", "Full path" },
                            folderTable.Rows.Select(f => new[] { f.Name, f.SizeOnDisk.ToString(), f.Size.ToString(), f.FileCount.ToString(), f.Path }));
                    else if (unreadable)
                        Csv.Write(dialog.FileName, new[] { "Folder", "Reason" },
                            lastResult.Unreadable.Select(u => new[] { u.Path, u.Reason }));
                    else
                        Csv.Write(dialog.FileName, new[] { "Name", "Size on disk (bytes)", "Size (bytes)", "Folder", "Full path" },
                            table.Rows.Select(f => new[] { f.Name, f.SizeOnDisk.ToString(), f.Size.ToString(), f.Folder, f.FullPath }));
                    status.Text = "Exported to " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export failed: " + ex.Message, "DiskBrush", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void RestartAsAdministrator()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Win32Exception)
            {
                // The user declined the Windows prompt; keep running normally.
            }
        }

        static bool IsAdministrator()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        sealed class DriveItem
        {
            public readonly string Root;
            readonly string label;

            public DriveItem(DriveInfo d)
            {
                Root = d.RootDirectory.FullName;
                string name = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "Removable" : "Local Disk") : d.VolumeLabel;
                label = string.Format("{0} {1} - {2} used of {3}", Root, name,
                    PathUtil.FormatSize(d.TotalSize - d.TotalFreeSpace), PathUtil.FormatSize(d.TotalSize));
            }

            public override string ToString() { return label; }
        }
    }
}
