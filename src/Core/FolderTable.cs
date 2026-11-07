using System;
using System.Collections.Generic;

namespace DiskBrush
{
    /// <summary>One folder with totals that include everything in its subfolders.</summary>
    public sealed class FolderEntry
    {
        public readonly string Path;
        public long Size, SizeOnDisk, FileCount;

        public FolderEntry(string path) { Path = path; }

        public string Name
        {
            get
            {
                string trimmed = Path.TrimEnd('\\');
                int slash = trimmed.LastIndexOf('\\');
                return slash < 0 || trimmed.Length <= 2 ? Path : trimmed.Substring(slash + 1);
            }
        }
    }

    public enum FolderColumn { Name, SizeOnDisk, Size, FileCount, Path }

    /// <summary>Folder totals (including subfolders) built from a scan, sorted largest first, filterable by name.</summary>
    public sealed class FolderTable
    {
        List<FolderEntry> all = new List<FolderEntry>();
        List<FolderEntry> view = new List<FolderEntry>();
        string filter = "";

        public FolderColumn SortColumn { get; private set; }
        public bool Descending { get; private set; }

        public FolderTable()
        {
            SortColumn = FolderColumn.SizeOnDisk;
            Descending = true;
        }

        public int Count { get { return view.Count; } }
        public FolderEntry this[int index] { get { return view[index]; } }
        public IEnumerable<FolderEntry> Rows { get { return view; } }

        /// <summary>Adds every file's size to its folder and to each parent folder up to the scan root.</summary>
        public static List<FolderEntry> Totals(IEnumerable<FileEntry> files, string root)
        {
            string top = PathUtil.ToDisplay(root);
            if (top.Length > 3) top = top.TrimEnd('\\');

            // First the folder's own files, then roll those amounts up to every parent.
            var own = new Dictionary<string, FolderEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (FileEntry f in files)
            {
                FolderEntry e;
                if (!own.TryGetValue(f.Folder, out e)) own.Add(f.Folder, e = new FolderEntry(f.Folder));
                e.Size += f.Size;
                e.SizeOnDisk += f.SizeOnDisk;
                e.FileCount++;
            }

            var totals = new Dictionary<string, FolderEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (FolderEntry o in own.Values)
            {
                for (string p = o.Path; p != null; p = Parent(p, top))
                {
                    FolderEntry t;
                    if (!totals.TryGetValue(p, out t)) totals.Add(p, t = new FolderEntry(p));
                    t.Size += o.Size;
                    t.SizeOnDisk += o.SizeOnDisk;
                    t.FileCount += o.FileCount;
                }
            }
            return new List<FolderEntry>(totals.Values);
        }

        /// <summary>The parent folder, or null once the scan root has been reached.</summary>
        static string Parent(string path, string root)
        {
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.Length <= 3) return null;
            int slash = path.LastIndexOf('\\');
            if (slash < 2) return null;
            return slash == 2 ? path.Substring(0, 3) : path.Substring(0, slash);
        }

        public void Load(List<FolderEntry> folders)
        {
            all = folders;
            all.Sort(Compare);
            ApplyFilter();
        }

        public void SortBy(FolderColumn column)
        {
            if (column == SortColumn) Descending = !Descending;
            else
            {
                SortColumn = column;
                Descending = column != FolderColumn.Name && column != FolderColumn.Path;
            }
            all.Sort(Compare);
            ApplyFilter();
        }

        public void SetFilter(string text)
        {
            filter = (text ?? "").Trim();
            ApplyFilter();
        }

        void ApplyFilter()
        {
            if (filter.Length == 0) { view = all; return; }
            var filtered = new List<FolderEntry>();
            foreach (FolderEntry f in all)
                if (f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(f);
            view = filtered;
        }

        int Compare(FolderEntry a, FolderEntry b)
        {
            int result;
            switch (SortColumn)
            {
                case FolderColumn.Name: result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                case FolderColumn.Size: result = a.Size.CompareTo(b.Size); break;
                case FolderColumn.FileCount: result = a.FileCount.CompareTo(b.FileCount); break;
                case FolderColumn.Path: result = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
                default: result = a.SizeOnDisk.CompareTo(b.SizeOnDisk); break;
            }
            if (result == 0) result = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            return Descending ? -result : result;
        }
    }
}
