using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace FF14ConfigEditor.UISave
{
    // RegionID 数据来源: https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv

    /// <summary>
    /// `UISAVE.DAT`文件中 FMARKER 段的标点预设数据。
    /// </summary>
    public class SectionFMARKER : UISaveSection
    {
        public const int MarkerHeaderByteLength = 16;
        public const int WayMarkByteLength = 104;
        public const int MarkerTailByteLength = 4;

        public List<WayMark> WayMarks { get; private set; } = [];
        private byte[] _markerHeader = [];
        private byte[] _markerTail = [];

        public int MarkerTailLength => _markerTail.Length;

        public SectionFMARKER(
            short index,
            byte[] unknown1,
            int length,
            byte[] unknown2,
            byte[] data,
            byte[] endFlag) : base(index, unknown1, length, unknown2, data, endFlag)
        {
            ParseMarker();
        }

        public void ParseMarker()
        {
            ValidateMarkerDataLength();

            using MemoryStream ms = new(data);
            using BinaryReader reader = new(ms);

            byte[] markerHeader = UISaveBinaryReader.ReadExact(
                reader,
                MarkerHeaderByteLength,
                "FMARKER 标记头",
                index,
                UISaveOffsetOrigin.FMarkerSectionData);
            DebugHelper.Log($"Marker 标记头: {BitConverter.ToString(markerHeader)}");

            List<WayMark> parsedWayMarks = [];
            int wayMarkCount = GetWayMarkCount(data.Length);
            for (int count = 0; count < wayMarkCount; count++)
            {
                long startPos = ms.Position;
                WayMark wayMark = ParseWayMark(reader, index);
                parsedWayMarks.Add(wayMark);

                DebugHelper.Log($"WayMark Parsed #{count + 1} at offset {startPos}");
                DebugHelper.Log($"= = = = =");
                wayMark.DebugPrintInfo();
            }

            byte[] markerTail = UISaveBinaryReader.ReadExact(
                reader,
                MarkerTailByteLength,
                "FMARKER 标记尾部",
                index,
                UISaveOffsetOrigin.FMarkerSectionData);
            DebugHelper.Log($"Marker 尾部: {markerTail.Length} bytes");
            if (markerTail.Any(value => value != 0))
            {
                DebugHelper.LogWarning("FMARKER 标记尾部不是全零，已原样保留。");
            }

            _markerHeader = markerHeader;
            WayMarks = parsedWayMarks;
            _markerTail = markerTail;
        }

        public override byte[] ToRawBytes()
        {
            ValidateMarkerForSave();
            ValidateSectionFields();

            data = BuildMarkerDataForSave();
            length = data.Length;

            return base.ToRawBytes();
        }

        public override void ValidateForSave()
        {
            ValidateMarkerForSave();
            ValidateSectionFields();
        }

        private void ValidateMarkerForSave()
        {
            ValidateByteArray(_markerHeader, MarkerHeaderByteLength, "FMARKER 标记头", index);
            ValidateByteArray(_markerTail, MarkerTailByteLength, "FMARKER 标记尾部", index);

            if (WayMarks is null)
            {
                throw new UISaveFormatException(
                    "FMARKER 标点预设列表不能为空。",
                    sectionIndex: index,
                    fieldName: "FMARKER 标点预设列表");
            }

            for (int i = 0; i < WayMarks.Count; i++)
            {
                ValidateWayMarkForSave(WayMarks[i], i);
            }
        }

        private byte[] BuildMarkerDataForSave()
        {
            using MemoryStream ms = new();
            using BinaryWriter writer = new(ms);

            writer.Write(_markerHeader);

            for (int i = 0; i < WayMarks.Count; i++)
            {
                long positionBeforeWrite = ms.Position;
                WriteWayMark(writer, WayMarks[i]);
                long writtenLength = ms.Position - positionBeforeWrite;
                if (writtenLength != WayMarkByteLength)
                {
                    throw new UISaveFormatException(
                        $"FMARKER 第 {i + 1} 个标点预设写出长度不是 {WayMarkByteLength} 字节。",
                        sectionIndex: index,
                        expectedLength: WayMarkByteLength,
                        remainingLength: writtenLength,
                        fieldName: $"FMARKER 第 {i + 1} 个标点预设");
                }
            }

            writer.Write(_markerTail);
            return ms.ToArray();
        }

        private void ValidateWayMarkForSave(WayMark? wayMark, int wayMarkIndex)
        {
            if (wayMark is null)
            {
                throw new UISaveFormatException(
                    $"FMARKER 第 {wayMarkIndex + 1} 个标点预设不能为空。",
                    sectionIndex: index,
                    fieldName: $"FMARKER 第 {wayMarkIndex + 1} 个标点预设");
            }

            ValidateWayMarkPointForSave(wayMark.A, "A", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.B, "B", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.C, "C", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.D, "D", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.One, "1", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.Two, "2", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.Three, "3", wayMarkIndex);
            ValidateWayMarkPointForSave(wayMark.Four, "4", wayMarkIndex);
        }

        private void ValidateWayMarkPointForSave(WayMarkPoint? point, string pointName, int wayMarkIndex)
        {
            if (point is null)
            {
                throw new UISaveFormatException(
                    $"FMARKER 第 {wayMarkIndex + 1} 个标点预设的 {pointName} 点不能为空。",
                    sectionIndex: index,
                    fieldName: $"FMARKER 第 {wayMarkIndex + 1} 个标点预设的 {pointName} 点");
            }
        }

        private void ValidateMarkerDataLength()
        {
            if (data is null)
            {
                throw new UISaveFormatException(
                    "FMARKER 数据不能为空。",
                    sectionIndex: index,
                    fieldName: "FMARKER 数据");
            }

            int minimumLength = MarkerHeaderByteLength + MarkerTailByteLength;
            if (data.Length < minimumLength)
            {
                throw new UISaveFormatException(
                    $"FMARKER 数据长度不能小于 {minimumLength} 字节。",
                    sectionIndex: index,
                    expectedLength: minimumLength,
                    remainingLength: data.Length,
                    fieldName: "FMARKER 数据");
            }

            int wayMarkBytesLength = data.Length - minimumLength;
            if (wayMarkBytesLength % WayMarkByteLength != 0)
            {
                throw new UISaveFormatException(
                    "FMARKER 数据长度不符合已知结构，应为 16 字节头、若干个 104 字节标点预设和 4 字节尾部。",
                    sectionIndex: index,
                    expectedLength: WayMarkByteLength,
                    remainingLength: wayMarkBytesLength,
                    fieldName: "FMARKER 数据");
            }
        }

        private static int GetWayMarkCount(int markerDataLength)
        {
            return (markerDataLength - MarkerHeaderByteLength - MarkerTailByteLength) / WayMarkByteLength;
        }

        private static void WriteWayMark(BinaryWriter writer, WayMark wayMark)
        {
            WriteRawPoint(writer, wayMark.A);
            WriteRawPoint(writer, wayMark.B);
            WriteRawPoint(writer, wayMark.C);
            WriteRawPoint(writer, wayMark.D);
            WriteRawPoint(writer, wayMark.One);
            WriteRawPoint(writer, wayMark.Two);
            WriteRawPoint(writer, wayMark.Three);
            WriteRawPoint(writer, wayMark.Four);

            writer.Write(wayMark.enableFlag);
            writer.Write(wayMark.unknown);
            writer.Write(wayMark.RegionID);
            writer.Write(wayMark.timestamp);
        }

        private static void WriteRawPoint(BinaryWriter writer, WayMarkPoint point)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }

        private static WayMark ParseWayMark(BinaryReader reader, int sectionIndex)
        {
            WayMark wayMark = new()
            {
                A = ReadRawPoint(reader, sectionIndex),
                B = ReadRawPoint(reader, sectionIndex),
                C = ReadRawPoint(reader, sectionIndex),
                D = ReadRawPoint(reader, sectionIndex),
                One = ReadRawPoint(reader, sectionIndex),
                Two = ReadRawPoint(reader, sectionIndex),
                Three = ReadRawPoint(reader, sectionIndex),
                Four = ReadRawPoint(reader, sectionIndex),

                enableFlag = UISaveBinaryReader.ReadByte(
                    reader,
                    "FMARKER 启用标记",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData),
                unknown = UISaveBinaryReader.ReadByte(
                    reader,
                    "FMARKER 未知字节",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData),
                RegionID = UISaveBinaryReader.ReadUInt16(
                    reader,
                    "FMARKER 区域 ID",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData),
                timestamp = UISaveBinaryReader.ReadInt32(
                    reader,
                    "FMARKER 时间戳",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData)
            };

            return wayMark;
        }

        private static WayMarkPoint ReadRawPoint(BinaryReader reader, int sectionIndex)
        {
            return new WayMarkPoint
            {
                X = UISaveBinaryReader.ReadInt32(
                    reader,
                    "FMARKER 坐标 X",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData),
                Y = UISaveBinaryReader.ReadInt32(
                    reader,
                    "FMARKER 坐标 Y",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData),
                Z = UISaveBinaryReader.ReadInt32(
                    reader,
                    "FMARKER 坐标 Z",
                    sectionIndex,
                    UISaveOffsetOrigin.FMarkerSectionData)
            };
        }
    }

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
            DebugHelper.Log($"A Points: ({A.X}, {A.Y}, {A.Z})");
            DebugHelper.Log($"B Points: ({B.X}, {B.Y}, {B.Z})");
            DebugHelper.Log($"C Points: ({C.X}, {C.Y}, {C.Z})");
            DebugHelper.Log($"D Points: ({D.X}, {D.Y}, {D.Z})");
            DebugHelper.Log($"1 Points: ({One.X}, {One.Y}, {One.Z})");
            DebugHelper.Log($"2 Points: ({Two.X}, {Two.Y}, {Two.Z})");
            DebugHelper.Log($"3 Points: ({Three.X}, {Three.Y}, {Three.Z})");
            DebugHelper.Log($"4 Points: ({Four.X}, {Four.Y}, {Four.Z})");

            DebugHelper.Log($"Enable Flag: {enableFlag:X2}");
            DebugHelper.Log($"Unknown: {unknown:X2}");
            DebugHelper.Log($"Region ID: {RegionID}");
            DebugHelper.Log($"Timestamp: {timestamp}");
        }
    }

    /// <summary>
    /// 每一个标点的坐标数据结构。
    /// </summary>
    public class WayMarkPoint : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // 游戏内显示时坐标会换算为浮点数，文件中以乘以 1000 后的整数保存。
        private int rawX = 0;
        private int rawY = 0;
        private int rawZ = 0;

        public int X
        {
            get { return rawX; }
            set
            {
                if (rawX != value)
                {
                    rawX = value;
                    OnPropertyChanged(nameof(X));
                    OnPropertyChanged(nameof(FloatX));
                }
            }
        }
        public int Y
        {
            get { return rawY; }
            set
            {
                if (rawY != value)
                {
                    rawY = value;
                    OnPropertyChanged(nameof(Y));
                    OnPropertyChanged(nameof(FloatY));
                }
            }
        }
        public int Z
        {
            get { return rawZ; }
            set
            {
                if (rawZ != value)
                {
                    rawZ = value;
                    OnPropertyChanged(nameof(Z));
                    OnPropertyChanged(nameof(FloatZ));
                }
            }
        }

        public float FloatX
        {
            get { return rawX / 1000f; }
            set
            {
                int newVal = (int)(value * 1000);
                if (rawX != newVal)
                {
                    rawX = newVal;
                    OnPropertyChanged(nameof(X));
                    OnPropertyChanged(nameof(FloatX));
                }
            }
        }
        public float FloatY
        {
            get { return rawY / 1000f; }
            set
            {
                int newVal = (int)(value * 1000);
                if (rawY != newVal)
                {
                    rawY = newVal;
                    OnPropertyChanged(nameof(Y));
                    OnPropertyChanged(nameof(FloatY));
                }
            }
        }
        public float FloatZ
        {
            get { return rawZ / 1000f; }
            set
            {
                int newVal = (int)(value * 1000);
                if (rawZ != newVal)
                {
                    rawZ = newVal;
                    OnPropertyChanged(nameof(Z));
                    OnPropertyChanged(nameof(FloatZ));
                }
            }
        }
    }
}
