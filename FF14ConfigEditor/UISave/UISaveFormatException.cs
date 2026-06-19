using System;
using System.Collections.Generic;

namespace FF14ConfigEditor.UISave
{
    /// <summary>
    /// UISAVE.DAT 内容无法解析或无法安全序列化时抛出的格式异常。
    /// </summary>
    public sealed class UISaveFormatException : Exception
    {
        public string? FieldName { get; }
        public string? OffsetOrigin { get; }
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
            string? fieldName = null,
            string? offsetOrigin = null,
            Exception? innerException = null)
            : base(BuildMessage(message, fieldName, offsetOrigin, offset, sectionIndex, expectedLength, remainingLength), innerException)
        {
            FieldName = NormalizeDetail(fieldName);
            OffsetOrigin = NormalizeDetail(offsetOrigin);
            Offset = offset;
            SectionIndex = sectionIndex;
            ExpectedLength = expectedLength;
            RemainingLength = remainingLength;
        }

        private static string BuildMessage(
            string message,
            string? fieldName,
            string? offsetOrigin,
            long? offset,
            int? sectionIndex,
            long? expectedLength,
            long? remainingLength)
        {
            List<string> details = [];
            string? normalizedFieldName = NormalizeDetail(fieldName);
            string? normalizedOffsetOrigin = NormalizeDetail(offsetOrigin);
            if (normalizedFieldName is not null) details.Add($"字段={normalizedFieldName}");
            if (normalizedOffsetOrigin is not null) details.Add($"偏移来源={normalizedOffsetOrigin}");
            if (offset.HasValue) details.Add($"偏移={offset.Value}");
            if (sectionIndex.HasValue) details.Add($"段={sectionIndex.Value}");
            if (expectedLength.HasValue) details.Add($"期望长度={expectedLength.Value}");
            if (remainingLength.HasValue) details.Add($"剩余长度={remainingLength.Value}");

            return details.Count == 0
                ? message
                : $"{message} ({string.Join(", ", details)})";
        }

        private static string? NormalizeDetail(string? detail)
        {
            return string.IsNullOrWhiteSpace(detail) ? null : detail;
        }
    }
}
