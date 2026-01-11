# 最终幻想14用户配置文件编辑器

对用户的配置文件进行解析和编辑的工具，野心是支持所有配置文件的读写。

因此架构是照着这个野心设计的，但是目前精力只够解析UI_SAVE.DAT中的场景标点部分的数据。

# 技术路线
C# + WPF + .NET 8

# 解决方案结构

本工程使用Visual Studio创建，同时存在两个项目，项目结构如下：

- FF14ConfigEditor：对于Config文件读写的功能。
    - UISave
        - SectionFMARKER.cs：解析`UISAVE.DAT`中的标点部分。
        - UISaveSection.cs：解析`UISAVE.DAT`中的每个Section。
        - Utils.cs：针对`UISAVE.DAT`的助手类。
    - ConfigBase.cs：对`.DAT`文件进行操作的基类。
    - ConfigUISave.cs：继承自`ConfigBase.cs`，针对于`UISAVE.DATA`文件的派生类。
    - DebugHelper.cs：用于调整控制台的输出信息。
- UIMarkerEditor：用于编辑`UISAVE.DAT`中标点部分的图形化编辑器。
    - MainWindow.xaml
    - MainWindow.xaml.cs

# 参考
- 对于`UISAVE.DAT`文件的解析，参考了以下内容：
    - https://github.com/PunishedPineapple/UISAVE_Reader
    - https://github.com/Lujiang0111/FFxivUisaveParser
- 关于`UISAVE.DAT`文件的所有Section的含义，参考了以下内容：
    - https://github.com/Haselnussbomber/HaselDebug/blob/main/HaselDebug/Tabs/Disabled/UIModuleTab.cs
- 关于标点部分的Region ID，可以参考这里找到映射关系：
    - https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv
