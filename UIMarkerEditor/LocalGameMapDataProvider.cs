using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lumina;
using Lumina.Data;
using Lumina.Excel;

namespace UIMarkerEditor;

internal interface ILocalGameMapDataProvider
{
    MapDataSnapshotIdentity GetSnapshotIdentity(string gameInstallDirectory);
    MapDataSnapshot LoadFromGameInstallDirectory(string gameInstallDirectory);
}

internal sealed class LocalGameMapDataProvider : ILocalGameMapDataProvider
{
    // ContentFinderCondition.csv 中 43 是 Name，44 是 NameShort。
    private const int ContentFinderConditionNameColumn = 43;

    public MapDataSnapshotIdentity GetSnapshotIdentity(string gameInstallDirectory)
    {
        string sqpackDirectory = ResolveSqpackDirectory(gameInstallDirectory);
        return CreateSnapshotIdentity(sqpackDirectory);
    }

    public MapDataSnapshot LoadFromGameInstallDirectory(string gameInstallDirectory)
    {
        MapDataSnapshotIdentity identity = GetSnapshotIdentity(gameInstallDirectory);

        using GameData gameData = new(identity.SourcePath);
        if (!gameData.FileExists("exd/root.exl"))
        {
            throw new InvalidOperationException("sqpack 中缺少 exd/root.exl，无法读取表格数据。");
        }

        Dictionary<ushort, string> mapNames = LoadContentFinderConditionNames(gameData);
        if (mapNames.Count == 0)
        {
            throw new InvalidOperationException("没有从本地客户端解析到可用地图数据。");
        }

        return new MapDataSnapshot(
            identity.Version,
            identity.SourcePath,
            identity.SourceFingerprint,
            mapNames);
    }

    private static string ResolveSqpackDirectory(string gameInstallDirectory)
    {
        if (!WayMarkOpenDirectoryResolver.TryResolveGameDirectory(gameInstallDirectory, out string? gameDirectory))
        {
            throw new InvalidOperationException("游戏安装目录无效，无法定位 game 目录。");
        }

        string sqpackDirectory = Path.Combine(gameDirectory, "sqpack");
        if (!Directory.Exists(sqpackDirectory))
        {
            throw new DirectoryNotFoundException($"未找到 sqpack 目录：{sqpackDirectory}");
        }

        return sqpackDirectory;
    }

    private static Dictionary<ushort, string> LoadContentFinderConditionNames(GameData gameData)
    {
        RawExcelSheet contentFinderConditionSheet = gameData.Excel.GetRawSheet("ContentFinderCondition", Language.ChineseSimplified);

        Dictionary<ushort, string> mapNames = [];
        foreach (RawRow contentFinderConditionRow in new ExcelSheet<RawRow>(contentFinderConditionSheet))
        {
            uint regionId = contentFinderConditionRow.RowId;
            if (regionId == MapData.EmptyRegionId || regionId > ushort.MaxValue) continue;

            string name = FormatRawValue(contentFinderConditionRow.ReadColumn(ContentFinderConditionNameColumn)).Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            mapNames[(ushort)regionId] = name;
        }

        return mapNames;
    }

    private static MapDataSnapshotIdentity CreateSnapshotIdentity(string sqpackDirectory)
    {
        DateTime latestIndexWriteTime = DateTime.MinValue;
        List<string> indexMetadata = [];
        try
        {
            foreach (string indexFile in Directory
                .EnumerateFiles(sqpackDirectory, "*.index", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                FileInfo fileInfo = new(indexFile);
                DateTime writeTime = fileInfo.LastWriteTimeUtc;
                if (writeTime > latestIndexWriteTime)
                {
                    latestIndexWriteTime = writeTime;
                }

                string relativePath = Path.GetRelativePath(sqpackDirectory, indexFile)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                indexMetadata.Add(FormattableString.Invariant($"{relativePath}:{writeTime.Ticks}:{fileInfo.Length}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            latestIndexWriteTime = DateTime.MinValue;
            indexMetadata.Clear();
        }

        string versionStamp = latestIndexWriteTime > DateTime.MinValue
            ? MapDataSourceParsers.FormatSnapshotTimestamp(latestIndexWriteTime)
            : "unknown";
        string fingerprint = indexMetadata.Count > 0
            ? $"sqpack-indexes:{indexMetadata.Count}:{CreateMetadataHash(indexMetadata)}"
            : string.Empty;
        return new MapDataSnapshotIdentity(
            versionStamp,
            sqpackDirectory,
            fingerprint);
    }

    private static string CreateMetadataHash(IEnumerable<string> metadata)
    {
        string payload = string.Join("\n", metadata);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string FormatRawValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string stringValue => stringValue,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable enumerable when value is not string => string.Join(",", enumerable.Cast<object>().Take(8)),
            _ => value.ToString() ?? string.Empty
        };
    }
}
