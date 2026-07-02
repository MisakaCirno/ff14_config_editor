# 最终幻想14用户配置文件编辑器

对 FF14 / FFXIV 本地配置文件进行解析和编辑的工具。

当前主要维护的是 `UISAVE.DAT` 中的场地标点预设编辑；同时包含备份/还原、角色目录辅助、地图数据解析、客户端日志解析等配套功能。

# 技术路线

C# + WPF + .NET 10

SDK 版本由根目录的 `global.json` 固定，目前为 `10.0.301`，`rollForward` 为 `latestFeature`。

# 解决方案结构

本工程使用 Visual Studio 创建，目前 solution 中项目如下：

- FF14ConfigEditor：核心配置文件读写库。
    - UISave：解析和保存 `UISAVE.DAT`，其中 `SectionFMARKER.cs` 负责标点部分。
    - ConfigUISave.cs：针对 `UISAVE.DAT` 文件的包装类。
    - SafeFileWriter.cs：用于保存时的安全写入。
- UIMarkerEditor：用于编辑 `UISAVE.DAT` 中标点预设的 WPF 图形化编辑器。
    - 支持打开、编辑、保存、备份和还原标点文件。
    - 支持本地角色目录辅助、标点收藏、导入/导出分享 JSON。
    - 地图数据可来自在线来源、本地游戏 `game\sqpack` 解析，或用户填写的 CSV。
- FF14ConfigEditor.Tests：核心库测试。
- UIMarkerEditor.Tests：WPF 应用逻辑测试。
- FF14LogParser：FF14 客户端日志解析库。
- FF14LogParser.Tests：日志解析库测试。
- FF14LogViewer：临时/辅助日志查看项目，暂不作为主要维护对象。

# 使用注意

- 本工具会读取和写入玩家本地配置文件，保存或还原前请确认目标路径。
- 不建议在角色在线时编辑 `UISAVE.DAT`，更安全的时机是选角界面或关闭游戏后。
- 工具有备份和安全写入保护，但仍建议保留自己的原始备份。
- 使用本地游戏数据时会读取 FFXIV 安装目录下的 `game\sqpack`，用于解析地图名称和地图 ID，不修改游戏文件。
- 默认不允许未知地图 ID；开启允许后只校验 `DAT` 可保存范围，地图 ID 是否真实可用需要自行确认。

# 常用验证命令

```powershell
dotnet --version
dotnet build FFXIVConfigEditor.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test UIMarkerEditor.Tests\UIMarkerEditor.Tests.csproj --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FF14LogParser.Tests\FF14LogParser.Tests.csproj --no-restore
```

# 参考

- 对于 `UISAVE.DAT` 文件的解析，参考了以下内容：
    - https://github.com/PunishedPineapple/UISAVE_Reader
    - https://github.com/Lujiang0111/FFxivUisaveParser
- 关于 `UISAVE.DAT` 文件的 section / UI 保存结构，参考了以下内容：
    - https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/UiSavePackModule.cs
    - https://github.com/Haselnussbomber/HaselDebug/blob/main/HaselDebug/Tabs/Disabled/UIModuleTab.cs
- 关于标点部分的 Region ID 和地图数据，可以参考这里找到映射关系：
    - https://github.com/thewakingsands/ffxiv-datamining-cn/blob/master/ContentFinderCondition.csv
    - https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv
    - https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version
    - https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json
- 本地游戏数据读取：
    - https://github.com/NotAdam/Lumina
- 日志解析参考：
    - https://ffxivlog.orz.tools/
    - https://github.com/ffxiv-cyou/ffxiv-log-parser
- 友情链接：
    - https://act.diemoe.net
    - https://souma.diemoe.net

第三方库、图标和素材的许可证说明见 `THIRD_PARTY_NOTICES.md`。FINAL FANTASY XIV 及相关素材版权、商标归 Square Enix Holdings Co., Ltd. / Square Enix Co., Ltd. 所有；本工具为非官方工具，与 Square Enix 无从属或授权关系。
