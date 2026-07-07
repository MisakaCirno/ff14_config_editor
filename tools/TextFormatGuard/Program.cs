using System.Diagnostics;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var command = args.Length == 1 ? args[0].Trim().ToLowerInvariant() : string.Empty;
if (command is not ("check" or "fix"))
{
    PrintUsage();
    return 2;
}

var fix = command == "fix";
var repoRoot = Git.GetRepositoryRoot();
var trackedFiles = Git.GetTrackedFiles(repoRoot);
var result = TextFormatGuard.Run(repoRoot, trackedFiles, fix);

foreach (var finding in result.Findings)
{
    Console.WriteLine(finding);
}

Console.WriteLine();
Console.WriteLine(fix
    ? $"TextFormatGuard fix: checked {result.CheckedCount} text files, fixed {result.FixedCount} files, skipped {result.SkippedCount} binary files, skipped {result.MissingCount} missing tracked files, errors {result.ErrorCount}."
    : $"TextFormatGuard check: checked {result.CheckedCount} text files, issues {result.IssueCount}, skipped {result.SkippedCount} binary files, skipped {result.MissingCount} missing tracked files, errors {result.ErrorCount}.");

return result.HasFailures ? 1 : 0;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/TextFormatGuard -- check");
    Console.WriteLine("  dotnet run --project tools/TextFormatGuard -- fix");
}

internal static class TextFormatGuard
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".ico",
        ".dat",
        ".bin",
        ".zip",
        ".7z",
        ".rar",
        ".gz",
        ".tar",
        ".bmp",
        ".gif",
        ".webp",
        ".pdf",
        ".dll",
        ".exe",
        ".pdb",
    };

    private static readonly HashSet<string> BomRequiredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
    };

    public static GuardResult Run(string repoRoot, IReadOnlyList<string> trackedFiles, bool fix)
    {
        var result = new GuardResult();

        foreach (var relativePath in trackedFiles)
        {
            var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var displayPath = relativePath.Replace('\\', '/');
            var extension = Path.GetExtension(relativePath);

            if (BinaryExtensions.Contains(extension))
            {
                result.SkippedCount++;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                result.AddMissing(displayPath);
                continue;
            }

            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Contains((byte)0))
            {
                result.AddError(displayPath, "contains NUL bytes; skipped as binary or non-UTF-8 text");
                continue;
            }

            var hasBom = HasUtf8Bom(bytes);
            string text;
            try
            {
                var contentBytes = hasBom ? bytes.AsSpan(Utf8Bom.Length).ToArray() : bytes;
                text = StrictUtf8.GetString(contentBytes);
            }
            catch (DecoderFallbackException)
            {
                result.AddError(displayPath, "is not valid UTF-8");
                continue;
            }

            result.CheckedCount++;

            var requiresBom = BomRequiredExtensions.Contains(extension);
            var desiredBomText = requiresBom ? "UTF-8 BOM" : "UTF-8 without BOM";
            var issues = new List<string>();

            if (requiresBom && !hasBom)
            {
                issues.Add($"expected {desiredBomText}");
            }
            else if (!requiresBom && hasBom)
            {
                issues.Add($"expected {desiredBomText}");
            }

            if (!HasCrLfLineEndings(text))
            {
                issues.Add("expected CRLF line endings");
            }

            if (issues.Count == 0)
            {
                continue;
            }

            if (!fix)
            {
                result.AddIssue(displayPath, string.Join("; ", issues));
                continue;
            }

            var normalizedText = NormalizeLineEndings(text);
            File.WriteAllBytes(fullPath, EncodeUtf8(normalizedText, requiresBom));
            result.AddFixed(displayPath, string.Join("; ", issues));
        }

        return result;
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= Utf8Bom.Length
        && bytes[0] == Utf8Bom[0]
        && bytes[1] == Utf8Bom[1]
        && bytes[2] == Utf8Bom[2];

    private static bool HasCrLfLineEndings(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' && (index == 0 || text[index - 1] != '\r'))
            {
                return false;
            }

            if (text[index] == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n'))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeLineEndings(string text)
    {
        var builder = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                builder.Append("\r\n");
                continue;
            }

            if (current == '\n')
            {
                builder.Append("\r\n");
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static byte[] EncodeUtf8(string text, bool emitBom)
    {
        var contentBytes = StrictUtf8.GetBytes(text);
        if (!emitBom)
        {
            return contentBytes;
        }

        var bytes = new byte[Utf8Bom.Length + contentBytes.Length];
        Utf8Bom.CopyTo(bytes, 0);
        contentBytes.CopyTo(bytes.AsSpan(Utf8Bom.Length));
        return bytes;
    }
}

internal static class Git
{
    public static string GetRepositoryRoot()
    {
        var output = Run("rev-parse --show-toplevel", Directory.GetCurrentDirectory());
        return Path.GetFullPath(output.Trim());
    }

    public static IReadOnlyList<string> GetTrackedFiles(string repoRoot)
    {
        var output = Run("ls-files -z", repoRoot);
        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }

    private static string Run(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed: {error.Trim()}");
        }

        return output;
    }
}

internal sealed class GuardResult
{
    public int CheckedCount { get; set; }
    public int SkippedCount { get; set; }
    public int MissingCount { get; private set; }
    public int FixedCount { get; private set; }
    public int IssueCount { get; private set; }
    public int ErrorCount { get; private set; }
    public List<string> Findings { get; } = [];
    public bool HasFailures => IssueCount > 0 || ErrorCount > 0;

    public void AddIssue(string path, string message)
    {
        IssueCount++;
        Findings.Add($"ISSUE {path}: {message}");
    }

    public void AddFixed(string path, string message)
    {
        FixedCount++;
        Findings.Add($"FIXED {path}: {message}");
    }

    public void AddMissing(string path)
    {
        MissingCount++;
        Findings.Add($"SKIPPED {path}: missing from working tree");
    }

    public void AddError(string path, string message)
    {
        ErrorCount++;
        Findings.Add($"ERROR {path}: {message}");
    }
}
