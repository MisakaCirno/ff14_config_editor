using System;
using System.Collections.Generic;
using System.IO;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    public void ChangeDataDirectory(string newDataDirectory, bool migrateExistingData)
    {
        if (string.IsNullOrWhiteSpace(newDataDirectory))
        {
            throw new InvalidOperationException("数据目录不能为空。");
        }

        string oldDataDirectory = DataDirectory;
        string targetDirectory = Path.GetFullPath(newDataDirectory);
        Directory.CreateDirectory(targetDirectory);
        VerifyDirectoryWritable(targetDirectory);

        if (migrateExistingData && Directory.Exists(oldDataDirectory) &&
            !string.Equals(oldDataDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            string oldFullPath = Path.GetFullPath(oldDataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetFullPath = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (targetFullPath.StartsWith(oldFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("新数据目录不能位于旧数据目录内部，请选择其它位置后再迁移。");
            }

            CopyDirectory(oldDataDirectory, targetDirectory);
        }

        if (!migrateExistingData)
        {
            Settings = new AppSettings();
            Characters.Clear();
            ServerList = new ServerListCache();
        }

        DataDirectory = targetDirectory;
        EnsureDataDirectory();
        ConfigureLogger();
        SaveBootstrap(allowOverwriteInvalid: true);
        LoadSettings();
        LoadCharacters();
        LoadServerList();
    }

    private void EnsureDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            if (!File.Exists(SettingsFilePath))
            {
                WriteJson(SettingsFilePath, Settings);
            }

            if (!File.Exists(CharactersFilePath))
            {
                WriteJson(CharactersFilePath, new List<CharacterProfile>());
            }
        }
        catch (AppDataStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("准备本地数据目录", DataDirectory, ex);
        }
    }

    private void ConfigureLogger()
    {
        AppLogger.SetLogFilePath(LogFilePath);
        AppLogger.Info(AppLogCategory.General, $"日志文件路径：{LogFilePath}");
    }

    private void SaveBootstrap(bool allowOverwriteInvalid = false)
    {
        if (bootstrapFileInvalid && !allowOverwriteInvalid)
        {
            return;
        }

        WriteJson(BootstrapFilePath, new BootstrapSettings { DataDirectory = DataDirectory });
        bootstrapFileInvalid = false;
    }

    private static void VerifyDirectoryWritable(string directory)
    {
        string testFile = Path.Combine(directory, $".write_test_{Guid.NewGuid():N}");
        File.WriteAllText(testFile, string.Empty);
        File.Delete(testFile);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetFile = file.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase);
            string? targetFileDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            SafeFileWriter.Copy(file, targetFile);
        }
    }

}
