using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

internal static class UISaveFormatExceptionAssert
{
    public static void HasDiagnostic(
        UISaveFormatException exception,
        string fieldName,
        string? offsetOrigin = null)
    {
        Assert.Equal(fieldName, exception.FieldName);
        Assert.Equal(offsetOrigin, exception.OffsetOrigin);
        Assert.Contains($"字段={fieldName}", exception.Message);

        if (offsetOrigin is null)
        {
            Assert.DoesNotContain("偏移来源=", exception.Message);
        }
        else
        {
            Assert.Contains($"偏移来源={offsetOrigin}", exception.Message);
        }
    }
}
