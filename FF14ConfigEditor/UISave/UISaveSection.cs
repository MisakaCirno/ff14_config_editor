using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// `UISAVE.DAT`文件中存储的Section的数据结构。
    /// </summary>
    public class UISaveSection
    {
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
            this.unknown1 = unknown1;
            this.length = length;
            this.unknown2 = unknown2;
            this.data = data;
            this.endFlag = endFlag;

            if (length != data.Length)
            {
                throw new ArgumentException("Section length does not match data length.");
            }
        }

        public virtual byte[] ToRawBytes()
        {
            List<byte> rawBytes =
            [
                .. BitConverter.GetBytes(index),
                .. unknown1,
                .. BitConverter.GetBytes(data.Length),
                .. unknown2,
                .. data,
                .. endFlag,
            ];

            return [.. rawBytes];
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
