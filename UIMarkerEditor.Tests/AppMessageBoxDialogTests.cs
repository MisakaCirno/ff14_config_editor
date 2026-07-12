using System.Windows;
using System.Windows.Input;

namespace UIMarkerEditor.Tests;

public sealed class AppMessageBoxDialogTests
{
    [Fact]
    public void BuildClipboardText_IncludesCompleteDialogContent()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            AppMessageBoxDialog dialog = new(
                "第一行\n第二行",
                "复制测试",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                "不再提示",
                isOptionChecked: true);
            try
            {
                string clipboardText = dialog.BuildClipboardText();

                Assert.Contains("复制测试", clipboardText, StringComparison.Ordinal);
                Assert.Contains("第一行\n第二行", clipboardText, StringComparison.Ordinal);
                Assert.Contains("[x] 不再提示", clipboardText, StringComparison.Ordinal);
                Assert.Contains("确定   取消", clipboardText, StringComparison.Ordinal);
                Assert.StartsWith("---------------------------", clipboardText, StringComparison.Ordinal);
                Assert.EndsWith("---------------------------", clipboardText, StringComparison.Ordinal);
                Assert.Contains(
                    dialog.CommandBindings.Cast<CommandBinding>(),
                    binding => ReferenceEquals(binding.Command, ApplicationCommands.Copy));
                Assert.Contains(
                    dialog.InputBindings.OfType<KeyBinding>(),
                    binding => ReferenceEquals(binding.Command, ApplicationCommands.Copy) &&
                        binding.Key == Key.C &&
                        binding.Modifiers == ModifierKeys.Control);
                Assert.Contains(
                    dialog.InputBindings.OfType<KeyBinding>(),
                    binding => ReferenceEquals(binding.Command, ApplicationCommands.Copy) &&
                        binding.Key == Key.Insert &&
                        binding.Modifiers == ModifierKeys.Control);
            }
            finally
            {
                dialog.Close();
            }
        });

        Assert.Null(exception);
    }
}
