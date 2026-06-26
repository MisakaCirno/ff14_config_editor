using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace UIMarkerEditor;

internal static class GameCharacterDirectoryName
{
    private const string Prefix = "FFXIV_CHR";
    private const int PrefixLength = 9;
    private const int UserIDLength = 16;
    private const int DirectoryNameLength = PrefixLength + UserIDLength;

    public static bool TryExtractUserID(
        string? directoryName,
        [NotNullWhen(true)] out string? userID)
    {
        userID = null;
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        string trimmedName = directoryName.Trim();
        if (trimmedName.Length != DirectoryNameLength ||
            !trimmedName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidateUserID = trimmedName[PrefixLength..];
        if (!candidateUserID.All(Uri.IsHexDigit))
        {
            return false;
        }

        userID = candidateUserID.ToUpperInvariant();
        return true;
    }

    public static bool TryGetUserIDFromDirectoryPath(
        string? directoryPath,
        [NotNullWhen(true)] out string? userID)
    {
        userID = null;
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            string trimmedPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return TryExtractUserID(Path.GetFileName(trimmedPath), out userID);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
