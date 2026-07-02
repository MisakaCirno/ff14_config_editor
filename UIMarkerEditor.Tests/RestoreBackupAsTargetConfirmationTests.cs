using System.IO;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class RestoreBackupAsTargetConfirmationTests
{
    [Fact]
    public void Evaluate_WhenTargetIsUISaveAndMissing_DoesNotRequireConfirmation()
    {
        string targetFilePath = Path.Combine(Path.GetTempPath(), "UISAVE.DAT");

        RestoreBackupAsTargetConfirmation confirmation =
            RestoreBackupAsTargetConfirmation.Evaluate(targetFilePath, targetExists: false);

        Assert.True(confirmation.IsExpectedSaveFileName);
        Assert.False(confirmation.TargetExists);
        Assert.False(confirmation.RequiresConfirmation);
        Assert.Equal(string.Empty, confirmation.Message);
    }

    [Fact]
    public void Evaluate_WhenTargetIsUISaveAndExists_WarnsWithFullPathAndOverwrite()
    {
        string targetFilePath = Path.Combine(Path.GetTempPath(), "UISAVE.DAT");

        RestoreBackupAsTargetConfirmation confirmation =
            RestoreBackupAsTargetConfirmation.Evaluate(targetFilePath, targetExists: true);

        Assert.True(confirmation.RequiresConfirmation);
        Assert.Contains(Path.GetFullPath(targetFilePath), confirmation.Message);
        Assert.Contains("覆盖此文件", confirmation.Message);
        Assert.DoesNotContain("目标文件名不是 UISAVE.DAT", confirmation.Message);
    }

    [Fact]
    public void Evaluate_WhenTargetNameIsUnexpectedAndMissing_WarnsAboutUnexpectedName()
    {
        string targetFilePath = Path.Combine(Path.GetTempPath(), "notes.txt");

        RestoreBackupAsTargetConfirmation confirmation =
            RestoreBackupAsTargetConfirmation.Evaluate(targetFilePath, targetExists: false);

        Assert.True(confirmation.RequiresConfirmation);
        Assert.Contains(Path.GetFullPath(targetFilePath), confirmation.Message);
        Assert.Contains("目标文件名不是 UISAVE.DAT", confirmation.Message);
        Assert.DoesNotContain("覆盖此文件", confirmation.Message);
    }

    [Fact]
    public void Evaluate_WhenTargetNameIsUnexpectedAndExists_WarnsAboutNameAndOverwrite()
    {
        string targetFilePath = Path.Combine(Path.GetTempPath(), "important.txt");

        RestoreBackupAsTargetConfirmation confirmation =
            RestoreBackupAsTargetConfirmation.Evaluate(targetFilePath, targetExists: true);

        Assert.True(confirmation.RequiresConfirmation);
        Assert.Contains(Path.GetFullPath(targetFilePath), confirmation.Message);
        Assert.Contains("目标文件名不是 UISAVE.DAT", confirmation.Message);
        Assert.Contains("覆盖此文件", confirmation.Message);
    }
}
