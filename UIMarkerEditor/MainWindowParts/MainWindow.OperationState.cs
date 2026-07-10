namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        internal bool IsBlockingOperationInProgress()
        {
            return isWayMarkFileLoading || IsNonFileOperationInProgress();
        }

        private bool IsNonFileOperationInProgress()
        {
            return MapDataOperationOverlay_Control.IsBusy ||
                BackupRestore_Control.IsOperationBusy ||
                CharacterProfiles_Control.IsOperationBusy;
        }

        private bool TryGetBlockingOperationDescription(out string description)
        {
            if (MapDataOperationOverlay_Control.IsBusy)
            {
                description = "地图数据操作";
                return true;
            }

            if (BackupRestore_Control.IsOperationBusy)
            {
                description = "备份操作";
                return true;
            }

            if (CharacterProfiles_Control.IsOperationBusy)
            {
                description = "角色备注操作";
                return true;
            }

            description = string.Empty;
            return false;
        }
    }
}
