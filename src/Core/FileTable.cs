using System;
using System.Collections.Generic;

namespace DiskBrush
{
    public enum FileColumn { Name, SizeOnDisk, Size, Folder, FullPath }

    /// <summary>The sorted, filtered list of files behind the Files view. Has no UI dependencies so it can be tested.</summary>
    public sealed class FileTable
    {
        List<FileEntry> all = new List<FileEntry>();
        List<FileEntry> view = new List<FileEntry>();
        string filter = "";

        public FileColumn SortColumn { get; private set; }
        public bool Descending { get; private set; }

        public FileTable()
        {
            SortColumn = FileColumn.SizeOnDisk;
            Descending = true;
        }

        public int Count { get { return view.Count; } }
        public FileEntry this[int index] { get { return view[index]; } }
        public IEnumerable<FileEntry> Rows { get { return view; } }

        public void Load(List<FileEntry> files)
        {
            all = files;
            all.Sort(Compare);
            ApplyFilter();
        }

        /// <summary>Clicking the current column flips direction; a new column starts largest-first for sizes, A–Z for text.</summary>
        public void SortBy(FileColumn column)
        {
            if (column == SortColumn)
            {
                Descending = !Descending;
            }
            else
            {
                SortColumn = column;
                Descending = column == FileColumn.SizeOnDisk || column == FileColumn.Size;
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
            if (filter.Length == 0)
            {
                view = all;
                return;
            }
            var filtered = new List<FileEntry>();
            foreach (FileEntry f in all)
                if (f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(f);
            view = filtered;
        }

        int Compare(FileEntry a, FileEntry b)
        {
            int result;
            switch (SortColumn)
            {
                case FileColumn.Size: result = a.Size.CompareTo(b.Size); break;
                case FileColumn.Name: result = Text(a.Name, b.Name); break;
                case FileColumn.Folder: result = Text(a.Folder, b.Folder); break;
                case FileColumn.FullPath:
                    result = Text(a.Folder, b.Folder);
                    if (result == 0) result = Text(a.Name, b.Name);
                    break;
                default: result = a.SizeOnDisk.CompareTo(b.SizeOnDisk); break;
            }
            if (result == 0) result = Text(a.Name, b.Name);
            return Descending ? -result : result;
        }

        static int Text(string a, string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
