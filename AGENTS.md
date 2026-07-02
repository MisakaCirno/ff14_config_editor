# 仓库约定

- 除二进制文件或明确要求特殊格式的外部文件外，文本文件统一使用 CRLF 行尾。
- 不要转换图片、图标、DAT/BIN 文件、压缩包等二进制资源。
- commit message 使用中文。
- 创建 commit 前尽可能按合理的功能边界拆分成小提交。
- C# / XAML / csproj / sln 使用 UTF-8 BOM；Markdown / JSON / 仓库配置文本使用 UTF-8 无 BOM。
- 文本格式检查使用 `dotnet run --project tools/TextFormatGuard -- check`；需要修复时使用 `dotnet run --project tools/TextFormatGuard -- fix`。
- 修复后仍需运行 `git diff --check`，并人工 review diff，确认没有误改二进制或引入无意义格式化。
- PowerShell 读取中文文本时请显式使用 `-Encoding UTF8`。
