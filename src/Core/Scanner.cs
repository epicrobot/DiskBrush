using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;

namespace DiskBrush
{
    /// <summary>Walks a folder tree and collects every file, skipping Windows system files and folder links.</summary>
    public static class Scanner
    {
        const int ProgressEvery = 2000;

        /// <param name="progress">Called with the number of files found so far; may be null.</param>
        public static ScanResult Scan(string root, CancellationToken token, Action<long> progress)
        {
            var result = new ScanResult();
            var pending = new Stack<string>();
            pending.Push(PathUtil.ToDisplay(root));
            long count = 0;

            while (pending.Count > 0)
            {
                if (token.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }
                string folder = pending.Pop();
                string reason = null;
                try
                {
                    var dir = new DirectoryInfo(PathUtil.ToLong(folder));
                    // Errors are thrown while iterating, so the whole loop sits inside the try.
                    foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos())
                    {
                        if (token.IsCancellationRequested) break;
                        string path = PathUtil.Join(folder, info.Name);
                        FileAttributes attributes = info.Attributes;

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            // Never follow junctions, symlinks or mount points: no loops, no double counting.
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (Exclusions.IsExcludedFolder(path)) continue;
                            pending.Push(path);
                        }
                        else
                        {
                            if (Exclusions.IsExcludedFile(path, attributes)) continue;
                            long size = ((FileInfo)info).Length;
                            result.Files.Add(new FileEntry(info.Name, folder, size, SizeOnDisk.Get(path, size)));
                            count++;
                            if (progress != null && count % ProgressEvery == 0) progress(count);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { reason = "Access denied"; }
                catch (SecurityException) { reason = "Access denied"; }
                catch (IOException ex) { reason = ex.Message; }
                catch (ArgumentException ex) { reason = "Invalid path: " + ex.Message; }

                if (reason != null) result.Unreadable.Add(new UnreadableFolder(folder, reason));
            }

            if (token.IsCancellationRequested) result.Cancelled = true;
            if (progress != null) progress(count);
            return result;
        }
    }
}
