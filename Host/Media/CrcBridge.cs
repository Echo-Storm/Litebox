// CRC32 of a local image file, memoised. Used to tell whether a "web" image (from the LaunchBox metadata DB,
// which carries each image's CRC32) is already OWNED locally — a local file with the same CRC32 is the same
// image, whatever its filename. This mirrors ExtendDB's own dedup (CrcCache): the stored ":crc32" stream is
// read FIRST (via FileMetaStore, which delegates to ExtendDB's FileMetadataStorage when it's loaded), and on
// a miss we compute the standard CRC-32 (poly 0xEDB88320) once, cache it in memory AND write it back to the
// ":crc32" stream — so a file obtained WITHOUT the addon (no ADS) is still identified correctly, and the
// next lookup is cheap.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace LbApiHost.Host.Media;

internal static class CrcBridge
{
    private static readonly Dictionary<string, uint> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static uint[]? _table;

    /// <summary>Unsigned CRC-32 of a file (0 on error / empty). Cached; ":crc32" ADS first, computed on miss.</summary>
    public static uint Crc(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (_cache.TryGetValue(path, out var c)) return c;
        uint crc = 0;
        var stored = FileMetaStore.Read(path, FileMetaStore.StreamCrc32);
        if (stored != null && long.TryParse(stored, out var l) && l != 0) crc = unchecked((uint)l);
        if (crc == 0)
        {
            crc = Compute(path);
            if (crc != 0) { try { FileMetaStore.Write(path, FileMetaStore.StreamCrc32, ((long)crc).ToString()); } catch { } }
        }
        _cache[path] = crc;
        return crc;
    }

    /// <summary>Prime the in-memory cache with a known CRC (e.g. the DB value right after a download).</summary>
    public static void Seed(string path, uint crc)
    {
        if (!string.IsNullOrEmpty(path)) _cache[path] = crc;
    }

    private static uint Compute(string path)
    {
        _table ??= BuildTable();
        try
        {
            uint crc = 0xFFFFFFFFu;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
            var buf = new byte[1 << 16];
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++)
                    crc = (crc >> 8) ^ _table[(crc ^ buf[i]) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }
        catch { return 0; }
    }

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
}
