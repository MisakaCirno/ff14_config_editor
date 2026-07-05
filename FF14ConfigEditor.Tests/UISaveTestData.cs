using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

internal static class UISaveTestData
{
    private static readonly byte[] FileFormatVersion = [0x55, 0x49, 0x53, 0x41, 0x56, 0x45, 0x01, 0x00];
    private static readonly byte[] FileUnknown = [0x10, 0x20, 0x30, 0x40];
    private static readonly byte[] PayloadUnknown = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
    private static readonly byte[] UserId = [0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11];

    public static byte[] SectionUnknown1()
    {
        return [0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6];
    }

    public static byte[] SectionUnknown2()
    {
        return [0xB1, 0xB2, 0xB3, 0xB4];
    }

    public static byte[] SectionEndFlag()
    {
        return [0xE1, 0xE2, 0xE3, 0xE4];
    }

    public static byte[] MarkerTail()
    {
        return [0x00, 0x00, 0x00, 0x00];
    }

    public static void SetMarkerTail(SectionFMARKER section, byte[] markerTail)
    {
        SetPrivateField(section, "_markerTail", markerTail);
    }

    public static void SetMarkerHeader(SectionFMARKER section, byte[] markerHeader)
    {
        SetPrivateField(section, "_markerHeader", markerHeader);
    }

    public static void SetConfigByteArrayField(ConfigUISave config, string fieldName, byte[]? value)
    {
        SetPrivateField(config, fieldName, value);
    }

    public static byte[] GetConfigByteArrayField(ConfigUISave config, string fieldName)
    {
        if (GetPrivateField(config, fieldName) is not byte[] value)
        {
            throw new InvalidOperationException($"字段 {fieldName} 不是 byte[]。");
        }

        return value.ToArray();
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        return GetPrivateFieldInfo(target, fieldName).GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        GetPrivateFieldInfo(target, fieldName).SetValue(target, value);
    }

    private static System.Reflection.FieldInfo GetPrivateFieldInfo(object target, string fieldName)
    {
        System.Reflection.FieldInfo? field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            throw new InvalidOperationException($"无法找到字段 {fieldName}。");
        }

        return field;
    }

    public static byte[] BuildFile(byte[] decryptedPayload, byte[]? fileTail = null)
    {
        byte[] encryptedPayload = Utils.EncryptData(decryptedPayload);
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(encryptedPayload.Length);
        writer.Write(FileUnknown);
        writer.Write(encryptedPayload);
        if (fileTail is { Length: > 0 })
        {
            writer.Write(fileTail);
        }

        return ms.ToArray();
    }

    public static byte[] BuildFile(
        byte[] decryptedPayload,
        byte[] fileFormatVersion,
        byte[] fileUnknown,
        byte[]? fileTail = null)
    {
        byte[] encryptedPayload = Utils.EncryptData(decryptedPayload);
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(fileFormatVersion);
        writer.Write(encryptedPayload.Length);
        writer.Write(fileUnknown);
        writer.Write(encryptedPayload);
        if (fileTail is { Length: > 0 })
        {
            writer.Write(fileTail);
        }

        return ms.ToArray();
    }

    public static byte[] BuildFileWithPartialEncryptedLength(byte[] partialEncryptedLength)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(partialEncryptedLength);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithPartialFileUnknown(int declaredLength, byte[] partialFileUnknown)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(declaredLength);
        writer.Write(partialFileUnknown);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithDeclaredEncryptedLength(int declaredLength, byte[] encryptedPayload)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(declaredLength);
        writer.Write(FileUnknown);
        writer.Write(encryptedPayload);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithDeclaredEncryptedLength(int declaredLength)
    {
        return BuildFileWithDeclaredEncryptedLength(declaredLength, []);
    }

    public static byte[] BuildPayload(params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(PayloadUnknown);
        writer.Write(UserId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }

        return ms.ToArray();
    }

    public static byte[] BuildPayload(byte[] payloadUnknown, byte[] userId, params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(payloadUnknown);
        writer.Write(userId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }

        return ms.ToArray();
    }

    public static byte[] BuildPayloadWithTail(byte[] payloadTail, params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(PayloadUnknown);
        writer.Write(UserId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }
        writer.Write(payloadTail);

        return ms.ToArray();
    }

    public static byte[] BuildSection(short index, byte[] data)
    {
        return BuildSection(index, data, SectionUnknown1(), SectionUnknown2(), SectionEndFlag());
    }

    public static byte[] BuildSection(
        short index,
        byte[] data,
        byte[] unknown1,
        byte[] unknown2,
        byte[] endFlag)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(unknown1);
        writer.Write(data.Length);
        writer.Write(unknown2);
        writer.Write(data);
        writer.Write(endFlag);

        return ms.ToArray();
    }

    public static byte[] BuildSectionPrefixWithLength(short index, int declaredLength)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(SectionUnknown1());
        writer.Write(declaredLength);

        return ms.ToArray();
    }

    public static byte[] BuildSectionWithDeclaredLength(short index, int declaredLength, byte[] data)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(SectionUnknown1());
        writer.Write(declaredLength);
        writer.Write(SectionUnknown2());
        writer.Write(data);

        return ms.ToArray();
    }

    public static byte[] BuildMarkerData(int wayMarkCount, byte[]? tail = null)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Enumerable.Range(0, SectionFMARKER.MarkerHeaderByteLength).Select(value => (byte)value).ToArray());
        for (int i = 0; i < wayMarkCount; i++)
        {
            writer.Write(BuildWayMarkBytes((ushort)(100 + i), i));
        }

        if (tail is { Length: > 0 })
        {
            writer.Write(tail);
        }

        return ms.ToArray();
    }

    public static SectionFMARKER BuildFMarkerSection(byte[] markerData)
    {
        return new SectionFMARKER(
            17,
            SectionUnknown1(),
            markerData.Length,
            SectionUnknown2(),
            markerData,
            SectionEndFlag());
    }

    private static byte[] BuildWayMarkBytes(ushort regionId, int seed)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        for (int i = 0; i < 24; i++)
        {
            writer.Write(seed * 1000 + i + 1);
        }

        writer.Write((byte)0xFF);
        writer.Write((byte)0x12);
        writer.Write(regionId);
        writer.Write(123456 + seed);

        return ms.ToArray();
    }

    // 构造一个语义明确的单 WayMark marker 数据：
    // - 16 字节头（0x00..0x0F）。
    // - 1 个 WayMark，8 个点坐标连续递增 1..24（A.X=1 ... Four.Z=24），便于发现偏移或点顺序错误。
    // - 4 字节尾部。
    // 配合语义断言测试使用：坐标递增 + 交错 enableFlag 可暴露"读写对称但点顺序/位映射写反"的对称 bug。
    public static byte[] BuildMarkerDataWithKnownWayMark(byte enableFlag, ushort regionId, int timestamp, byte unknown = 0x12)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Enumerable.Range(0, SectionFMARKER.MarkerHeaderByteLength).Select(value => (byte)value).ToArray());
        writer.Write(BuildKnownWayMarkBytes(enableFlag, regionId, timestamp, unknown));
        writer.Write(MarkerTail());

        return ms.ToArray();
    }

    private static byte[] BuildKnownWayMarkBytes(byte enableFlag, ushort regionId, int timestamp, byte unknown)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        for (int i = 0; i < 24; i++)
        {
            writer.Write(i + 1);
        }

        writer.Write(enableFlag);
        writer.Write(unknown);
        writer.Write(regionId);
        writer.Write(timestamp);

        return ms.ToArray();
    }
}
