using System.Globalization;
using System.Text.RegularExpressions;
using FtpClient.Core.Models;

namespace FtpClient.Infrastructure.Ftp;

public static partial class FtpListParser
{
    public static IReadOnlyList<FtpEntry> ParseMlsd(string content, string currentPath)
    {
        var entries = new List<FtpEntry>();
        foreach (var rawLine in SplitLines(content))
        {
            var separator = rawLine.IndexOf(' ');
            if (separator <= 0 || separator == rawLine.Length - 1)
            {
                continue;
            }

            var facts = rawLine[..separator]
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

            var name = rawLine[(separator + 1)..].Trim();
            if (name is "." or ".." || !facts.TryGetValue("type", out var type))
            {
                continue;
            }

            var isDirectory = type.Equals("dir", StringComparison.OrdinalIgnoreCase)
                || type.Equals("cdir", StringComparison.OrdinalIgnoreCase)
                || type.Equals("pdir", StringComparison.OrdinalIgnoreCase);

            long? size = null;
            if (facts.TryGetValue("size", out var sizeText)
                && long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize))
            {
                size = parsedSize;
            }

            DateTimeOffset? modified = null;
            if (facts.TryGetValue("modify", out var modifyText)
                && DateTimeOffset.TryParseExact(
                    modifyText,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsedModified))
            {
                modified = parsedModified;
            }

            entries.Add(new FtpEntry(
                name,
                CombineRemotePath(currentPath, name),
                isDirectory,
                isDirectory ? null : size,
                modified,
                facts.GetValueOrDefault("perm", string.Empty)));
        }

        return entries;
    }

    public static IReadOnlyList<FtpEntry> ParseUnixList(string content, string currentPath)
    {
        var entries = new List<FtpEntry>();
        foreach (var line in SplitLines(content))
        {
            var match = UnixListRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            if (name is "." or "..")
            {
                continue;
            }

            var isDirectory = match.Groups["type"].Value == "d";
            long? size = long.TryParse(match.Groups["size"].Value, out var parsedSize) ? parsedSize : null;
            entries.Add(new FtpEntry(
                name,
                CombineRemotePath(currentPath, name),
                isDirectory,
                isDirectory ? null : size,
                null,
                match.Groups["permissions"].Value));
        }

        return entries;
    }

    public static string CombineRemotePath(string directory, string name)
    {
        var normalizedDirectory = string.IsNullOrWhiteSpace(directory) ? "/" : directory.Replace('\\', '/');
        return normalizedDirectory == "/"
            ? $"/{name}"
            : $"{normalizedDirectory.TrimEnd('/')}/{name}";
    }

    private static IEnumerable<string> SplitLines(string content) =>
        content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"^(?<type>[-dl])(?<permissions>[rwxstST-]{9})\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+\w+\s+\d+\s+[\d:]+\s+(?<name>.+)$")]
    private static partial Regex UnixListRegex();
}
