namespace DiskBrush
{
    /// <summary>Converts between display paths (C:\x) and Win32 long-path form (\\?\C:\x).</summary>
    public static class PathUtil
    {
        const string LongPrefix = @"\\?\";

        public static string ToLong(string path)
        {
            return path.StartsWith(LongPrefix) ? path : LongPrefix + path;
        }

        public static string ToDisplay(string path)
        {
            return path.StartsWith(LongPrefix) ? path.Substring(LongPrefix.Length) : path;
        }

        public static string Join(string folder, string name)
        {
            return folder.EndsWith("\\") ? folder + name : folder + "\\" + name;
        }

        /// <summary>True for paths directly under a drive root, e.g. C:\Windows.</summary>
        public static bool IsDirectlyUnderDriveRoot(string displayPath)
        {
            return displayPath.Length > 3 && displayPath[1] == ':' && displayPath[2] == '\\'
                && displayPath.IndexOf('\\', 3) < 0;
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? bytes + " B" : value.ToString("0.0") + " " + units[unit];
        }
    }
}
