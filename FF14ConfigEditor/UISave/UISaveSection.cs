using System;
using System.IO;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// `UISAVE.DAT`文件中存储的段数据结构。
    /// </summary>
    public class UISaveSection
    {
        public const int Unknown1ByteLength = 6;
        public const int Unknown2ByteLength = 4;
        public const int EndFlagByteLength = 4;

        public short index = -1;
        public byte[] unknown1 = [];
        public int length = 0;
        public byte[] unknown2 = [];
        public byte[] data = [];
        public byte[] endFlag = [];

        public UISaveSection(
            short index,
            byte[] unknown1,
            int length,
            byte[] unknown2,
            byte[] data,
            byte[] endFlag)
        {
            this.index = index;
            this.unknown1 = unknown1 ?? throw new ArgumentNullException(nameof(unknown1));
            this.length = length;
            this.unknown2 = unknown2 ?? throw new ArgumentNullException(nameof(unknown2));
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.endFlag = endFlag ?? throw new ArgumentNullException(nameof(endFlag));

            ValidateStructure();
        }

        public virtual byte[] ToRawBytes()
        {
            ValidateForSave();

            using MemoryStream ms = new();
            using BinaryWriter writer = new(ms);

            writer.Write(index);
            writer.Write(unknown1);
            writer.Write(length);
            writer.Write(unknown2);
            writer.Write(data);
            writer.Write(endFlag);

            return ms.ToArray();
        }

        public virtual void ValidateForSave()
        {
            ValidateStructure();
        }

        protected void ValidateStructure()
        {
            ValidateByteArray(unknown1, Unknown1ByteLength, "段 unknown1", index);
            ValidateByteArray(unknown2, Unknown2ByteLength, "段 unknown2", index);
            ValidateByteArray(endFlag, EndFlagByteLength, "段结束标记", index);

            if (length < 0)
            {
                throw new UISaveFormatException(
                    "段长度不能为负数。",
                    sectionIndex: index,
                    expectedLength: 0);
            }

            if (length != data.Length)
            {
                throw new UISaveFormatException(
                    "段长度与数据长度不一致。",
                    sectionIndex: index,
                    expectedLength: length,
                    remainingLength: data.Length);
            }
        }

        protected static void ValidateByteArray(
            byte[] value,
            int expectedLength,
            string fieldName,
            int? sectionIndex = null)
        {
            if (value.Length != expectedLength)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 必须正好是 {expectedLength} 字节。",
                    sectionIndex: sectionIndex,
                    expectedLength: expectedLength,
                    remainingLength: value.Length);
            }
        }

        public void DebugPrintInfo()
        {
            DebugHelper.Log($"Section Index: {index}");
            DebugHelper.Log($"Section Unknown1: {BitConverter.ToString(unknown1)}");
            DebugHelper.Log($"Section Length: {length}");
            DebugHelper.Log($"Section Unknown2: {BitConverter.ToString(unknown2)}");
            DebugHelper.Log($"Section Data: {BitConverter.ToString(data)}");
            DebugHelper.Log($"Section End Flag: {BitConverter.ToString(endFlag)}");
        }
    }
}
