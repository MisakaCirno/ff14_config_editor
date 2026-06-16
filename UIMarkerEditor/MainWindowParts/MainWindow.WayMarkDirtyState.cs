using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private readonly List<WayMark> trackedWayMarks = [];
        private bool isWayMarkDirty;
        private bool suppressWayMarkDirtyTracking;

        private void TrackWayMarkChanges(IEnumerable<WayMark> wayMarks)
        {
            UntrackWayMarkChanges();

            foreach (WayMark wayMark in wayMarks)
            {
                trackedWayMarks.Add(wayMark);
                wayMark.PropertyChanged += WayMarkModel_PropertyChanged;
                SubscribeWayMarkPoints(wayMark);
            }
        }

        private void UntrackWayMarkChanges()
        {
            foreach (WayMark wayMark in trackedWayMarks)
            {
                wayMark.PropertyChanged -= WayMarkModel_PropertyChanged;
                UnsubscribeWayMarkPoints(wayMark);
            }

            trackedWayMarks.Clear();
        }

        private void SubscribeWayMarkPoints(WayMark wayMark)
        {
            wayMark.A.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.B.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.C.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.D.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.One.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.Two.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.Three.PropertyChanged += WayMarkModel_PropertyChanged;
            wayMark.Four.PropertyChanged += WayMarkModel_PropertyChanged;
        }

        private void UnsubscribeWayMarkPoints(WayMark wayMark)
        {
            wayMark.A.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.B.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.C.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.D.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.One.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.Two.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.Three.PropertyChanged -= WayMarkModel_PropertyChanged;
            wayMark.Four.PropertyChanged -= WayMarkModel_PropertyChanged;
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

            MessageBoxResult result = MessageBox.Show(
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
