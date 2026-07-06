using System;
using System.Collections.Generic;
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

        internal void ParseMarker()
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
            AppLogger.Debug(AppLogCategory.General, $"Marker 标记头: {BitConverter.ToString(markerHeader)}");

            List<WayMark> parsedWayMarks = [];
            int wayMarkCount = GetWayMarkCount(data.Length);
            for (int count = 0; count < wayMarkCount; count++)
            {
                long startPos = ms.Position;
                WayMark wayMark = ParseWayMark(reader, index);
                parsedWayMarks.Add(wayMark);

                AppLogger.Debug(AppLogCategory.General, $"WayMark Parsed #{count + 1} at offset {startPos}");
                AppLogger.Debug(AppLogCategory.General, "= = = = =");
                wayMark.DebugPrintInfo();
            }

            byte[] markerTail = UISaveBinaryReader.ReadExact(
                reader,
                MarkerTailByteLength,
                "FMARKER 标记尾部",
                index,
                UISaveOffsetOrigin.FMarkerSectionData);
            AppLogger.Debug(AppLogCategory.General, $"Marker 尾部: {markerTail.Length} bytes");
            if (markerTail.Any(value => value != 0))
            {
                AppLogger.Warning(AppLogCategory.UISaveWarning, "FMARKER 标记尾部不是全零，已原样保留。");
            }

            _markerHeader = markerHeader;
            WayMarks = parsedWayMarks;
            _markerTail = markerTail;
        }

        public override byte[] ToRawBytes()
        {
            // 纯读取：只返回当前编辑状态对应的字节，不改 length/data。
            // 内存状态（length/data）的提交由 Save() 经 PrepareSave() + 落盘后 CommitRawState() 完成，
            // 保证落盘失败时不会半提交。基类 ToRawBytes() 契约即纯读取，这里保持一致。
            PreparedSave preparedSave = PrepareSave();
            return preparedSave.RawBytes;
        }

        internal override PreparedSave PrepareSave()
        {
            ValidateMarkerForSave();
            ValidateSectionFields();

            byte[] markerData = BuildMarkerDataForSave();
            int markerLength = markerData.Length;
            byte[] rawBytes = BuildRawBytes(markerLength, markerData);

            return new PreparedSave(rawBytes, () => CommitRawState(markerLength, markerData));
        }

        public override void ValidateForSave()
        {
            ValidateMarkerForSave();
            ValidateSectionFields();
        }

        private void CommitRawState(int markerLength, byte[] markerData)
        {
            length = markerLength;
            data = markerData;
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

}
