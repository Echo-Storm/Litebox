// Extension for a downloaded image, computed EXACTLY like ExtendDB's
// SqliteCommand_ExecuteReader_Patch.ExtractFileType(fileName): the GameImages FileName is a source URL for
// non-launchbox origins (screenscraper, igdb, …), so its "extension" is unreliable. ExtendDB therefore:
//   1. reads a "filetype=XXX" query parameter first (screenscraper URLs carry filetype=png), else
//   2. takes the part after the last dot, stripping any "?query" suffix.
// This is a faithful port so LiteBox names downloaded files the same way ExtendDB does.

#nullable enable

using System.Text.RegularExpressions;

namespace LbApiHost.Host.Media;

internal static class ImageFileType
{
    /// <summary>Extension (with leading dot) for a GameImages FileName / source URL, or "" if none.</summary>
    public static string Extract(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "";

        if (fileName.Contains("filetype="))
        {
            var match = Regex.Match(fileName, @"filetype=(\w+)");
            if (match.Success)
                return "." + match.Groups[1].Value;
        }

        int lastDot = fileName.LastIndexOf('.');
        if (lastDot >= 0)
        {
            int queryStart = fileName.IndexOf('?', lastDot);
            return queryStart >= 0 ? fileName.Substring(lastDot, queryStart - lastDot) : fileName.Substring(lastDot);
        }

        return "";
    }
}
