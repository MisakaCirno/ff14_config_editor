using System.Collections.Generic;
using System.Text;

namespace UIMarkerEditor;

internal static class PathTextRepair
{
    private static readonly UTF8Encoding StrictUtf8Encoding = new(false, throwOnInvalidBytes: true);
    private static readonly Lazy<Encoding?> GbkEncoding = new(CreateGbkEncoding);

    public static string RepairCommonUtf8Mojibake(string value)
    {
        return TryRepairUtf8DecodedAsGbk(value, out string repaired)
            ? repaired
            : value;
    }

    public static IEnumerable<string> EnumerateCommonUtf8MojibakeVariants(string value)
    {
        yield return value;

        if (TryRepairUtf8DecodedAsGbk(value, out string repaired) &&
            !string.Equals(value, repaired, StringComparison.Ordinal))
        {
            yield return repaired;
        }
    }

    internal static bool TryRepairUtf8DecodedAsGbk(string value, out string repaired)
    {
        repaired = value;
        if (string.IsNullOrWhiteSpace(value) || !HasUtf8AsGbkMojibakeMarker(value))
        {
            return false;
        }

        Encoding? gbkEncoding = GbkEncoding.Value;
        if (gbkEncoding == null)
        {
            return false;
        }

        try
        {
            byte[] bytes = gbkEncoding.GetBytes(value);
            string decoded = StrictUtf8Encoding.GetString(bytes);
            if (string.Equals(decoded, value, StringComparison.Ordinal) ||
                decoded.Contains('\uFFFD') ||
                CountUtf8AsGbkMojibakeMarkers(decoded) >= CountUtf8AsGbkMojibakeMarkers(value) ||
                !ContainsCjk(decoded))
            {
                return false;
            }

            repaired = decoded;
            return true;
        }
        catch (Exception ex) when (ex is EncoderFallbackException or DecoderFallbackException or ArgumentException)
        {
            return false;
        }
    }

    private static Encoding? CreateGbkEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasUtf8AsGbkMojibakeMarker(string value)
    {
        return CountUtf8AsGbkMojibakeMarkers(value) > 0;
    }

    private static int CountUtf8AsGbkMojibakeMarkers(string value)
    {
        int count = 0;
        foreach (char character in value)
        {
            count += character switch
            {
                '\u20AC' => 1,
                '\u6D93' => 1,
                '\u6769' => 1,
                '\u7F01' => 1,
                '\u9357' => 1,
                '\u935D' => 1,
                '\u9366' => 1,
                '\u93B4' => 1,
                '\u93C8' => 1,
                '\u93C9' => 1,
                '\u9428' => 1,
                '\u950A' => 1,
                _ => 0
            };
        }

        return count;
    }

    private static bool ContainsCjk(string value)
    {
        foreach (char character in value)
        {
            if (character is >= '\u4E00' and <= '\u9FFF')
            {
                return true;
            }
        }

        return false;
    }
}
