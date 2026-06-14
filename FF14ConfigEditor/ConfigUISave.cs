using FF14ConfigEditor.UISave;
using System;
using System.Collections.Generic;
using System.IO;

namespace FF14ConfigEditor
{
    /// <summary>
    /// 针对`UISAVE.DAT`文件操作的包装类。
    /// </summary>
    /// <param name="filePath"></param>
    public class ConfigUISave: ConfigBase
    {
        /*
         * 文件解析参考:
         * https://github.com/PunishedPineapple/UISAVE_Reader
         * https://github.com/Lujiang0111/FFxivUisaveParser
         * section 名称映射参考:
         * https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/UiSavePackModule.cs
         */

        private const int FileFormatVersionByteLength = 8;
        private const int FileUnknownByteLength = 4;
        private const int PayloadUnknownByteLength = 8;
        private const int UserIdByteLength = 8;
        private const int SectionIndexByteLength = 2;

        // 写回文件的时候需要用到
        // 没加密的部分
        private byte[] fileFormatVersionRaw = [];
        private byte[] fileUnknownRaw = [];
        private byte[] fileTailRaw = []; // 保存加密数据之后的文件尾部
        // 加密了的部分
        private byte[] payloadUnknownRaw = [];
        private byte[] userIDRaw = [];
        private byte[] payloadTailRaw = []; // 保存解密数据末尾的填充

        /// <summary>
        /// 解析出的段列表
        /// </summary>
        public List<UISaveSection> Sections { get; private set; } = [];

        public string UserIDHex => FormatUserIdHex(userIDRaw);

        public string UserIDRawBytesHex => BitConverter.ToString(userIDRaw);

        public static bool TryGetSectionName(int sectionIndex, out string sectionName)
        {
            if (SectionFunctionMap.TryGetValue(sectionIndex, out string? name))
            {
                sectionName = name;
                return true;
            }

            sectionName = string.Empty;
            return false;
        }

        public SectionFMARKER? Marks
        {
            get
            {
                // 根据段的 index 字段查找 FMARKER (index == 17)，避免直接按列表下标访问
                foreach (UISaveSection s in Sections)
                {
                    if (s is SectionFMARKER fm && fm.index == 17)
                        return fm;
                }
                return null;
            }
        }

        public ConfigUISave(string filePath) : base(filePath)
        {
            Load();
        }

        public override void Load()
        {
            ParsedUISaveFile parsedFile = ParseFile(FilePath);
            ApplyParsedFile(parsedFile);
        }

        public override void Save()
        {
            ValidateEnvelopeForSave();

            byte[] encryptedData;
            using (MemoryStream ms = new())
            {
                using (BinaryWriter writer = new(ms))
                {
                    writer.Write(payloadUnknownRaw);
                    writer.Write(userIDRaw);

                    foreach (UISaveSection section in Sections)
                    {
                        writer.Write(section.ToRawBytes());
                    }

                    if (payloadTailRaw.Length > 0)
                    {
                        writer.Write(payloadTailRaw);
                    }
                }

                byte[] decryptedData = ms.ToArray();
                encryptedData = Utils.EncryptData(decryptedData);
            }

            byte[] fileBytes;
            using (MemoryStream ms = new())
            {
                using (BinaryWriter writer = new(ms))
                {
                    writer.Write(fileFormatVersionRaw);
                    writer.Write(encryptedData.Length);
                    writer.Write(fileUnknownRaw);
                    writer.Write(encryptedData);
                    if (fileTailRaw.Length > 0)
                    {
                        writer.Write(fileTailRaw);
                    }
                }

                fileBytes = ms.ToArray();
            }

            SafeFileWriter.WriteAllBytes(FilePath, fileBytes);
        }

        /// <summary>
        /// 解析加密后的数据内容。
        /// </summary>
        /// <param name="decryptedData"></param>
        public void ParseEncryptedPart(byte[] decryptedData)
        {
            ParsedEncryptedPayload parsedPayload = ParseEncryptedPayload(decryptedData);
            ApplyParsedFile(new ParsedUISaveFile(
                fileFormatVersionRaw,
                fileUnknownRaw,
                fileTailRaw,
                parsedPayload.PayloadUnknownRaw,
                parsedPayload.UserIDRaw,
                parsedPayload.Sections,
                parsedPayload.PayloadTailRaw));
        }

        private static ParsedUISaveFile ParseFile(string filePath)
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);

            byte[] fileFormatVersionRaw = UISaveBinaryReader.ReadExact(
                reader,
                FileFormatVersionByteLength,
                "文件格式版本",
                offsetOrigin: UISaveOffsetOrigin.File);
            DebugHelper.Log($"文件格式版本: {BitConverter.ToString(fileFormatVersionRaw)}");

            long encryptedLengthOffset = UISaveBinaryReader.GetOffset(reader);
            int encryptLength = UISaveBinaryReader.ReadInt32(
                reader,
                "加密数据长度",
                offsetOrigin: UISaveOffsetOrigin.File);
            DebugHelper.Log($"加密数据长度: {encryptLength}");
            if (encryptLength < 0)
            {
                throw new UISaveFormatException(
                    $"加密数据长度不能为负数：{encryptLength}。",
                    offset: encryptedLengthOffset,
                    expectedLength: 0,
                    fieldName: "加密数据长度",
                    offsetOrigin: UISaveOffsetOrigin.File);
            }

            byte[] fileUnknownRaw = UISaveBinaryReader.ReadExact(
                reader,
                FileUnknownByteLength,
                "文件未知头部",
                offsetOrigin: UISaveOffsetOrigin.File);
            DebugHelper.Log($"未知头部: {BitConverter.ToString(fileUnknownRaw)}");

            UISaveBinaryReader.EnsureRemaining(
                reader,
                encryptLength,
                "加密数据",
                offsetOrigin: UISaveOffsetOrigin.File);
            byte[] encryptedData = UISaveBinaryReader.ReadExact(
                reader,
                encryptLength,
                "加密数据",
                offsetOrigin: UISaveOffsetOrigin.File);
            byte[] fileTailRaw = ReadRemainingFileTail(reader);
            byte[] decryptedData = Utils.DecryptData(encryptedData);

            ParsedEncryptedPayload parsedPayload = ParseEncryptedPayload(decryptedData);
            return new ParsedUISaveFile(
                fileFormatVersionRaw,
                fileUnknownRaw,
                fileTailRaw,
                parsedPayload.PayloadUnknownRaw,
                parsedPayload.UserIDRaw,
                parsedPayload.Sections,
                parsedPayload.PayloadTailRaw);
        }

        private static ParsedEncryptedPayload ParseEncryptedPayload(byte[] decryptedData)
        {
            ArgumentNullException.ThrowIfNull(decryptedData);

            using MemoryStream ms = new(decryptedData);
            using BinaryReader reader = new(ms);

            byte[] payloadUnknownRaw = UISaveBinaryReader.ReadExact(
                reader,
                PayloadUnknownByteLength,
                "加密部分未知头",
                offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
            DebugHelper.Log($"加密部分 - 未知: {BitConverter.ToString(payloadUnknownRaw)}");

            byte[] userIDRaw = UISaveBinaryReader.ReadExact(
                reader,
                UserIdByteLength,
                "加密部分用户 ID",
                offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
            DebugHelper.Log($"加密部分 - 用户ID: {FormatUserIdHex(userIDRaw)}");

            List<UISaveSection> sections = [];
            byte[] payloadTailRaw = [];

            while (ms.Position < ms.Length)
            {
                long sectionStartOffset = ms.Position;
                long remaining = ms.Length - ms.Position;
                if (remaining < SectionIndexByteLength)
                {
                    payloadTailRaw = UISaveBinaryReader.ReadExact(
                        reader,
                        checked((int)remaining),
                        "加密部分尾部填充",
                        offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
                    DebugHelper.LogUnknownPreserved($"解密数据末尾存在 {payloadTailRaw.Length} 字节尾部填充，已原样保留。");
                    break;
                }

                short index = UISaveBinaryReader.ReadInt16(
                    reader,
                    "段索引",
                    offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
                byte[] sectionUnknown1 = UISaveBinaryReader.ReadExact(
                    reader,
                    UISaveSection.Unknown1ByteLength,
                    "段未知字段 1",
                    index,
                    UISaveOffsetOrigin.DecryptedPayload);

                long sectionLengthOffset = ms.Position;
                int sectionLength = UISaveBinaryReader.ReadInt32(
                    reader,
                    "段长度",
                    index,
                    UISaveOffsetOrigin.DecryptedPayload);
                if (sectionLength < 0)
                {
                    throw new UISaveFormatException(
                        $"段长度不能为负数：{sectionLength}。",
                        offset: sectionLengthOffset,
                        sectionIndex: index,
                        expectedLength: 0,
                        fieldName: "段长度",
                        offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
                }

                byte[] sectionUnknown2 = UISaveBinaryReader.ReadExact(
                    reader,
                    UISaveSection.Unknown2ByteLength,
                    "段未知字段 2",
                    index,
                    UISaveOffsetOrigin.DecryptedPayload);

                long bytesNeeded = (long)sectionLength + UISaveSection.EndFlagByteLength;
                long bytesRemaining = ms.Length - ms.Position;
                if (bytesNeeded > bytesRemaining)
                {
                    throw new UISaveFormatException(
                        "段数据或结束标记被截断。",
                        offset: sectionStartOffset,
                        sectionIndex: index,
                        expectedLength: bytesNeeded,
                        remainingLength: bytesRemaining,
                        fieldName: "段数据和结束标记",
                        offsetOrigin: UISaveOffsetOrigin.DecryptedPayload);
                }

                byte[] sectionData = UISaveBinaryReader.ReadExact(
                    reader,
                    sectionLength,
                    "段数据",
                    index,
                    UISaveOffsetOrigin.DecryptedPayload);
                byte[] endFlag = UISaveBinaryReader.ReadExact(
                    reader,
                    UISaveSection.EndFlagByteLength,
                    "段结束标记",
                    index,
                    UISaveOffsetOrigin.DecryptedPayload);
                if (endFlag.Any(value => value != 0))
                {
                    DebugHelper.LogWarning($"段 {index} 的结束标记不是全零，已原样保留。");
                }

                UISaveSection section = CreateSection(
                    index,
                    sectionUnknown1,
                    sectionLength,
                    sectionUnknown2,
                    sectionData,
                    endFlag);
                sections.Add(section);

                DebugHelper.Log($"- - - - -");
                if (TryGetSectionName(index, out string name))
                {
                    DebugHelper.Log($"Section Name: {name}");
                }
                else
                {
                    DebugHelper.LogUnknownPreserved($"发现未知 UISAVE 段 index={index}，已按原始数据保留。");
                }
                section.DebugPrintInfo();
            }

            return new ParsedEncryptedPayload(payloadUnknownRaw, userIDRaw, sections, payloadTailRaw);
        }

        private void ApplyParsedFile(ParsedUISaveFile parsedFile)
        {
            fileFormatVersionRaw = parsedFile.FileFormatVersionRaw;
            fileUnknownRaw = parsedFile.FileUnknownRaw;
            fileTailRaw = parsedFile.FileTailRaw;
            payloadUnknownRaw = parsedFile.PayloadUnknownRaw;
            userIDRaw = parsedFile.UserIDRaw;
            Sections = parsedFile.Sections;
            payloadTailRaw = parsedFile.PayloadTailRaw;
        }

        private static UISaveSection CreateSection(
            short index,
            byte[] sectionUnknown1,
            int sectionLength,
            byte[] sectionUnknown2,
            byte[] sectionData,
            byte[] endFlag)
        {
            return index == 17
                ? new SectionFMARKER(index, sectionUnknown1, sectionLength, sectionUnknown2, sectionData, endFlag)
                : new UISaveSection(index, sectionUnknown1, sectionLength, sectionUnknown2, sectionData, endFlag);
        }

        private void ValidateEnvelopeForSave()
        {
            ValidateRawLength(fileFormatVersionRaw, FileFormatVersionByteLength, "文件格式版本");
            ValidateRawLength(fileUnknownRaw, FileUnknownByteLength, "文件未知头部");
            ValidateRawNotNull(fileTailRaw, "文件尾部填充");
            ValidateRawLength(payloadUnknownRaw, PayloadUnknownByteLength, "加密部分未知头");
            ValidateRawLength(userIDRaw, UserIdByteLength, "加密部分用户 ID");
            ValidateRawNotNull(payloadTailRaw, "加密部分尾部填充");
            ValidateSectionsForSave();
        }

        private void ValidateSectionsForSave()
        {
            if (Sections is null)
            {
                throw new UISaveFormatException(
                    "段列表不能为空。",
                    fieldName: "段列表");
            }

            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i] is null)
                {
                    throw new UISaveFormatException(
                        $"段列表第 {i} 项不能为空。",
                        fieldName: $"段列表第 {i} 项");
                }
            }
        }

        private static byte[] ReadRemainingFileTail(BinaryReader reader)
        {
            long? remaining = UISaveBinaryReader.GetRemaining(reader);
            if (!remaining.HasValue || remaining.Value == 0)
            {
                return [];
            }

            if (remaining.Value > int.MaxValue)
            {
                throw new UISaveFormatException(
                    "文件尾部过大，无法一次读取。",
                    offset: UISaveBinaryReader.GetOffset(reader),
                    expectedLength: int.MaxValue,
                    remainingLength: remaining.Value,
                    fieldName: "文件尾部填充",
                    offsetOrigin: UISaveOffsetOrigin.File);
            }

            byte[] fileTail = UISaveBinaryReader.ReadExact(
                reader,
                (int)remaining.Value,
                "文件尾部填充",
                offsetOrigin: UISaveOffsetOrigin.File);
            DebugHelper.LogUnknownPreserved($"加密数据之后存在 {fileTail.Length} 字节文件尾部填充，已原样保留。");
            return fileTail;
        }

        private static void ValidateRawLength(byte[]? value, int expectedLength, string fieldName)
        {
            if (value is null)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 不能为空。",
                    expectedLength: expectedLength,
                    remainingLength: 0,
                    fieldName: fieldName);
            }

            if (value.Length != expectedLength)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 必须正好是 {expectedLength} 字节。",
                    expectedLength: expectedLength,
                    remainingLength: value.Length,
                    fieldName: fieldName);
            }
        }

        private static void ValidateRawNotNull(byte[]? value, string fieldName)
        {
            if (value is null)
            {
                throw new UISaveFormatException(
                    $"{fieldName} 不能为空。",
                    fieldName: fieldName);
            }
        }

        private static string FormatUserIdHex(byte[] raw)
        {
            return raw.Length == UserIdByteLength
                ? BitConverter.ToUInt64(raw, 0).ToString("X16")
                : string.Empty;
        }

        // section 名称只用于日志和显示，不参与解析、保存或拒绝未知 section。
        // 名称主要参考 FFXIVClientStructs 的 UiSavePackModule.DataSegment，2026-06-14 复核。
        private static readonly Dictionary<int, string> SectionFunctionMap = new()
        {
            //邮件历史
            {0,"LETTER.DAT"},
            {1,"RETTASK.DAT"},
            {2,"FLAGS.DAT"},
            {3,"RCFAV.DAT"},
            //社交
            {4,"UIDATA.DAT"},
            //传送历史
            {5,"TLPH.DAT"},
            {6,"ITCC.DAT"},
            {7,"PVPSET.DAT"},
            {8,"EMTH.DAT"},
            {9,"MNONLST.DAT"},
            {10,"MUNTLST.DAT"},
            {11,"EMJ.DAT"},
            {12,"AOZNOTE.DAT"},
            //跨服通讯贝
            {13,"CWLS.DAT"},
            {14,"ARCHVLST.DAT"},
            {15,"GRPPOS.DAT"},
            {16,"CRAFT.DAT"},
            //场地标点
            {17,"FMARKER.DAT"},
            {18,"MYCNOT.DAT"},
            {19,"ORNMLST.DAT"},
            {20,"MYCITEM.DAT"},
            {21,"GRPSTAMP.DAT"},
            {22,"RTNR.DAT"},
            {23,"BANNER.DAT"},
            {24,"ADVNOTE.DAT"},
            {25,"AKTKNOT.DAT"},
            {26,"VVDNOTE.DAT"},
            {27,"VVDACT.DAT"},
            {28,"TOFU.DAT"},
            {29,"FISHING.DAT"},
            {30,"ACTION.DAT"},
            {31,"TFILTER.DAT"},
            {32,"READYC.DAT"},
            {33,"PTRLST.DAT"},
            {34,"CATSBM.DAT"},
            {35,"DESCRI.DAT"},
            {36,"MJICWSP.DAT"},
            {37,"PERFORM.DAT"},
            {38,"MKDSJOB.DAT"},
            {39,"MKDLORE.DAT"},
            {40,"MKDSJN.DAT"},
            {41,"QPNL.DAT"},
            {42,"GLASSES.DAT"},
            {43,"XBMNOTE.DAT"},
            {44,"XBM.DAT"},
        };

        private sealed record ParsedUISaveFile(
            byte[] FileFormatVersionRaw,
            byte[] FileUnknownRaw,
            byte[] FileTailRaw,
            byte[] PayloadUnknownRaw,
            byte[] UserIDRaw,
            List<UISaveSection> Sections,
            byte[] PayloadTailRaw);

        private sealed record ParsedEncryptedPayload(
            byte[] PayloadUnknownRaw,
            byte[] UserIDRaw,
            List<UISaveSection> Sections,
            byte[] PayloadTailRaw);
    }
}
