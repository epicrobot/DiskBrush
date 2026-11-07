using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DiskBrush
{
    /// <summary>
    /// Size on disk as Windows reports it: allocated (compressed/sparse) size rounded up to the cluster size.
    /// Tiny files stored inside the MFT show one cluster, where Explorer may show 0.
    /// </summary>
    public static class SizeOnDisk
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceW(string lpRootPathName, out uint sectorsPerCluster,
            out uint bytesPerSector, out uint numberOfFreeClusters, out uint totalNumberOfClusters);

        const uint InvalidFileSize = 0xFFFFFFFF;
        const long DefaultCluster = 4096;

        static readonly Dictionary<string, long> ClusterCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new object();

        public static long ClusterSize(string displayPath)
        {
            string root = displayPath.Length >= 3 ? displayPath.Substring(0, 3) : displayPath;
            lock (Gate)
            {
                long cluster;
                if (ClusterCache.TryGetValue(root, out cluster)) return cluster;
                uint spc, bps, free, total;
                cluster = GetDiskFreeSpaceW(root, out spc, out bps, out free, out total)
                    ? (long)spc * bps
                    : DefaultCluster;
                if (cluster <= 0) cluster = DefaultCluster;
                ClusterCache[root] = cluster;
                return cluster;
            }
        }

        /// <summary>Returns size on disk; if Windows can't tell, falls back to the logical size rounded up.</summary>
        public static long Get(string displayPath, long logicalSize)
        {
            long cluster = ClusterSize(displayPath);
            uint high;
            uint low = GetCompressedFileSizeW(PathUtil.ToLong(displayPath), out high);
            long allocated;
            if (low == InvalidFileSize && Marshal.GetLastWin32Error() != 0)
                allocated = logicalSize;
            else
                allocated = ((long)high << 32) | low;
            return RoundUp(allocated, cluster);
        }

        public static long RoundUp(long bytes, long cluster)
        {
            if (bytes <= 0) return 0;
            return ((bytes + cluster - 1) / cluster) * cluster;
        }
    }
}
