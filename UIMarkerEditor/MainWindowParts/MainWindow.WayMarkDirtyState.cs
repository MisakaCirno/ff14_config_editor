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

        private bool TryCommitPendingWayMarkEdits(bool showValidationMessage = true)
        {
            if (WayMarkEditor_Control.CommitPendingEdits())
            {
                return true;
            }

            if (showValidationMessage)
            {
                AppMessageBox.Show(
                    this,
                    "当前坐标输入不完整或超出可保存范围，请修正后再继续。",
                    "坐标输入无效",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        private bool ConfirmSaveOrDiscardWayMarkChanges()
        {
            if (!TryCommitPendingWayMarkEdits())
            {
                return false;
            }

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

        private bool TryPrepareCloseWayMarkChanges(out bool shouldSave)
        {
            shouldSave = false;
            if (!TryCommitPendingWayMarkEdits())
            {
                return false;
            }

            if (!isWayMarkDirty)
            {
                return true;
            }

            MessageBoxResult result = AppMessageBox.Show(
                this,
                "当前标点文件有未保存的修改。\n\n选择“是”在关闭前保存，选择“否”继续关闭并放弃这些修改，选择“取消”返回编辑。\n\n如果后续关闭被取消，当前修改会保留。",
                "未保存的标点修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            shouldSave = result == MessageBoxResult.Yes;
            return true;
        }
    }
}
