using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SplitWireTurkey
{
    public static class ZapretWhitelistSettings
    {
        public static string NormalizeSite(string rawSite)
        {
            if (string.IsNullOrWhiteSpace(rawSite))
                return string.Empty;

            var site = rawSite.Trim();

            site = site.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
            site = site.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
            site = site.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);

            site = site.Trim('/');
            site = site.Trim();

            var cutIndex = site.IndexOfAny(new[] { '/', '?', '#', ':' });
            if (cutIndex >= 0)
                site = site.Substring(0, cutIndex);

            site = site.Trim();
            site = site.Trim('.');

            return site.ToLowerInvariant();
        }

        public static List<string> ParseSites(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return new List<string>();

            return rawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeSite)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void WriteAllowList(string filePath, IEnumerable<string> sites)
        {
            var normalized = sites
                .Select(NormalizeSite)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(filePath, normalized);
        }
    }
}
