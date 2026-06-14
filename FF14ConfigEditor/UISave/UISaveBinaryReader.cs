using System;
using System.IO;

namespace FF14ConfigEditor.UISave
{
    internal static class UISaveBinaryReader
    {
        public static byte[] ReadExact(
            BinaryReader reader,
            int byteCount,
            string fieldName,
            int? sectionIndex = null)
        {
            if (byteCount < 0)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 的读取长度不能为负数。",
                    offset: GetOffset(reader),
                    sectionIndex: sectionIndex,
                    expectedLength: byteCount);
            }

            long offset = GetOffset(reader);
            long? remaining = GetRemaining(reader);
            if (remaining.HasValue && remaining.Value < byteCount)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 数据被截断。",
                    offset: offset,
                    sectionIndex: sectionIndex,
                    expectedLength: byteCount,
                    remainingLength: remaining.Value);
            }

            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 数据被截断。",
                    offset: offset,
                    sectionIndex: sectionIndex,
                    expectedLength: byteCount,
                    remainingLength: bytes.Length);
            }

            return bytes;
        }

        public static void EnsureRemaining(
            BinaryReader reader,
            long byteCount,
            string fieldName,
            int? sectionIndex = null)
        {
            if (byteCount < 0)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 的长度不能为负数。",
                    offset: GetOffset(reader),
                    sectionIndex: sectionIndex,
                    expectedLength: byteCount);
            }

            long? remaining = GetRemaining(reader);
            if (remaining.HasValue && remaining.Value < byteCount)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 超出剩余数据长度。",
                    offset: GetOffset(reader),
                    sectionIndex: sectionIndex,
                    expectedLength: byteCount,
                    remainingLength: remaining.Value);
            }
        }

        public static byte ReadByte(BinaryReader reader, string fieldName, int? sectionIndex = null)
        {
            return ReadExact(reader, sizeof(byte), fieldName, sectionIndex)[0];
        }

        public static short ReadInt16(BinaryReader reader, string fieldName, int? sectionIndex = null)
        {
            return BitConverter.ToInt16(ReadExact(reader, sizeof(short), fieldName, sectionIndex), 0);
        }

        public static ushort ReadUInt16(BinaryReader reader, string fieldName, int? sectionIndex = null)
        {
            return BitConverter.ToUInt16(ReadExact(reader, sizeof(ushort), fieldName, sectionIndex), 0);
        }

        public static int ReadInt32(BinaryReader reader, string fieldName, int? sectionIndex = null)
        {
            return BitConverter.ToInt32(ReadExact(reader, sizeof(int), fieldName, sectionIndex), 0);
        }

        public static long GetOffset(BinaryReader reader)
        {
            return reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
        }

        public static long? GetRemaining(BinaryReader reader)
        {
            if (!reader.BaseStream.CanSeek) return null;
            return reader.BaseStream.Length - reader.BaseStream.Position;
        }
    }
}
