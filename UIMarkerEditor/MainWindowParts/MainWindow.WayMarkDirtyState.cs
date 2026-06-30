using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private readonly WayMarkChangeTracker wayMarkChangeTracker;
        private bool isWayMarkDirty;
        private bool suppressWayMarkDirtyTracking;

        private void TrackWayMarkChanges(IEnumerable<WayMark> wayMarks)
        {
            wayMarkChangeTracker.Track(wayMarks);
        }

        private void WayMarkModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MarkWayMarkDirty();
        }

        private void MarkWayMarkDirty()
        {
            if (suppressWayMarkDirtyTracking || !HasLoadedWayMarkFile())
            {
                return;
            }

            SetWayMarkDirty(true);
        }

        private void SetWayMarkDirty(bool isDirty)
        {
            if (isWayMarkDirty == isDirty)
            {
                return;
            }

            isWayMarkDirty = isDirty;
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            Title = WayMarkDocumentTitleFormatter.BuildTitle(
                DefaultWindowTitle,
                currentFilePath,
                isWayMarkDirty);
        }

        private bool ConfirmSaveOrDiscardWayMarkChanges()
        {
            if (!isWayMarkDirty)
            {
                return true;
            }

            MessageBoxResult result = AppMessageBox.Show(
                this,
                "当前标点文件有未保存的修改。\n\n选择“是”保存修改，选择“否”放弃修改，选择“取消”继续编辑。",
                "未保存的标点修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (result == MessageBoxResult.No)
            {
                return true;
            }

            return SaveWayMarkFile(showSuccessMessage: false);
        }
    }
}
