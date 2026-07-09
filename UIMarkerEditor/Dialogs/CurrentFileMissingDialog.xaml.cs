using System.Windows;

namespace UIMarkerEditor;

public partial class CurrentFileMissingDialog : Window
{
    public CurrentFileMissingDialog(string filePath)
    {
        InitializeComponent();
        FilePath_TextBox.Text = filePath;
    }

    public CurrentFileMissingDialogResult Result { get; private set; } = CurrentFileMissingDialogResult.ContinueEditing;

    private void SaveToOriginal_Button_Click(object sender, RoutedEventArgs e)
    {
        Result = CurrentFileMissingDialogResult.SaveToOriginalPath;
        DialogResult = true;
    }

    private void CloseFile_Button_Click(object sender, RoutedEventArgs e)
    {
        Result = CurrentFileMissingDialogResult.CloseCurrentFile;
        DialogResult = true;
    }

    private void ContinueEditing_Button_Click(object sender, RoutedEventArgs e)
    {
        Result = CurrentFileMissingDialogResult.ContinueEditing;
        DialogResult = true;
    }
}

public enum CurrentFileMissingDialogResult
{
    SaveToOriginalPath,
    CloseCurrentFile,
    ContinueEditing
}
