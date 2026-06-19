using System;
using System.IO;
using System.Text.Json;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private enum JsonFileReadStatus
    {
        Missing,
        Success,
        Invalid
    }

    private sealed record JsonFileReadResult<T>(
        JsonFileReadStatus Status,
        T? Value = default,
        Exception? Error = null);

    private JsonFileReadResult<T> ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path)) return new JsonFileReadResult<T>(JsonFileReadStatus.Missing);

        try
        {
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, jsonOptions);
            return value == null
                ? new JsonFileReadResult<T>(
                    JsonFileReadStatus.Invalid,
                    Error: new JsonException("JSON 内容为空或类型不匹配。"))
                : new JsonFileReadResult<T>(JsonFileReadStatus.Success, value);
        }
        catch (Exception ex)
        {
            return new JsonFileReadResult<T>(JsonFileReadStatus.Invalid, Error: ex);
        }
    }

    private void WriteJson<T>(string path, T value)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(value, jsonOptions);
            SafeFileWriter.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("写入本地 JSON 文件", path, ex);
        }
    }

    private void AddJsonReadWarning(string path, string message, Exception? error)
    {
        string warningKey = $"json:{Path.GetFullPath(path)}";
        if (!dataLoadWarningKeys.Add(warningKey)) return;

        string detail = error == null ? string.Empty : $"{Environment.NewLine}原因：{error.Message}";
        dataLoadWarnings.Add($"{message}{Environment.NewLine}文件：{path}{detail}");
    }
}
