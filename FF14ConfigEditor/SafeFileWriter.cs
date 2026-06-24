using System.Text;

namespace FF14ConfigEditor
{
    public static class SafeFileWriter
    {
        private static readonly Encoding DefaultTextEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void WriteAllText(string path, string contents)
        {
            WriteAllText(path, contents, DefaultTextEncoding);
        }

        public static void WriteAllText(string path, string contents, Encoding encoding)
        {
            WriteAllBytes(path, encoding.GetBytes(contents));
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Target file must have a parent directory.");
            Directory.CreateDirectory(directory);

            string tempPath = CreateTempPath(directory, fullPath);
            try
            {
                using (FileStream stream = new(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.WriteThrough))
                {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(flushToDisk: true);
                }

                ReplaceOrMove(tempPath, fullPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        public static void Copy(string sourcePath, string targetPath)
        {
            string sourceFullPath = Path.GetFullPath(sourcePath);
            string targetFullPath = Path.GetFullPath(targetPath);
            string targetDirectory = Path.GetDirectoryName(targetFullPath)
                ?? throw new InvalidOperationException("Target file must have a parent directory.");
            Directory.CreateDirectory(targetDirectory);

            string tempPath = CreateTempPath(targetDirectory, targetFullPath);
            try
            {
                using (FileStream source = new(
                    sourceFullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (FileStream target = new(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.WriteThrough))
                {
                    source.CopyTo(target);
                    target.Flush(flushToDisk: true);
                }

                ReplaceOrMove(tempPath, targetFullPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static string CreateTempPath(string directory, string targetPath)
        {
            string fileName = Path.GetFileName(targetPath);
            return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        }

        private static void ReplaceOrMove(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                try
                {
                    File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Some Windows environments deny File.Replace metadata updates even when a same-directory overwrite is allowed.
                    File.Move(tempPath, targetPath, overwrite: true);
                }

                return;
            }

            File.Move(tempPath, targetPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup; the target file has already stayed untouched or been replaced.
            }
        }
    }
}
