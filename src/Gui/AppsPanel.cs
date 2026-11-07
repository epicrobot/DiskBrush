using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DiskBrush
{
    /// <summary>The Apps tab: installed desktop and Store apps, largest first. Windows components are hidden.</summary>
    sealed class AppsPanel : UserControl
    {
        readonly Button measureButton = new Button { Text = "Measure installed apps", AutoSize = true };
        readonly Button cancelButton = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        readonly Label progress = new Label { AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
        readonly ListView list = new ListView();
        List<AppInfo> apps = new List<AppInfo>();
        CancellationTokenSource cancel;
        int sortColumn = 3;
        bool descending = true;

        public AppsPanel()
        {
            Dock = DockStyle.Fill;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            top.Controls.Add(measureButton);
            top.Controls.Add(cancelButton);
            top.Controls.Add(progress);
            progress.Text = "Lists installed programs (and Store apps when running as Administrator). Windows components are hidden.";

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.Columns.Add("", 30);
            list.Columns.Add("Name", 300);
            list.Columns.Add("Publisher", 180);
            list.Columns.Add("Size on disk", 110, HorizontalAlignment.Right);
            list.Columns.Add("Install folder", 380);
            list.Columns.Add("Note", 260);

            Controls.Add(list);
            Controls.Add(top);

            measureButton.Click += delegate { StartMeasuring(); };
            cancelButton.Click += delegate { if (cancel != null) cancel.Cancel(); };
            list.ColumnClick += (s, e) =>
            {
                if (e.Column == sortColumn) descending = !descending;
                else { sortColumn = e.Column; descending = e.Column == 3; }
                ShowApps();
            };
            list.DoubleClick += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                var app = (AppInfo)list.SelectedItems[0].Tag;
                if (app.InstallFolder != null) Process.Start("explorer.exe", "\"" + app.InstallFolder + "\"");
            };
        }

        public void CancelWork()
        {
            if (cancel != null) cancel.Cancel();
        }

        void StartMeasuring()
        {
            measureButton.Enabled = false;
            cancelButton.Enabled = true;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            progress.Text = "Reading installed programs...";

            var worker = new Thread(() =>
            {
                List<AppInfo> found = AppCatalog.DesktopApps(AppCatalog.ReadRegistry());
                Dictionary<AppInfo, List<string>> storeFolders;
                found.AddRange(AppCatalog.StoreApps(out storeFolders));
                Post(() => { apps = found; ShowApps(); });

                int done = 0;
                foreach (AppInfo app in found)
                {
                    if (token.IsCancellationRequested) break;
                    done++;
                    if (app.InstallFolder == null || app.Problem) continue;
                    string text = "Measuring " + done + " of " + found.Count + ": " + app.Name;
                    Post(() => progress.Text = text);
                    List<string> folders;
                    if (!storeFolders.TryGetValue(app, out folders)) folders = new List<string> { app.InstallFolder };
                    AppCatalog.Measure(app, folders, token);
                }
                Post(() =>
                {
                    ShowApps();
                    long total = apps.Where(a => a.SizeOnDisk > 0).Sum(a => a.SizeOnDisk);
                    progress.Text = (token.IsCancellationRequested ? "Cancelled. " : "") + apps.Count + " apps, "
                        + PathUtil.FormatSize(total) + " measured. Double-click an app to open its folder.";
                    measureButton.Enabled = true;
                    cancelButton.Enabled = false;
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Post(Action action)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
        }

        void ShowApps()
        {
            IEnumerable<AppInfo> sorted;
            Func<AppInfo, string> text = a => sortColumn == 1 ? a.Name : sortColumn == 2 ? a.Publisher
                : sortColumn == 4 ? (a.InstallFolder ?? "") : sortColumn == 5 ? (a.Note ?? "") : a.Name;
            if (sortColumn == 3 || sortColumn == 0)
                sorted = descending ? apps.OrderByDescending(a => a.SizeOnDisk) : apps.OrderBy(a => a.SizeOnDisk);
            else
                sorted = descending ? apps.OrderByDescending(text, StringComparer.OrdinalIgnoreCase)
                                    : apps.OrderBy(text, StringComparer.OrdinalIgnoreCase);

            var bold = new Font(list.Font, FontStyle.Bold);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (AppInfo a in sorted)
            {
                var item = new ListViewItem(new[]
                {
                    a.Problem ? "!" : a.PossiblyUnused ? "?" : "", a.Name, a.Publisher,
                    a.SizeOnDisk >= 0 ? PathUtil.FormatSize(a.SizeOnDisk) : "unknown",
                    a.InstallFolder ?? "", a.Note ?? ""
                }) { Tag = a };
                if (a.Problem) { item.ForeColor = Color.Red; item.Font = bold; }
                else if (a.PossiblyUnused) { item.ForeColor = Color.DarkOrange; item.Font = bold; }
                list.Items.Add(item);
            }
            list.EndUpdate();
        }
    }
}
