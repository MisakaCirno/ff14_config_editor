# Codex 协作复盘与防错清单

这个文件记录本仓库协作中已经犯过、容易复发、会造成真实风险的错误。可以口头叫“错题本”，但正式维护时按“复盘与防错清单”处理。后续处理 `FFXIVConfigEditor`，尤其是 WPF 对话框、本地数据目录、迁移和持久化逻辑时，先快速扫一遍这里。

## 使用方式

1. 改 WPF 窗口或对话框前，先看“WPF 资源作用域”。
2. 改数据目录、设置保存、最近文件、日志、缓存、备份前，先看“本地数据与迁移”。
3. 改完后至少运行对应测试；涉及 WPF XAML 时，不能只依赖数据层测试。

## 记录格式

新增条目时使用下面的结构，避免只写一句“以后注意”但没有可执行护栏：

```markdown
### N. 简短问题标题

适用范围：涉及哪些模块、文件、功能或工作流。

症状：用户或测试看到什么现象；如果有原始错误信息，保留关键原文。

根因：为什么会发生，尽量写成可复用的判断规则，而不只是这一次的偶然细节。

以后怎么做：

- 具体防错规则 1。
- 具体防错规则 2。

对应护栏：

- 自动化测试、静态检查、人工检查点或验证命令。
- 如果当前没有护栏，要写明原因和后续补法。
```

记录要求：

- 标题写问题本质，不写情绪化描述。
- 症状保留关键错误文本，尤其是异常类型、资源名、路径、阶段名。
- 根因要能迁移到未来相似场景。
- “以后怎么做”必须是可执行规则。
- 能补测试就补测试；暂时不能补测试时，写清楚人工检查点。
- 同类问题复发时更新原条目，不要随手复制出多个相近条目。
- 条目编号全文件连续递增。

## WPF 资源作用域

### 1. 独立对话框不能引用只在某个 UserControl 局部合并的样式

症状：打开对话框时报 `XamlParseException`，内部异常类似：

```text
无法找到名为“SettingsFormLabelStyle”的资源。资源名称区分大小写。
```

这次触发点：`DataDirectoryMigrationReportDialog.xaml` 引用了 `SettingsFormLabelStyle` / `SettingsReadOnlyTextBoxStyle`，但这两个样式来自 `ToolSettingsControl.xaml` 局部合并的 `ToolSettingsStyles.xaml`。独立 `Window` 初始化时不在这个资源作用域内，因此运行时崩溃。

为什么这是高频错题：以前其他对话里也犯过同类错误。根因通常是“在某个页面里能用的 StaticResource，被误以为全应用都能用”。

以后怎么做：

- 如果样式只服务某个 UserControl，就不要在独立 Dialog 里直接引用它。
- 如果 Dialog 确实要复用该样式，Dialog 自己合并对应 ResourceDictionary。
- 如果多个窗口都需要该样式，考虑把样式提升到全局主题资源，而不是偷偷依赖页面局部资源。
- 新增 Dialog 后，必须补一个 XAML 初始化测试，至少实例化一次默认构造和关键结果构造。

对应护栏：

- `UIMarkerEditor.Tests/XamlResourceTests.cs` 应覆盖新 Dialog 的 `InitializeComponent()`。
- 涉及 XAML 的最终验证至少运行：

```powershell
dotnet test UIMarkerEditor.Tests\UIMarkerEditor.Tests.csproj --no-restore -o .tmp\uimarker-test-output
```

### 2. 数据层测试通过，不代表 XAML 运行时资源可用

症状：`AppDataStore` 迁移测试全绿，但用户点击按钮后窗口初始化崩溃。

根因：数据层测试覆盖了迁移行为，没有实例化新 XAML 窗口；WPF 的 `StaticResource` 查找错误只有窗口加载时才暴露。

以后怎么做：

- 新增或改动 XAML Window/Dialog 时，把“窗口能初始化”作为测试对象本身。
- 不要把“项目能编译”当成“XAML 资源一定能解析”。

### 3. 不要把多个 WPF 初始化测试拆成多个独立 Application.Current 生命周期

症状：新增一个 Dialog 初始化测试后，单独运行该测试能通过，但整套 `XamlResourceTests` 卡住，甚至弹出 `testhost.exe` 应用程序错误；异常根因可能是跨线程访问 `Application.Current.Shutdown()`。

根因：WPF 的 `Application.Current` 是进程级对象，多个 `[Fact]` 各自开 STA 线程、各自创建/关闭 Application，容易让第二个测试碰到另一个线程拥有的 Application。

以后怎么做：

- 轻量 XAML 初始化护栏优先合并进同一个 STA 测试流程。
- 如果未来要拆多个 WPF XAML 测试，先抽一个真正共享的 STA Dispatcher 测试基础设施，不要每个测试自己管理 `Application.Current`。
- 单独测试通过不等于整套 WPF 测试稳定；新增 WPF 测试后必须跑整个 `XamlResourceTests` 过滤器。

对应验证：

```powershell
dotnet test UIMarkerEditor.Tests\UIMarkerEditor.Tests.csproj --no-restore -o .tmp\uimarker-test-output --filter FullyQualifiedName~XamlResourceTests
```

## 本地数据与迁移

### 4. “迁移”不要做成“复制并切换后留下两份受管数据”

症状：用户期望迁移是挪走数据，但实现语义更接近复制并切换，旧目录可能保留另一份旧版本。

根因：只考虑了避免数据丢失，没有完整定义迁移完成后的所有权和清理语义。

以后怎么做：

- 迁移流程必须是：逐文件复制 -> 哈希校验 -> 切换数据目录 -> 清理旧目录中的受管文件。
- 清理旧文件前必须重新验证目标文件哈希和旧文件哈希。
- 旧文件变化、目标文件变化、删除失败时，不得强行删除。

### 5. 不要迁移用户额外放进数据目录根部的内容

症状：如果直接迁移整个旧目录，可能把用户手动放进去的无关文件也搬走，甚至后续删除。

根因：把“数据目录下的一切”误认为都是工具产物。

以后怎么做：

- 只迁移工具管理的顶层目录：`configs`、`backups`、`cache`、`logs`。
- 这些目录之外的根部文件或未知目录视为用户内容，不复制、不删除。
- 如果旧目录最后只剩用户内容，清理迁移状态文件，并提示用户自行检查旧目录。

### 6. 旧目录清理失败要区分“受管文件未清理”和“用户额外内容保留”

症状：旧目录存在就一直保留 `migration-state.json`，导致下次启动反复尝试恢复，甚至对已经正常使用的新目录做不必要校验。

根因：把“旧目录仍存在”当成“迁移未完成”。

以后怎么做：

- 只要受管文件已清理完成，迁移就算完成。
- 旧目录只剩用户额外内容时，不在下次启动重试清理。
- 汇报窗口要告诉用户旧目录仍有非本工具管理内容，请自行检查。

### 7. 迁移状态文件和启动配置文件不是同一层数据

症状：用户难以理解为什么改了数据目录后，`bootstrap.json` 和 `migration-state.json` 仍固定在原位置。

根因：没有明确区分“固定启动配置层”和“可迁移数据层”。

以后怎么做：

- `bootstrap.json`：固定保存当前数据目录路径。
- `migration-state.json`：固定记录迁移进度和恢复信息。
- 可迁移数据放在 `Data\configs`、`Data\backups`、`Data\cache`、`Data\logs`。
- UI 必须明确显示这两个固定文件的位置和作用。

### 8. 写 `bootstrap.json` 和写 `migration-state.json` 的顺序要能抗崩溃

症状：如果先写 `bootstrap.json` 指向新目录，再写迁移状态为 committed 时崩溃，当前会话和下次启动可能对迁移阶段理解不一致。

根因：把两个文件写入当成了一个事务。

以后怎么做：

- 复制和哈希校验完成后，先记录迁移进入可提交/已提交阶段。
- 再写 `bootstrap.json` 切换当前数据目录。
- 如果写入其中一步失败，恢复内存状态，并保留可诊断的迁移状态。

### 9. 切换路径后重新读取，失败不能污染内存状态

症状：迁移中切到新目录并加载设置/角色/缓存，如果中途失败，内存可能停留在半新半旧状态。

根因：没有把路径切换和重读视为一个状态事务。

以后怎么做：

- 迁移开始前创建完整快照。
- 切换目录、`LoadSettings()`、`LoadCharacters()`、`LoadWayMarkFavorites()`、`LoadServerList()`、`LoadMapDataCache()` 任一步失败，都恢复快照。
- 恢复后日志路径也要回到可用状态。

### 10. 迁移成功后，后续保存设置失败也必须显示迁移结果

症状：迁移已经成功，但随后 `SaveSettings` 失败，用户只看到“保存设置失败”，会误判迁移也失败。

根因：迁移结果窗口放在整个保存流程最后，错误归因不清晰。

以后怎么做：

- 只要迁移已经产生结果，就必须展示迁移报告。
- 如果迁移窗口已经展示过结果，设置页不要重复弹第二个报告。
- 如果迁移本身失败，错误留在迁移窗口里，不要再追加“保存设置失败”的误导提示。

### 11. 新数据目录必须严格约束

症状：允许用户选磁盘根目录、共享根目录、当前数据目录内部目录或非空目录，会带来误删、递归迁移、覆盖已有数据等风险。

以后怎么做：

- 禁止磁盘根目录。
- 禁止 UNC 共享根目录。
- 禁止目标目录位于源数据目录内部。
- 目标目录必须为空。

## UI 进度与线程

### 12. 迁移进度窗口和迁移结果窗口应合并

症状：先弹进度窗口，再弹结果窗口，流程碎，用户难以判断两者是否是同一次迁移。

以后怎么做：

- 同一个窗口先显示进度条和当前操作。
- 迁移完成后原地切换为结果汇报。
- 迁移中禁止关闭窗口，避免用户误以为关闭窗口可以取消迁移。

### 13. 不要把整个迁移粗暴丢到后台线程

症状：为避免 UI 卡顿，直接 `Task.Run` 整个迁移，可能在后台线程修改 WPF 绑定集合。

根因：文件 IO 和 UI 状态重载混在一起。

以后怎么做：

- 文件复制、哈希计算、旧文件删除可以异步执行。
- `ObservableCollection`、设置对象、UI 绑定状态的重载和切换保持在 UI 调用流里。
- 进度通过 `IProgress<DataDirectoryMigrationProgress>` 上报。

## 工具与验证习惯

### 14. PowerShell 写文件后要检查 BOM 和行尾噪声

症状：功能没变，但 diff 出现重复 BOM、整文件行尾变化或 `git diff --check` 警告。

根因：临时脚本写文件时没有保持原编码/行尾。

以后怎么做：

- 写文件前检测 UTF-8 BOM，写回时保持。
- 大段替换后运行 `git diff --check`。
- 如出现行尾警告，统一回项目既有行尾风格。

### 15. Windows 沙箱里 `apply_patch` 可能失败，要换安全编辑方式

症状：`apply_patch` 报 `orchestrator_helper_launch_canceled`。

以后怎么做：

- 不要在这个错误上空转。
- 改用 PowerShell 精确替换或整文件写回，但必须保持编码、行尾，并回读关键片段。

### 16. 构建/测试要按风险选命令

常用验证：

```powershell
dotnet test UIMarkerEditor.Tests\UIMarkerEditor.Tests.csproj --no-restore -o .tmp\uimarker-test-output
dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj --no-build
git -c safe.directory=D:/VisualStudio项目/FFXIVConfigEditor diff --check
```

如果 WPF 应用或 Visual Studio 锁住默认输出目录，优先用 `.tmp` 输出目录验证，不要先去动用户环境。