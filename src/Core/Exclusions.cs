using System;
using System.Collections.Generic;
using System.IO;

namespace DiskBrush
{
    /// <summary>Windows system files and folders that are never scanned or shown.</summary>
    public static class Exclusions
    {
        static readonly HashSet<string> RootFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData",
            "System Volume Information", "$Recycle.Bin", "Recovery", "$WinREAgent"
        };

        static readonly HashSet<string> RootFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pagefile.sys", "hiberfil.sys", "swapfile.sys"
        };

        public static bool IsExcludedFolder(string displayPath)
        {
            return PathUtil.IsDirectlyUnderDriveRoot(displayPath)
                && RootFolders.Contains(displayPath.Substring(3));
        }

        /// <summary>True when the path is an excluded system folder or anywhere inside one (e.g. C:\Windows\System32).</summary>
        public static bool IsInsideExcludedFolder(string displayPath)
        {
            string path = displayPath.TrimEnd('\\');
            if (path.Length <= 3) return false;
            int slash = path.IndexOf('\\', 3);
            return IsExcludedFolder(slash < 0 ? path : path.Substring(0, slash));
        }

        public static bool IsExcludedFile(string displayPath, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.System) != 0) return true;
            return PathUtil.IsDirectlyUnderDriveRoot(displayPath)
                && RootFiles.Contains(displayPath.Substring(3));
        }
    }
}
