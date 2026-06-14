using System;
using System.Collections.Generic;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// UISAVE.DAT 内容无法解析或无法安全序列化时抛出的格式异常。
    /// </summary>
    public sealed class UISaveFormatException : Exception
    {
        public long? Offset { get; }
        public int? SectionIndex { get; }
        public long? ExpectedLength { get; }
        public long? RemainingLength { get; }

        public UISaveFormatException(
            string message,
            long? offset = null,
            int? sectionIndex = null,
            long? expectedLength = null,
            long? remainingLength = null,
            Exception? innerException = null)
            : base(BuildMessage(message, offset, sectionIndex, expectedLength, remainingLength), innerException)
        {
            Offset = offset;
            SectionIndex = sectionIndex;
            ExpectedLength = expectedLength;
            RemainingLength = remainingLength;
        }

        private static string BuildMessage(
            string message,
            long? offset,
            int? sectionIndex,
            long? expectedLength,
            long? remainingLength)
        {
            List<string> details = [];
            if (offset.HasValue) details.Add($"偏移={offset.Value}");
            if (sectionIndex.HasValue) details.Add($"段={sectionIndex.Value}");
            if (expectedLength.HasValue) details.Add($"期望长度={expectedLength.Value}");
            if (remainingLength.HasValue) details.Add($"剩余长度={remainingLength.Value}");

            return details.Count == 0
                ? message
                : $"{message} ({string.Join(", ", details)})";
        }
    }
}
