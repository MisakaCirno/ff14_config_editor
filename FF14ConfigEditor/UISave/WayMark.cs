using System.ComponentModel;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// 每一组标点预设的数据结构。
    /// </summary>
    public class WayMark : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public WayMarkPoint A { get; set; } = new();
        public WayMarkPoint B { get; set; } = new();
        public WayMarkPoint C { get; set; } = new();
        public WayMarkPoint D { get; set; } = new();
        public WayMarkPoint One { get; set; } = new();
        public WayMarkPoint Two { get; set; } = new();
        public WayMarkPoint Three { get; set; } = new();
        public WayMarkPoint Four { get; set; } = new();

        // enableFlag 共 8 位，每一位代表一个标点的启用状态。
        // 位 0 到位 7 依次代表 A、B、C、D、1、2、3、4。
        public byte enableFlag;
        public bool AEnabled
        {
            get { return (enableFlag & 0x01) != 0; }
            set
            {
                bool current = (enableFlag & 0x01) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x01; else enableFlag &= 0xFE;
                    OnPropertyChanged(nameof(AEnabled));
                }
            }
        }
        public bool BEnabled
        {
            get { return (enableFlag & 0x02) != 0; }
            set
            {
                bool current = (enableFlag & 0x02) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x02; else enableFlag &= 0xFD;
                    OnPropertyChanged(nameof(BEnabled));
                }
            }
        }
        public bool CEnabled
        {
            get { return (enableFlag & 0x04) != 0; }
            set
            {
                bool current = (enableFlag & 0x04) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x04; else enableFlag &= 0xFB;
                    OnPropertyChanged(nameof(CEnabled));
                }
            }
        }
        public bool DEnabled
        {
            get { return (enableFlag & 0x08) != 0; }
            set
            {
                bool current = (enableFlag & 0x08) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x08; else enableFlag &= 0xF7;
                    OnPropertyChanged(nameof(DEnabled));
                }
            }
        }
        public bool OneEnabled
        {
            get { return (enableFlag & 0x10) != 0; }
            set
            {
                bool current = (enableFlag & 0x10) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x10; else enableFlag &= 0xEF;
                    OnPropertyChanged(nameof(OneEnabled));
                }
            }
        }
        public bool TwoEnabled
        {
            get { return (enableFlag & 0x20) != 0; }
            set
            {
                bool current = (enableFlag & 0x20) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x20; else enableFlag &= 0xDF;
                    OnPropertyChanged(nameof(TwoEnabled));
                }
            }
        }
        public bool ThreeEnabled
        {
            get { return (enableFlag & 0x40) != 0; }
            set
            {
                bool current = (enableFlag & 0x40) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x40; else enableFlag &= 0xBF;
                    OnPropertyChanged(nameof(ThreeEnabled));
                }
            }
        }
        public bool FourEnabled
        {
            get { return (enableFlag & 0x80) != 0; }
            set
            {
                bool current = (enableFlag & 0x80) != 0;
                if (current != value)
                {
                    if (value) enableFlag |= 0x80; else enableFlag &= 0x7F;
                    OnPropertyChanged(nameof(FourEnabled));
                }
            }
        }

        public byte unknown;

        private ushort _regionID;
        public ushort RegionID
        {
            get { return _regionID; }
            set
            {
                if (_regionID != value)
                {
                    _regionID = value;
                    OnPropertyChanged(nameof(RegionID));
                    OnPropertyChanged(nameof(DisplayRegionID));
                }
            }
        }

        public string DisplayRegionID
        {
            get
            {
                return _regionID.ToString();
            }
            set
            {
                if (ushort.TryParse(value, out ushort parsedID))
                {
                    RegionID = parsedID;
                }
            }
        }

        public int timestamp;

        public void DebugPrintInfo()
        {
            AppLogger.Debug(AppLogCategory.General, $"A Points: ({A.X}, {A.Y}, {A.Z})");
            AppLogger.Debug(AppLogCategory.General, $"B Points: ({B.X}, {B.Y}, {B.Z})");
            AppLogger.Debug(AppLogCategory.General, $"C Points: ({C.X}, {C.Y}, {C.Z})");
            AppLogger.Debug(AppLogCategory.General, $"D Points: ({D.X}, {D.Y}, {D.Z})");
            AppLogger.Debug(AppLogCategory.General, $"1 Points: ({One.X}, {One.Y}, {One.Z})");
            AppLogger.Debug(AppLogCategory.General, $"2 Points: ({Two.X}, {Two.Y}, {Two.Z})");
            AppLogger.Debug(AppLogCategory.General, $"3 Points: ({Three.X}, {Three.Y}, {Three.Z})");
            AppLogger.Debug(AppLogCategory.General, $"4 Points: ({Four.X}, {Four.Y}, {Four.Z})");

            AppLogger.Debug(AppLogCategory.General, $"Enable Flag: {enableFlag:X2}");
            AppLogger.Debug(AppLogCategory.General, $"Unknown: {unknown:X2}");
            AppLogger.Debug(AppLogCategory.General, $"Region ID: {RegionID}");
            AppLogger.Debug(AppLogCategory.General, $"Timestamp: {timestamp}");
        }
    }
}
