namespace FF14ConfigEditor.Tests;

internal sealed record ConfigStateSnapshot(
    string UserIDHex,
    string UserIDRawBytesHex,
    byte[] FileFormatVersionRaw,
    byte[] FileUnknownRaw,
    byte[] FileTailRaw,
    byte[] PayloadUnknownRaw,
    byte[] UserIDRaw,
    byte[] PayloadTailRaw,
    SectionStateSnapshot[] Sections)
{
    public static ConfigStateSnapshot Capture(ConfigUISave config)
    {
        return new ConfigStateSnapshot(
            config.UserIDHex,
            config.UserIDRawBytesHex,
            UISaveTestData.GetConfigByteArrayField(config, "fileFormatVersionRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "fileUnknownRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "fileTailRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "payloadUnknownRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "userIDRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "payloadTailRaw"),
            config.Sections.Select(SectionStateSnapshot.Capture).ToArray());
    }

    public void AssertMatches(ConfigUISave config)
    {
        ConfigStateSnapshot actual = Capture(config);
        Assert.Equal(UserIDHex, actual.UserIDHex);
        Assert.Equal(UserIDRawBytesHex, actual.UserIDRawBytesHex);
        Assert.Equal(FileFormatVersionRaw, actual.FileFormatVersionRaw);
        Assert.Equal(FileUnknownRaw, actual.FileUnknownRaw);
        Assert.Equal(FileTailRaw, actual.FileTailRaw);
        Assert.Equal(PayloadUnknownRaw, actual.PayloadUnknownRaw);
        Assert.Equal(UserIDRaw, actual.UserIDRaw);
        Assert.Equal(PayloadTailRaw, actual.PayloadTailRaw);
        Assert.Equal(Sections.Length, actual.Sections.Length);
        for (int i = 0; i < Sections.Length; i++)
        {
            Sections[i].AssertMatches(actual.Sections[i]);
        }
    }
}
