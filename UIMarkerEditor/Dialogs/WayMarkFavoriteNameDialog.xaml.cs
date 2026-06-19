using System.Windows;

namespace UIMarkerEditor;

public partial class WayMarkFavoriteNameDialog : Window
{
    public string CommentName => CommentName_TextBox.Text.Trim();

    public WayMarkFavoriteNameDialog(string regionDisplayName, string defaultCommentName = "")
    {
        InitializeComponent();
        Hint_TextBlock.Text = $"为 {regionDisplayName} 添加一个便于查找的注释名。";
        CommentName_TextBox.Text = defaultCommentName;
        CommentName_TextBox.SelectAll();
        Loaded += (_, _) => CommentName_TextBox.Focus();
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommentName))
        {
            AppMessageBox.Show(this, "请填写注释名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}