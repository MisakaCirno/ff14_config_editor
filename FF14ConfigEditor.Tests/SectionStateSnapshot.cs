using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

internal sealed record SectionStateSnapshot(
    Type SectionType,
    short Index,
    byte[] Unknown1,
    int Length,
    byte[] Unknown2,
    byte[] Data,
    byte[] EndFlag)
{
    public static SectionStateSnapshot Capture(UISaveSection section)
    {
        return new SectionStateSnapshot(
            section.GetType(),
            section.index,
            section.unknown1.ToArray(),
            section.length,
            section.unknown2.ToArray(),
            section.data.ToArray(),
            section.endFlag.ToArray());
    }

    public void AssertMatches(SectionStateSnapshot actual)
    {
        Assert.Equal(SectionType, actual.SectionType);
        Assert.Equal(Index, actual.Index);
        Assert.Equal(Unknown1, actual.Unknown1);
        Assert.Equal(Length, actual.Length);
        Assert.Equal(Unknown2, actual.Unknown2);
        Assert.Equal(Data, actual.Data);
        Assert.Equal(EndFlag, actual.EndFlag);
    }
}
