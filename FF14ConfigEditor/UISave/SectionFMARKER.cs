using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF14ConfigEditor.UISave
{
    // regionID: https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv

    /// <summary>
    /// `UISAVE.DAT`文件中存储的Section的数据结构中，场景标点部分的详细数据结构。
    /// </summary>
    public class SectionFMARKER : UISaveSection
    {
        // 游戏里最多设置30个
        public List<WayMark> WayMarks { get; private set; } = [];
        private byte[] _markerHeader = [];
        private byte[] _markerTail = [];

        public SectionFMARKER(
            short index,
            byte[] unknown1,
            int length,
            byte[] unknown2,
            byte[] data,
            byte[] endFlag) : base(index, unknown1, length, unknown2, data, endFlag)
        {
            // 解析标记数据
            ParseMarker();
        }

        public void ParseMarker()
        {
            WayMarks.Clear();

            // 使用 MemoryStream 和 BinaryReader 优化读取
            using MemoryStream ms = new(data);
            using BinaryReader reader = new(ms);

            // 16 bytes unknown header
            _markerHeader = reader.ReadBytes(16);
            DebugHelper.Log($"Marker Unknown1: {BitConverter.ToString(_markerHeader)}");

            int count = 0;
            // 接下来每一段都是一个WayMark结构
            // 每一个WayMark结构通常大小为 104 字节 (8个标记点 * 12字节 + 8字节元数据)
            while (ms.Position < ms.Length)
            {
                // 防止读取越界，检查剩余长度是否足够解析一个完整结构
                // 标准结构包含 8 个坐标点 (A,B,C,D,1,2,3,4) = 96 字节
                // 加上后续元数据，至少需要 104 字节
                if (ms.Length - ms.Position < 104) break;

                long startPos = ms.Position;
                WayMark wayMark = ParseWayMark(reader);
                WayMarks.Add(wayMark);

                DebugHelper.Log($"WayMark Parsed #{++count} at offset {startPos}");
                DebugHelper.Log($"= = = = =");
                wayMark.DebugPrintInfo();
            }

            // 读取可能的尾部填充
            if (ms.Position < ms.Length)
            {
                _markerTail = reader.ReadBytes((int)(ms.Length - ms.Position));
                DebugHelper.Log($"Marker Tail: {_markerTail.Length} bytes");
            }
        }

        public override byte[] ToRawBytes()
        {
            using MemoryStream ms = new();
            using BinaryWriter writer = new(ms);

            // 写入头部 (16 bytes)
            if (_markerHeader.Length < 16) Array.Resize(ref _markerHeader, 16);
            writer.Write(_markerHeader);

            // 写入 WayMarks
            foreach (WayMark wayMark in WayMarks)
            {
                WriteWayMark(writer, wayMark);
            }

            // 写入尾部
            if (_markerTail.Length > 0)
            {
                writer.Write(_markerTail);
            }

            // 更新基类数据用于最终序列化
            data = ms.ToArray();
            length = data.Length;

            return base.ToRawBytes();
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
            writer.Write(wayMark.regionID);
            writer.Write(wayMark.timestamp);
        }

        private static void WriteRawPoint(BinaryWriter writer, WayMarkPoint point)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }

        private static WayMark ParseWayMark(BinaryReader reader)
        {
            WayMark wayMark = new()
            {
                // 解析坐标点 (A, B, C, D, 1, 2, 3, 4)
                // 每个点 3 个 int32 (X, Y, Z) = 12 字节
                A = ReadRawPoint(reader),
                B = ReadRawPoint(reader),
                C = ReadRawPoint(reader),
                D = ReadRawPoint(reader),
                One = ReadRawPoint(reader),
                Two = ReadRawPoint(reader),
                Three = ReadRawPoint(reader),
                Four = ReadRawPoint(reader),

                // 解析标志位和其他信息 (Offset 96)
                enableFlag = reader.ReadByte(),     // Offset 96
                unknown = reader.ReadByte(),        // Offset 97
                regionID = reader.ReadUInt16(),     // Offset 98
                timestamp = reader.ReadInt32()     // Offset 100
            };

            return wayMark;
        }

        private static WayMarkPoint ReadRawPoint(BinaryReader reader)
        {
            return new WayMarkPoint
            {
                X = reader.ReadInt32(),
                Y = reader.ReadInt32(),
                Z = reader.ReadInt32()
            };
        }
    }

    /// <summary>
    /// 每一个标点预设的数据结构
    /// </summary>
    public class WayMark
    {
        public WayMarkPoint A { get; set; } = new();
        public WayMarkPoint B { get; set; } = new();
        public WayMarkPoint C { get; set; } = new();
        public WayMarkPoint D { get; set; } = new();
        public WayMarkPoint One { get; set; } = new();
        public WayMarkPoint Two { get; set; } = new();
        public WayMarkPoint Three { get; set; } = new();
        public WayMarkPoint Four { get; set; } = new();

        // enableFlag共8位，每一位代表一个标点的启用状态
        // 位0代表A，位1代表B，位6代表3，位7代表4
        public byte enableFlag;
        public bool AEnabled
        {
            get { return (enableFlag & 0x01) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x01;
                else
                    enableFlag &= 0xFE;
            }
        }
        public bool BEnabled
        {
            get { return (enableFlag & 0x02) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x02;
                else
                    enableFlag &= 0xFD;
            }
        }
        public bool CEnabled
        {
            get { return (enableFlag & 0x04) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x04;
                else
                    enableFlag &= 0xFB;
            }
        }
        public bool DEnabled
        {
            get { return (enableFlag & 0x08) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x08;
                else
                    enableFlag &= 0xF7;
            }
        }
        public bool OneEnabled
        {
            get { return (enableFlag & 0x10) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x10;
                else
                    enableFlag &= 0xEF;
            }
        }
        public bool TwoEnabled
        {
            get { return (enableFlag & 0x20) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x20;
                else
                    enableFlag &= 0xDF;
            }
        }
        public bool ThreeEnabled
        {
            get { return (enableFlag & 0x40) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x40;
                else
                    enableFlag &= 0xBF;
            }
        }
        public bool FourEnabled
        {
            get { return (enableFlag & 0x80) != 0; }
            set
            {
                if (value)
                    enableFlag |= 0x80;
                else
                    enableFlag &= 0x7F;
            }
        }


        public byte unknown;

        public ushort regionID;
        public string DisplayRegionID
        {
            get
            {
                return regionID.ToString();
            }
            set
            {
                if (ushort.TryParse(value, out ushort parsedID))
                {
                    regionID = parsedID;
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
            DebugHelper.Log($"Region ID: {regionID}");
            DebugHelper.Log($"Timestamp: {timestamp}");
        }
    }

    /// <summary>
    /// 每一个标点的坐标数据结构
    /// </summary>
    public class WayMarkPoint
    {
        // 使用时坐标会转换为浮点数，但存储时是整数形式
        // 转换比例为 1000
        private int rawX = 0;
        private int rawY = 0;
        private int rawZ = 0;

        public int X
        {
            get { return rawX; }
            set { rawX = value; }
        }
        public int Y
        {
            get { return rawY; }
            set { rawY = value; }
        }
        public int Z
        {
            get { return rawZ; }
            set { rawZ = value; }
        }

        public float FloatX
        {
            get { return rawX / 1000f; }
            set { rawX = (int)(value * 1000); }
        }
        public float FloatY
        {
            get { return rawY / 1000f; }
            set { rawY = (int)(value * 1000); }
        }
        public float FloatZ
        {
            get { return rawZ / 1000f; }
            set { rawZ = (int)(value * 1000); }
        }
    }
}