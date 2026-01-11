using FF14ConfigEditor.UISave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
         * https://github.com/Haselnussbomber/HaselDebug/blob/main/HaselDebug/Tabs/Disabled/UIModuleTab.cs
         */

        // 写会文件的时候需要用到
        // 没加密的部分
        private byte[] fileFormatVersionRaw = [];
        private byte[] fileUnknownRaw = [];
        // 加密了的部分
        private byte[] payloadUnknownRaw = [];
        private byte[] userIDRaw = [];
        private byte[] payloadTailRaw = []; // 保存解密数据末尾的填充

        /// <summary>
        /// 解析出的 Section 列表
        /// </summary>
        public List<UISaveSection> Sections { get; private set; } = [];

        /// <summary>
        /// 每个Section对应的内容
        /// </summary>
        private readonly Dictionary<int, string> sectionFunctionMap = new()
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
            {14,"ACHVLST.DAT"},
            {15,"GRPPOS.DAT"},
            {16,"CRAFT.DAT"},
            //场地标点
            {17,"FMARKER.DAT"},
            {18,"MYCNOT.DAT"},
            {19,"ORNMLST.DAT"},
            {20,"MYCITEM.DAT"},
            {21,"GPSTAMP.DAT"},
            {22,"RTNR.DAT"},
            {23,"BANNER.DAT"},
            {24,"ADVNOTE.DAT"},
            {25,"AKTKNOT.DAT"},
            {26,"VVDNOTE.DAT"},
            {27,"VVDACT.DAT"},
            {28,"TOFU.DAT"},
            {29,"FISHING.DAT"},
            {30,"未知内容"},
            {31,"未知内容"},
            {32,"未知内容"},
            {33,"未知内容"},
            {34,"未知内容"},
            {35,"未知内容"},
            {36,"未知内容"},
            {37,"未知内容"},
            {38,"未知内容"},
            {39,"未知内容"},
            {40,"未知内容"},
            {41,"未知内容"},
            {42,"未知内容"},
        };

        public SectionFMARKER? Marks
        {
            get
            {
                // 根据 section 的 index 字段查找 FMARKER (index == 17)，避免直接按列表下标访问
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
            using FileStream fs = new(FilePath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);

            fileFormatVersionRaw = reader.ReadBytes(8);
            DebugHelper.Log($"文件格式版本: {BitConverter.ToString(fileFormatVersionRaw)}");

            int encryptLength = reader.ReadInt32();
            DebugHelper.Log($"加密数据长度: {encryptLength}");

            fileUnknownRaw = reader.ReadBytes(4);
            DebugHelper.Log($"未知头部: {BitConverter.ToString(fileUnknownRaw)}");

            // 读取加密数据
            byte[] encryptedData = reader.ReadBytes(encryptLength);
            byte[] decryptedData = Utils.DecryptData(encryptedData);

            // 处理解密后的数据（根据不同的部分需要进行解析）
            ParseEncryptedPart(decryptedData);
        }

        public override void Save()
        {
            // 写出文件内容到 filePath
            // 先处理加密部分
            byte[] encryptedData;
            using (MemoryStream ms = new())
            {
                using (BinaryWriter writer = new(ms))
                {
                    // 写入加密部分
                    writer.Write(payloadUnknownRaw);
                    writer.Write(userIDRaw);

                    // 写入 Sections
                    foreach (UISaveSection section in Sections)
                    {
                        writer.Write(section.ToRawBytes());
                    }

                    // 写入尾部填充
                    if (payloadTailRaw.Length > 0)
                    {
                        writer.Write(payloadTailRaw);
                    }
                }

                byte[] decryptedData = ms.ToArray();
                encryptedData = Utils.EncryptData(decryptedData);
            }

            using FileStream fs = new(FilePath, FileMode.Create, FileAccess.Write);
            using (BinaryWriter writer = new(fs))
            {
                // 写入未加密部分
                writer.Write(fileFormatVersionRaw);
                writer.Write(encryptedData.Length);
                writer.Write(fileUnknownRaw);

                // 写入加密部分
                writer.Write(encryptedData);
            }
        }

        /// <summary>
        /// 解析加密后的数据内容
        /// </summary>
        /// <param name="decryptedData"></param>
        public void ParseEncryptedPart(byte[] decryptedData)
        {
            using MemoryStream ms = new(decryptedData);
            using BinaryReader reader = new(ms);

            // 解析第一层结构
            // 8字节未知
            payloadUnknownRaw = reader.ReadBytes(8);
            DebugHelper.Log($"加密部分 - 未知: {BitConverter.ToString(payloadUnknownRaw)}");

            // 8字节用户ID，是int64
            userIDRaw = reader.ReadBytes(8);
            DebugHelper.Log($"加密部分 - 用户ID: {BitConverter.ToInt64(userIDRaw, 0):X16}");

            Sections.Clear();

            // 解析sections
            /**
             * 每个section的结构
             * - int16, index
             * - 6 bytes, section unknown1
             * - int32, section length
             * - 4 bytes, section unknown2
             * - section length bytes, section data
             * - 4 bytes, end flag
             */
            while (ms.Position < ms.Length)
            {
                // 防止读取越界，虽然理论上结构严谨不会发生，但加上判读更安全
                if (ms.Length - ms.Position < 2) break;

                short index = reader.ReadInt16();
                byte[] sectionUnknown1 = reader.ReadBytes(6);
                int sectionLength = reader.ReadInt32();
                byte[] sectionUnknown2 = reader.ReadBytes(4);
                byte[] sectionData = reader.ReadBytes(sectionLength);
                byte[] endFlag = reader.ReadBytes(4);

                if (index == 17)
                {
                    // FMARKER 特殊处理
                    SectionFMARKER fmarkerSection = new(
                        index,
                        sectionUnknown1,
                        sectionLength,
                        sectionUnknown2,
                        sectionData,
                        endFlag);
                    fmarkerSection.ParseMarker();
                    Sections.Add(fmarkerSection);

                    // 打印section信息
                    DebugHelper.Log($"- - - - -");
                    if (sectionFunctionMap.TryGetValue(index, out string? name))
                    {
                        DebugHelper.Log($"Section Name: {name}");
                    }
                    fmarkerSection.DebugPrintInfo();
                }
                else
                {
                    UISaveSection section = new(
                        index,
                        sectionUnknown1,
                        sectionLength,
                        sectionUnknown2,
                        sectionData,
                        endFlag);

                    Sections.Add(section);

                    // 打印section信息
                    DebugHelper.Log($"- - - - -");
                    if (sectionFunctionMap.TryGetValue(index, out string? name))
                    {
                        DebugHelper.Log($"Section Name: {name}");
                    }
                    section.DebugPrintInfo();
                }
            }

            // 读取剩余的尾部数据（padding）
            if (ms.Position < ms.Length)
            {
                payloadTailRaw = reader.ReadBytes((int)(ms.Length - ms.Position));
                DebugHelper.Log($"加密部分 - 尾部填充: {payloadTailRaw.Length} bytes");
            }
        }
    }
}
