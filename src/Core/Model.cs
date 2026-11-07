using System.Collections.Generic;

namespace DiskBrush
{
    /// <summary>One file found by a scan. Folder strings are shared between files of the same folder to save memory.</summary>
    public sealed class FileEntry
    {
        public readonly string Name;
        public readonly string Folder;
        public readonly long Size;
        public readonly long SizeOnDisk;

        public FileEntry(string name, string folder, long size, long sizeOnDisk)
        {
            Name = name;
            Folder = folder;
            Size = size;
            SizeOnDisk = sizeOnDisk;
        }

        public string FullPath
        {
            get { return PathUtil.Join(Folder, Name); }
        }
    }

    /// <summary>A folder the scan could not read, with the reason.</summary>
    public sealed class UnreadableFolder
    {
        public readonly string Path;
        public readonly string Reason;

        public UnreadableFolder(string path, string reason)
        {
            Path = path;
            Reason = reason;
        }
    }

    public sealed class ScanResult
    {
        public readonly List<FileEntry> Files = new List<FileEntry>();
        public readonly List<UnreadableFolder> Unreadable = new List<UnreadableFolder>();
        public bool Cancelled;

        public long TotalSizeOnDisk
        {
            get
            {
                long total = 0;
                foreach (FileEntry f in Files) total += f.SizeOnDisk;
                return total;
            }
        }
    }
}
