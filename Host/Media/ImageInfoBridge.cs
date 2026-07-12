// Per-file image METADATA, reflected onto ExtendDB's ExtendDB.Utility.ImageInfoAds (NTFS ":info" ADS or a
// .ads/<name>.json sidecar). This is the data the Image Query tool needs beyond what the disk itself gives —
// Origin (screenscraper/emumovies/…), the CRC32-matched DatabaseId, FileType, NativeRegion, OriginalUrl,
// stored dimensions. It exists ONLY when ExtendDB is loaded (Available == false otherwise), so the Image
// Query columns that come from here are simply NULL in a standalone LiteBox.

#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace LbApiHost.Host.Media;

internal readonly struct ImageInfo
{
    public readonly int DatabaseId, Duplicate, SizeX, SizeY;
    public readonly long Crc32, FileSize;
    public readonly string Origin, FileType, NativeRegion, OriginalUrl;
    public ImageInfo(int dbId, long crc, string origin, int dup, string ft, string nr, string url, int sx, int sy, long fs)
    { DatabaseId = dbId; Crc32 = crc; Origin = origin ?? ""; Duplicate = dup; FileType = ft ?? ""; NativeRegion = nr ?? ""; OriginalUrl = url ?? ""; SizeX = sx; SizeY = sy; FileSize = fs; }
}

internal static class ImageInfoBridge
{
    private static bool _probed;
    private static MethodInfo? _read;
    private static PropertyInfo? _pDbId, _pCrc, _pOrigin, _pDup, _pFt, _pNr, _pUrl, _pSx, _pSy, _pFs;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ExtendDB");
            var ads = asm?.GetType("ExtendDB.Utility.ImageInfoAds");
            _read = ads?.GetMethod("Read", new[] { typeof(string) });
            var data = asm?.GetType("ExtendDB.Utility.ImageInfoAds+ImageInfoData")
                       ?? asm?.GetType("ExtendDB.Utility.ImageInfoData");
            if (_read != null) data ??= _read.ReturnType;
            if (data != null)
            {
                _pDbId = data.GetProperty("DatabaseId"); _pCrc = data.GetProperty("CRC32");
                _pOrigin = data.GetProperty("Origin"); _pDup = data.GetProperty("Duplicate");
                _pFt = data.GetProperty("FileType"); _pNr = data.GetProperty("NativeRegion");
                _pUrl = data.GetProperty("OriginalUrl"); _pSx = data.GetProperty("SizeX");
                _pSy = data.GetProperty("SizeY"); _pFs = data.GetProperty("FileSize");
            }
        }
        catch { _read = null; }
    }

    /// <summary>True only when ExtendDB's ImageInfoAds is reflectable — the enrichment columns exist then.</summary>
    public static bool Available { get { Probe(); return _read != null; } }

    /// <summary>Read the ADS :info for a file (null when unavailable / no info).</summary>
    public static ImageInfo? Read(string path)
    {
        Probe();
        if (_read == null || string.IsNullOrEmpty(path)) return null;
        try
        {
            var d = _read.Invoke(null, new object[] { path });
            if (d == null) return null;
            int I(PropertyInfo? p) { try { return p?.GetValue(d) is int v ? v : 0; } catch { return 0; } }
            long L(PropertyInfo? p) { try { var o = p?.GetValue(d); return o is long l ? l : o is int i ? i : 0; } catch { return 0; } }
            string S(PropertyInfo? p) { try { return p?.GetValue(d) as string ?? ""; } catch { return ""; } }
            return new ImageInfo(I(_pDbId), L(_pCrc), S(_pOrigin), I(_pDup), S(_pFt), S(_pNr), S(_pUrl), I(_pSx), I(_pSy), L(_pFs));
        }
        catch { return null; }
    }

    /// <summary>
    /// Read the ADS :info regardless of ExtendDB presence: through ExtendDB's own reader when loaded (which
    /// also PROVES ExtendDB can deserialize what LiteBox wrote), else a native parse of the compact
    /// short-key JSON via <see cref="FileMetaStore"/>. Null when there is no :info.
    /// </summary>
    public static ImageInfo? ReadAny(string path)
    {
        if (Available)
        {
            var viaExt = Read(path);
            if (viaExt != null) return viaExt;
        }
        try
        {
            var json = FileMetaStore.Read(path, FileMetaStore.StreamInfo);
            if (string.IsNullOrEmpty(json)) return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            int GI(string k) => r.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : 0;
            long GL(string k) => r.TryGetProperty(k, out var v) && v.TryGetInt64(out var n) ? n : 0;
            string GS(string k) => r.TryGetProperty(k, out var v) ? (v.GetString() ?? "") : "";
            return new ImageInfo(GI("db"), GL("crc"), GS("o"), GI("dup"), GS("ft"), GS("nr"), GS("url"), GI("x"), GI("y"), GL("fs"));
        }
        catch { return null; }
    }
}
