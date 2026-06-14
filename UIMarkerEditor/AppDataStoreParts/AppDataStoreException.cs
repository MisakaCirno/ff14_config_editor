namespace UIMarkerEditor;

public sealed class AppDataStoreException : Exception
{
    public AppDataStoreException(string operation, string path, Exception innerException)
        : base($"{operation}失败：{path}{Environment.NewLine}原因：{innerException.Message}", innerException)
    {
        Operation = operation;
        Path = path;
    }

    public string Operation { get; }

    public string Path { get; }
}
