using System.Windows;

namespace UIMarkerEditor;

public partial class BackupCharacterProfileDialog : Window
{
    private readonly bool canEditUserID;

    public string UserID => UserID_TextBox.Text.Trim().ToUpperInvariant();
    public string CharacterName => CharacterName_TextBox.Text.Trim();
    public string DataCenter => ServerPicker_Control.SelectedDataCenter;
    public string World => ServerPicker_Control.SelectedWorld;
    public string Note => Note_TextBox.Text.Trim();

    public BackupCharacterProfileDialog(
        string userID,
        IEnumerable<ServerGroup> serverGroups,
        CharacterProfile? existingProfile)
        : this(
            userID,
            serverGroups,
            existingProfile,
            canEditUserID: false,
            title: "为备份创建角色备注",
            hint: "为选中的备份创建角色备注。")
    {
    }

    public BackupCharacterProfileDialog(IEnumerable<ServerGroup> serverGroups)
        : this(
            string.Empty,
            serverGroups,
            existingProfile: null,
            canEditUserID: true,
            title: "新建角色备注",
            hint: "创建一个新的角色备注。")
    {
    }

    private BackupCharacterProfileDialog(
        string userID,
        IEnumerable<ServerGroup> serverGroups,
        CharacterProfile? existingProfile,
        bool canEditUserID,
        string title,
        string hint)
    {
        InitializeComponent();

        this.canEditUserID = canEditUserID;
        Title = title;
        Hint_TextBlock.Text = hint;
        UserID_TextBox.Text = userID;
        UserID_TextBox.IsReadOnly = !canEditUserID;
        CharacterName_TextBox.Text = existingProfile?.CharacterName ?? string.Empty;
        Note_TextBox.Text = existingProfile?.Note ?? string.Empty;

        ServerPicker_Control.SetServerGroups(serverGroups);
        ServerPicker_Control.SelectServer(existingProfile?.DataCenter ?? string.Empty, existingProfile?.World ?? string.Empty);

        Loaded += (_, _) =>
        {
            if (this.canEditUserID)
            {
                UserID_TextBox.Focus();
                return;
            }

            CharacterName_TextBox.Focus();
        };
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserID))
        {
            AppMessageBox.Show(this, "User ID 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!IsValidUserID(UserID))
        {
            AppMessageBox.Show(this, "User ID 必须是 16 位十六进制字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(CharacterName))
        {
            AppMessageBox.Show(this, "角色名不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static bool IsValidUserID(string userID)
    {
        return userID.Length == 16 && userID.All(Uri.IsHexDigit);
    }
}
