# UISAVE.DAT 二进制读写重构待办

本文档用于记录本轮围绕 `UISAVE.DAT`、`FMARKER`、剪贴板标点导入导出，以及后续项目 Review 派生问题的讨论结论。后续修复按本文档分阶段推进，每完成一项就更新状态。

`UISAVE.DAT` 二进制读写主线已经完成一轮收口；本文档继续作为后续 UI 输入边界、本地数据层异常边界、项目结构整理和测试补强的跟踪入口，避免后续讨论时丢失上下文。

## 状态标记

- `[ ]` 未开始。
- `[~]` 已有部分实现，但还需要复核或补强。
- `[x]` 已完成，保留在文档中作为上下文。

## 协作修复流程

每个待办事项都按以下流程推进：

1. 先根据项目的最新情况复核待办事项是否仍然存在，并确认问题定位是否准确。
2. 列出解决方案并和用户讨论；用户确认前只做分析、定位和方案整理，不开始代码修复。
3. 用户确认后开始修复。修复完成后运行对应测试或构建验证，验证通过后创建详细中文 commit。

如果某一项只是文档变更，没有可运行的项目测试，需要在最终说明和提交信息中写明未运行测试的原因。

从项目 Review 派生出的 UI 输入、本地数据层、测试覆盖和项目结构整理任务，也继续遵守上述流程。也就是说：先复核问题是否仍存在，再讨论方案，确认后修复、验证并创建中文 commit。

## 基本原则

- `[x]` 真实游戏目录只能读取，不能写入、覆盖、复制回写或自动修复真实文件。
- `[x]` 代码注释优先使用中文；如果中途为了效率临时写英文，最终提交前统一改回中文。
- `[x]` 不为了旧版网游文件做专门兼容分支。旧文件只作为观察样本，帮助判断结构规律。
- `[x]` 二进制解析层只做结构安全校验，避免把当前版本观察值当作未来永远不变的格式上限。
- `[x]` 未知字段、未知 section、尾部填充应尽量原样保留，保证 round-trip。
- `[x]` 导入导出层可以做更严格的业务校验，例如地图 ID、坐标精度、剪贴板 JSON 完整性。

## 已完成的前置加固

- `[x]` 保存/恢复已改为 `SafeFileWriter` 原子写入。
- `[x]` `TestCMD` 已从 solution 中移除，仓库可复现。
- `[x]` 地图数据和服务器列表已改为启动前检查，并支持缓存模式。
- `[x]` 剪贴板导入标点已增加 `MapID`、坐标、点位完整性校验。
- `[x]` 本地 JSON 配置/缓存损坏时不再静默吞错，并避免覆盖损坏文件。
- `[x]` 打开/重载/保存 `UISAVE.DAT` 已增加 UI 异常边界。
- `[x]` 已新增 `FF14ConfigEditor.Tests` 测试项目，已有基础测试。

## 已提交但仍需复核收口的二进制层改动

以下内容已经进入代码库，但仍需结合后续清单复核和补测试，不代表对应阶段已经全部完成。

- `[x]` 新增 `UISaveBinaryReader`，集中做 `ReadExact` 和基础数值读取。
- `[x]` 新增 `UISaveFormatException`，用于格式错误并携带字段名、offset 来源、offset、section index、expected length、remaining length 等信息。
- `[x]` `ConfigUISave.Load()` 已改为先解析到临时结构，成功后再替换对象状态。
- `[x]` `ParseEncryptedPart()` 已避免中途失败污染当前对象状态。
- `[x]` section header、section data、endFlag 截断场景已有更明确异常。
- `[x]` `sectionLength < 0` 和 section data 超出 payload 剩余长度已有拒绝。
- `[x]` `SectionFMARKER.ParseMarker()` 使用局部结果解析，全部成功后再替换 `_markerHeader`、`_markerTail` 和 `WayMarks`。
- `[x]` `SectionFMARKER` 构造函数解析后，调用方不再重复调用 `ParseMarker()`。
- `[x]` `UISaveSection.ValidateForSave()` 已校验 section length、unknown 字段长度和 endFlag 长度。

## 外部参考结论

- `[x]` `PunishedPineapple/UISAVE_Reader` 支持以下理解：文件外层包含 8 字节版本、4 字节加密长度、4 字节未知头，然后是加密 payload；加密 payload 使用 XOR `0x31`。
- `[x]` `Lujiang0111/FFxivUisaveParser` 支持以下理解：`FMARKER` section data 是 16 字节头、若干个 104 字节标点预设、末尾 4 字节尾部。
- `[x]` `FFXIVClientStructs` 中 `MarkerPresetPlacement` 是 104 字节内存结构，能说明单个预设的语义，但它不是 `UISAVE.DAT` 文件结构的完整模型。
- `[x]` 卫月 `WaymarkPresetPlugin` 主要维护独立预设库，不是完整 `UISAVE.DAT` 解析器；它对共享 JSON 兼容性有参考价值，但不能直接当成本项目文件解析依据。
- `[x]` section 名称映射来源于 `FFXIVClientStructs` 中 `UiSavePackModule.DataSegment`；未知 section 仍应原样保留。

## 真实文件只读观察

这些观察来自只读扫描，不能把真实文件放入仓库，也不能让自动化测试依赖真实路径。

- `[x]` 当前真实 `UISAVE.DAT` 文件存在 `encryptLength` 后的文件级尾部零填充。
- `[x]` 当前真实文件 section index 连续，且存在当前映射未覆盖的后续 section。
- `[x]` 当前真实文件 payload 内部未观察到额外 payload tail。
- `[x]` 当前真实文件 `FMARKER` 长度为 `3140 = 16 + 104 * 30 + 4`。
- `[x]` 部分旧样本 `FMARKER` 长度为 `540 = 16 + 104 * 5 + 4`。
- `[x]` 已观察样本中 `FMARKER` 最后 4 字节为零。
- `[x]` 已观察样本中 section endFlag 为 4 字节零。
- `[x]` 后续测试只使用合成二进制数据，不读取或写入真实游戏目录。

## 阶段一：文件外层 envelope 和 round-trip

- `[x]` 新增 `fileTailRaw`，保存 `encryptLength` 之后的文件级尾部数据。
- `[x]` `Load()` 读取加密 payload 后，把剩余文件字节读入 `fileTailRaw`。
- `[x]` `Save()` 写回时在 encrypted data 后追加 `fileTailRaw`。
- `[x]` 不要求文件级 tail 必须全零；如需提示，可记录日志，但不要破坏未来兼容性。
- `[x]` 外层硬校验改为：`encryptLength >= 0` 且 `encryptLength <= fileSize - 16`。
- `[x]` 不设置文件总大小硬上限。
- `[x]` 所有长度计算使用 `long` 做边界判断，避免 `int` 加法溢出后绕过检查。
- `[x]` 明确区分文件 offset 和 payload offset，异常信息中尽量写清楚来源。

### 阶段一测试

- `[x]` 文件头不足 8 字节时抛出 `UISaveFormatException`。
- `[x]` 外层不足 16 字节时错误信息明确。
- `[x]` `encryptLength < 0` 时抛出明确格式异常。
- `[x]` `encryptLength > fileSize - 16` 时抛出明确格式异常。
- `[x]` 文件级 tail 能在 load/save 后原样保留。
- `[x]` 没有文件级 tail 的文件仍能 round-trip。

## 阶段二：payload 和 section 解析边界

- `[x]` payload 开头 8 字节未知字段和 8 字节 user id 已使用 exact read。
- `[x]` section index、unknown1、length、unknown2、data、endFlag 已做截断检查。
- `[x]` 复核 while 循环尾部逻辑：payload 剩余不足一个 section index 时只作为 tail 保留，不误解析 section。
- `[x]` 复核 `sectionLength + endFlagLength` 是否全程使用 `long`。
- `[x]` 复核 `ReadBytes(n)` 是否已经完全被 `ReadExact` 替代。
- `[x]` 未知 section index 不拒绝，使用普通 `UISaveSection` 保留。
- `[x]` 不强制 section index 连续、不强制 section 数量上限。
- `[x]` 不强制 section endFlag 内容必须为零，只校验长度并原样保留。
- `[x]` `SectionFunctionMap` 已补充已知 index，并通过 `TryGetSectionName` 明确该映射只用于日志和显示，不影响未知 section 的保留与保存。

### 阶段二测试

- `[x]` 负 `sectionLength` 测试。
- `[x]` 超长 `sectionLength` 测试。
- `[x]` 缺失 endFlag 测试。
- `[x]` payload 剩余 1 字节时作为 payload tail 保留。
- `[x]` 未知 section index 能 load/save 原样保留。
- `[x]` section endFlag 非零时能原样保留，除非后续确认它必须为零。

## 阶段三：事务式解析状态

目标是让 `ConfigUISave` 的加载行为接近 all-or-nothing：解析完整成功后才替换对象状态；如果文件格式错误或 payload 中途失败，当前对象应保持加载前状态，不能留下半解析字段。

- `[x]` `Load()` 已先解析临时结构，成功后再替换对象状态。
- `[x]` `ParseEncryptedPart()` 已改为解析临时 payload 后再提交。
- `[x]` 复核 `ApplyParsedFile()` 是否包含外层新增的 `fileTailRaw`。
- `[x]` 复核解析失败后 `fileFormatVersionRaw`、`fileUnknownRaw`、`payloadUnknownRaw`、`userIDRaw`、`Sections`、`payloadTailRaw`、`fileTailRaw` 都不被污染。
- `[x]` UI 层继续使用临时 `ConfigUISave` 打开/重载文件，成功后再替换当前对象。

### 阶段三测试

- `[x]` 加载失败不替换已有 `Sections`。
- `[x]` 加载失败不替换 `fileTailRaw`。
- `[x]` `ParseEncryptedPart()` 失败不替换 payload 相关状态。

## 阶段四：FMARKER 结构规则

- `[x]` `ParseMarker()` 使用局部结果解析，全部成功后再替换 `WayMarks`、`_markerHeader` 和 `_markerTail`。
- `[x]` 已移除重复解析职责，构造函数解析后调用方不再手动 `ParseMarker()`。
- `[x]` 移除 `MaxWayMarkSlots = 30` 作为格式硬上限。
- `[x]` 改用当前 section data 推导槽位数量：从 16 字节头之后连续读取完整 104 字节 WayMark，剩余交给 tail 规则处理。
- `[x]` `data.Length < 20` 时抛出明确格式异常。
- `[x]` `data.Length - 16 - 4` 不能被 104 整除时抛出明确格式异常。
- `[x]` `_markerTail` 长度应固定为 4 字节。
- `[x]` `_markerTail` 内容先原样保留，不强制必须为零；如果非零，可记录诊断信息。
- `[x]` 生产代码不保留当前槽位数量常量；真实样本的 30 槽只写在测试和文档说明中。
- `[x]` 如果未来游戏增加槽位，加载和保存应能保留全部槽位。
- `[x]` UI 当前按文件读出的 WayMarks 列表显示、导入覆盖和移动排序，不设置 30 槽限制。

### 阶段四测试

- `[x]` FMARKER round-trip 测试。
- `[x]` `ParseMarker()` 重复调用不重复插入。
- `[x]` 重新解析时不残留旧 tail。
- `[x]` data 不足最小 FMARKER 结构长度 20 字节时报错，已覆盖不足 16 字节场景。
- `[x]` data 长度不足 20 字节时报错。
- `[x]` FMARKER 长度为 `16 + 104 * 30 + 4` 时通过。
- `[x]` FMARKER 长度为 `16 + 104 * 5 + 4` 时通过，但测试名称不要写成旧版兼容。
- `[x]` 合成 `16 + 104 * 31 + 4` 或更多槽位时应通过，证明没有 30 槽硬上限。
- `[x]` FMARKER tail 长度不是 4 字节时失败。
- `[x]` FMARKER tail 非零时能原样保留，除非后续确认必须拒绝。

## 阶段五：保存前结构一致性校验

- `[x]` `UISaveSection.ValidateForSave()` 已校验 `unknown1`、`unknown2`、`endFlag` 长度、空数据和 `length == data.Length`。
- `[x]` `SectionFMARKER.ValidateForSave()` 已校验 marker header 长度。
- `[x]` `SectionFMARKER.ValidateForSave()` 改为校验 tail 长度为 4。
- `[x]` `SectionFMARKER.ValidateForSave()` 不再因为 WayMark 数量超过 30 而失败。
- `[x]` 每个 `WayMark` 写出必须稳定为 104 字节。
- `[x]` 保存前重新生成 FMARKER data 后，`section.length` 必须与 data 实际长度一致。
- `[x]` `ConfigUISave.ValidateEnvelopeForSave()` 加入 `fileTailRaw` 基础校验。
- `[x]` 保存失败时不应写出目标文件；继续依赖 `SafeFileWriter` 原子写。

### 阶段五测试

- `[x]` 保存前结构校验失败时不写文件。
- `[x]` FMARKER header 长度错误时保存失败。
- `[x]` FMARKER tail 长度错误时保存失败。
- `[x]` WayMark 数量超过 30 但结构合法时保存成功。
- `[x]` 普通 section 的 `length != data.Length` 时保存失败。

## 阶段六：剪贴板导入导出模型

- `[x]` 当前 `MarkerShare` 模型包含 `Name`、`MapID`、A/B/C/D/One/Two/Three/Four。
- `[x]` 当前导入会先完整校验，再修改当前槽位，避免半导入状态。
- `[x]` 当前导入已校验 `MapID` 存在且在 `ushort` 范围内。
- `[x]` 当前导入已校验 8 个点都存在。
- `[x]` 当前导入已校验坐标为有限数，并能转换到 raw int 范围。
- `[x]` 剪贴板导入默认拒绝 `MapID = 0`，除非后续明确支持空槽分享。
- `[x]` 剪贴板导入默认要求 `MapID` 存在于当前地图数据中；加载原始文件仍允许未知 RegionID。
- `[x]` 坐标导入对超过 3 位小数的值要明确处理：推荐拒绝，避免静默截断。
- `[x]` 未选择四舍五入；超过 3 位小数直接报错，不再隐式 `(int)` 截断。
- `[x]` 导出避免使用受系统区域影响的 `double.Parse(FormatCoordinate(...))`。
- `[x]` 导出坐标直接由 raw int 转成稳定数值。
- `[x]` `Name` 只作为展示字段，不作为可信校验字段。
- `[x]` 不要求至少一个 Active 点保持开启；全关闭的分享仍可导入。
- `[x]` 共享 DTO 中 `Active` 改为 `bool?`，缺失时导入报错。

### 阶段六测试

- `[x]` `MapID` 缺失时报错。
- `[x]` `MapID` 超出 `ushort` 范围时报错。
- `[x]` `MapID = 0` 在剪贴板导入时报错。
- `[x]` 未知 `MapID` 在剪贴板导入时报错。
- `[x]` 缺少任意点位时报错。
- `[x]` 非有限坐标时报错。
- `[x]` raw int 范围外坐标时报错。
- `[x]` 超过 3 位小数坐标时报错或按明确规则处理。
- `[x]` 导出 JSON 在不同系统区域设置下保持稳定。

## 阶段七：异常与 UI 文案

- `[x]` 二进制层已有 `UISaveFormatException`。
- `[x]` 所有格式异常信息统一为中文。
- `[x]` 格式异常尽量带字段名、offset 来源、offset、section index、expected length、remaining length。
- `[x]` UI 捕获 `UISaveFormatException` 时显示友好错误，并避免吞掉底层细节。
- `[x]` 非格式错误继续按一般异常处理，例如权限、文件占用、IO 错误。
- `[x]` 日志里区分“格式错误”“未知但保留”“警告”。
- `[x]` 已新增轻量日志系统 `AppLogger`，支持级别、分类、调试输出和文件输出。
- `[x]` 应用启动后日志写入当前数据目录 `logs/app.log`，数据目录变更后同步切换日志路径。

## 阶段八：测试和验证命令

- `[x]` 每阶段至少运行 `dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`。
- `[x]` 涉及 UI 项目、solution 结构、项目引用、窗口/交互逻辑时运行 `dotnet build FFXIVConfigEditor.sln`。
- `[x]` 提交前运行 `git diff --check`；暂存后运行 `git diff --cached --check`。
- `[x]` `dotnet test` 和 `dotnet build` 顺序执行，避免并行构建/测试抢占同一个输出 DLL。
- `[x]` 测试数据使用合成字节流、临时目录和内存对象，不依赖真实游戏文件。
- `[x]` 自动化测试不读取真实游戏目录，不把真实文件复制进仓库。
- `[x]` 涉及真实文件观察时，只运行只读扫描，并在结果中注明不修改真实文件。

## 阶段九：项目结构整理与文件职责拆分

本阶段来自二进制主线完成后的项目整理。目标不是改变行为，而是降低后续修复时的单文件阅读和冲突成本。

- `[x]` `WayMark` 和 `WayMarkPoint` 已从 `SectionFMARKER.cs` 拆出，`SectionFMARKER.cs` 聚焦 FMARKER 段解析、校验和写回。
- `[x]` `MarkerSharePoint`、`MarkerShareConverter`、`ValidatedMarkerShare` 已从 `MarkerShare.cs` 拆出，分享 DTO 和转换校验职责更清楚。
- `[x]` `UISaveBinaryTests.cs` 已拆出 FMARKER 测试、格式异常断言、状态快照和测试数据。
- `[x]` `UIMarkerEditor` 根目录辅助文件已移动到 `Dialogs`、`Converters`、`Data`、`WayMarks` 等目录。
- `[x]` `AppDataStore.cs` 已拆分为 `AppDataStoreParts` 下的设置、角色、数据目录、JSON、地图数据、服务器、备份、最近文件等 partial 文件。
- `[x]` `MainWindow.xaml.cs` 已拆分为 `MainWindowParts` 下的文件操作、最近文件、当前文件状态、布局、设置、备份、角色档案和窗口命令等 partial 文件。
- `[x]` 拆分过程保留原命名空间和公开行为，不主动引入功能变更。

## 阶段十：Review 派生的 UI 输入与本地数据层加固

本阶段来自完整项目 Review 后确认的后续问题。它们不属于 `UISAVE.DAT` 二进制核心高风险，但会影响用户输入边界、辅助数据保存和后续可测试性。

- `[x]` 形状生成器坐标输入已收紧：新增共享 `WayMarkCoordinateConverter`，形状生成和手工坐标编辑复用有限数、范围和 raw 坐标转换规则。
- `[x]` 形状生成器在写入标点前先完成全部点位坐标转换；遇到 `NaN`、`Infinity` 或超范围坐标时提示并中止，避免半套标点写入。
- `[x]` 区域选择写入行为已收紧：`RegionID = 0` 统一显示为 `空(0)`，生成地图显示列表时固定提供该项。
- `[x]` 区域搜索框不再把未识别文本静默解析为 `RegionID = 0`；只有用户从列表明确选择 `空(0)` 时，才会把区域设为未设定。
- `[x]` 本地数据写入异常边界已统一：新增 `AppDataStoreException`，包装本地 JSON 和文本写入失败。
- `[x]` 应用启动流程已增加顶层异常边界；本地数据初始化失败时显示友好错误并退出。
- `[x]` 最近文件、窗口布局、角色备注、备份备注和服务器同步尝试时间等辅助保存点已补充本地数据异常处理和日志。
- `[x]` `GetOrCreateCharacter()` 不再隐式写入文件，调用方按自身场景显式保存并处理失败。
- `[ ]` `AppDataStore` / UI 编排层仍缺少直接测试。下一项应先复核当前结构是否已经支持测试用临时数据目录，再决定是否补测试入口。

### 阶段十后续测试方向

- `[x]` 坐标转换边界已有单元测试覆盖。
- `[~]` 区域选择写入行为已通过构建和现有测试验证，但尚缺少直接 UI/数据层回归测试。
- `[ ]` `AppDataStore` 损坏 JSON 不覆盖、写入失败异常类型、最近文件保存失败返回值、角色档案读写、服务器缓存降级等场景需要补直接测试。
- `[ ]` 如果 WPF UI 自动化成本过高，优先给 `AppDataStore` 增加测试用数据目录入口，用临时目录覆盖数据层行为。

## 暂不做的事情

- `[x]` 暂不设置 `UISAVE.DAT` 文件总大小上限。
- `[x]` 暂不实现旧版游戏文件专用兼容。
- `[x]` 暂不把卫月插件 JSON 当成本工具唯一共享格式。
- `[x]` 暂不根据地图实际范围硬编码坐标合理性。
- `[x]` 暂不因为未知 section 或未知尾部内容拒绝加载。
- `[x]` 暂不让区域搜索文本自由写入未知 `RegionID`；当前只允许通过列表选择写入。
- `[x]` 暂不优先引入 WPF UI 自动化测试；下一步先评估并补数据层直接测试。

## 推荐修复顺序

1. `[x]` 文件级 tail 保留：新增 `fileTailRaw`，确保真实文件 round-trip 不丢尾部。
2. `[x]` FMARKER 动态槽位：移除 30 槽硬上限，按当前 FMARKER section 中的完整 WayMark 结构推导槽位数量。
3. `[x]` FMARKER tail 固定长度：长度必须 4，内容先保留。
4. `[x]` payload 和 section 长度复核：统一 `long` 边界计算和异常信息。
5. `[x]` 保存前校验补齐：确保保存失败不写目标文件。
6. `[x]` 剪贴板导入导出收紧：`MapID`、坐标精度、culture 稳定性。
7. `[x]` section 名称映射补充：新增已知 section 名称，但不影响未知 section 保留。
8. `[x]` 事务式解析状态复核：确认加载失败不会污染 `ConfigUISave` 已有状态，并补齐对应测试。
9. `[x]` FMARKER 生命周期和幂等测试收口：确认不重复解析、不残留旧 tail，并补齐 round-trip 测试状态。
10. `[x]` UI 友好错误和日志分级：日志系统、UI 捕获、格式异常中文文案和 offset 来源细分均已落地。
11. `[x]` 测试和验证规范收尾：确认测试只使用合成数据或临时目录，真实文件观察只读。
12. `[x]` 项目结构整理：拆分核心模型、测试文件、`AppDataStore` 和 `MainWindow` partial 文件。
13. `[x]` 形状生成坐标边界：统一 raw 坐标转换，拒绝非有限数和超范围坐标。
14. `[x]` 区域选择写入行为：`0` 统一为 `空(0)`，未识别文本不再静默写入。
15. `[x]` 本地数据写入异常边界：统一 `AppDataStoreException`，启动和辅助保存点补充异常处理。
16. `[ ]` AppDataStore / UI 编排层测试补强：下一项按协作流程复核、讨论并实施。

## 每轮修复后的记录格式

完成某个阶段后，在对应条目前把 `[ ]` 改为 `[x]`，并在这里追加一条简短记录：

- 日期：
- 阶段：
- 改动文件：
- 验证命令：
- 剩余风险：

- 日期：2026-06-14
- 阶段：阶段一，文件级 tail 保留
- 改动文件：`FF14ConfigEditor/ConfigUISave.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：阶段一中 offset 文案细化和超大文件尾的策略仍需后续复核。

- 日期：2026-06-14
- 阶段：阶段四，FMARKER 动态槽位
- 改动文件：`FF14ConfigEditor/UISave/SectionFMARKER.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：当时 FMARKER tail 固定 4 字节规则尚未收紧，已在后续记录中处理。

- 日期：2026-06-14
- 阶段：阶段四/五，FMARKER tail 固定长度
- 改动文件：`FF14ConfigEditor/UISave/SectionFMARKER.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：尚未提供 UI 级未知格式诊断信息导出，后续异常/UI 文案阶段继续处理。

- 日期：2026-06-14
- 阶段：阶段一/二，payload 和 section 长度复核
- 改动文件：`FF14ConfigEditor/ConfigUISave.cs`、`FF14ConfigEditor/UISave/UISaveBinaryReader.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：异常 offset 来源细分和 UI 级诊断信息导出仍留到异常/UI 文案阶段处理。

- 日期：2026-06-14
- 阶段：阶段六，剪贴板导入导出收紧
- 改动文件：`FF14ConfigEditor/UISave/MarkerShare.cs`、`UIMarkerEditor/Controls/WayMarkEditorParts/WayMarkEditorControl.ImportExport.cs`、`UIMarkerEditor/MapData.cs`、`FF14ConfigEditor.Tests/MarkerShareConverterTests.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：剪贴板 JSON 的 UI 文案和异常分层仍留到异常/UI 文案阶段处理。

- 日期：2026-06-14
- 阶段：阶段二，section 名称映射补充
- 改动文件：`FF14ConfigEditor/ConfigUISave.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`、`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：section 名称来源于 `FFXIVClientStructs`，未来游戏更新新增 index 时应继续按“未知但保留”策略处理。

- 日期：2026-06-14
- 阶段：待办文档同步，明确后续推进顺序
- 改动文件：`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：未运行项目测试；本次仅同步文档中的阶段说明和推荐顺序。
- 剩余风险：下一轮仍需按协作修复流程先复核阶段三现状，再讨论方案并实施。

- 日期：2026-06-14
- 阶段：阶段三，事务式解析状态复核
- 改动文件：`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`、`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：异常 offset 来源细分和 UI 友好文案仍留到异常/UI 文案阶段处理。

- 日期：2026-06-14
- 阶段：阶段四，FMARKER 生命周期和幂等测试收口
- 改动文件：`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`、`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：异常/UI 文案阶段仍需统一格式错误展示和日志分级。

- 日期：2026-06-14
- 阶段：阶段七，轻量日志系统和 UI 格式错误捕获
- 改动文件：`FF14ConfigEditor/AppLogger.cs`、`FF14ConfigEditor/DebugHelper.cs`、`FF14ConfigEditor/ConfigUISave.cs`、`FF14ConfigEditor/UISave/SectionFMARKER.cs`、`UIMarkerEditor/AppDataStore.cs`、`UIMarkerEditor/MainWindow.xaml.cs`、`FF14ConfigEditor.Tests/AppLoggerTests.cs`、`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：格式异常字段名、offset 来源和异常信息中文统一仍需继续收口。

- 日期：2026-06-14
- 阶段：阶段七，格式异常诊断字段和 offset 来源收口
- 改动文件：`FF14ConfigEditor/UISave/UISaveFormatException.cs`、`FF14ConfigEditor/UISave/UISaveBinaryReader.cs`、`FF14ConfigEditor/UISave/UISaveOffsetOrigin.cs`、`FF14ConfigEditor/ConfigUISave.cs`、`FF14ConfigEditor/UISave/UISaveSection.cs`、`FF14ConfigEditor/UISave/SectionFMARKER.cs`、`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`、`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`
- 剩余风险：阶段七暂无已知遗留；下一项进入阶段八测试和验证规范收尾。

- 日期：2026-06-14
- 阶段：阶段八，测试和验证规范收尾
- 改动文件：`UISAVE_BINARY_REFACTOR_TODO.md`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`dotnet build FFXIVConfigEditor.sln`；`git diff --check`；`git diff --cached --check`
- 剩余风险：UISAVE 二进制重构清单暂无未完成项；后续如果新增真实文件观察，仍需继续遵守只读和不入仓库规则。

- 日期：2026-06-14
- 阶段：阶段九，拆分 FMARKER 标点模型文件
- 改动文件：`FF14ConfigEditor/UISave/SectionFMARKER.cs`、`FF14ConfigEditor/UISave/WayMark.cs`、`FF14ConfigEditor/UISave/WayMarkPoint.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：本次为职责拆分，行为应保持不变；后续改 FMARKER 时继续关注模型和 section 写回边界。

- 日期：2026-06-14
- 阶段：阶段九，拆分标点分享模型和转换器
- 改动文件：`FF14ConfigEditor/UISave/MarkerShare.cs`、`FF14ConfigEditor/UISave/MarkerSharePoint.cs`、`FF14ConfigEditor/UISave/MarkerShareConverter.cs`、`FF14ConfigEditor/UISave/ValidatedMarkerShare.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：分享格式仍按当前工具 JSON 模型维护，不把卫月插件 JSON 当作唯一格式。

- 日期：2026-06-14
- 阶段：阶段九，拆分 UISAVE 二进制测试文件
- 改动文件：`FF14ConfigEditor.Tests/UISaveBinaryTests.cs`、`FF14ConfigEditor.Tests/SectionFMarkerTests.cs`、`FF14ConfigEditor.Tests/UISaveFormatExceptionAssert.cs`、`FF14ConfigEditor.Tests/ConfigStateSnapshot.cs`、`FF14ConfigEditor.Tests/SectionStateSnapshot.cs`、`FF14ConfigEditor.Tests/UISaveTestData.cs`
- 验证命令：`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：后续补测试时应继续按职责放入对应测试文件，避免重新堆回单一大文件。

- 日期：2026-06-14
- 阶段：阶段九，整理 UIMarkerEditor 根目录辅助文件
- 改动文件：`UIMarkerEditor/Dialogs`、`UIMarkerEditor/Converters`、`UIMarkerEditor/Data`、`UIMarkerEditor/WayMarks`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：XAML 绑定和命名空间保持不变；后续移动文件仍需跑 solution build 验证。

- 日期：2026-06-14
- 阶段：阶段九，拆分 AppDataStore 根文件
- 改动文件：`UIMarkerEditor/AppDataStore.cs`、`UIMarkerEditor/AppDataStoreParts/*.cs`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：`AppDataStore` 仍缺少直接测试，下一阶段需要补临时目录测试入口和关键场景覆盖。

- 日期：2026-06-14
- 阶段：阶段九，拆分 MainWindow 主窗口逻辑
- 改动文件：`UIMarkerEditor/MainWindow.xaml.cs`、`UIMarkerEditor/MainWindowParts/*.cs`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：主窗口仍是 UI 编排层，后续行为测试优先覆盖可抽出的数据层和核心转换逻辑。

- 日期：2026-06-14
- 阶段：阶段十，收紧形状生成坐标边界
- 改动文件：`FF14ConfigEditor/UISave/WayMarkCoordinateConverter.cs`、`UIMarkerEditor/Controls/WayMarkEditorParts/WayMarkEditorControl.ShapePosition.cs`、`UIMarkerEditor/Controls/WayMarkEditorParts/WayMarkEditorControl.Coordinates.cs`、`FF14ConfigEditor.Tests/WayMarkCoordinateConverterTests.cs`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：UI 输入提示仍靠手工触发路径验证；核心坐标转换边界已有测试。

- 日期：2026-06-14
- 阶段：阶段十，收紧区域选择写入行为
- 改动文件：`UIMarkerEditor/Data/MapData.cs`、`UIMarkerEditor/Controls/WayMarkEditorParts/WayMarkEditorControl.RegionSelector.cs`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：区域选择 UI 行为尚缺直接自动化测试；下一阶段测试补强时可评估是否抽出可测逻辑。

- 日期：2026-06-14
- 阶段：阶段十，统一本地数据写入异常边界
- 改动文件：`UIMarkerEditor/App.xaml.cs`、`UIMarkerEditor/AppDataStoreParts/AppDataStoreException.cs`、`UIMarkerEditor/AppDataStoreParts/AppDataStore.Json.cs`、`UIMarkerEditor/AppDataStoreParts/AppDataStore.Characters.cs`、`UIMarkerEditor/AppDataStoreParts/AppDataStore.RecentFiles.cs`、`UIMarkerEditor/AppDataStoreParts/AppDataStore.Servers.cs`、`UIMarkerEditor/MainWindowParts/MainWindow.FileStatus.cs`、`UIMarkerEditor/MainWindowParts/MainWindow.Layout.cs`、`UIMarkerEditor/Controls/ToolSettingsControl.xaml.cs`、`UIMarkerEditor/Controls/CharacterProfilesControl.xaml.cs`、`UIMarkerEditor/Controls/BackupRestoreControl.xaml.cs`
- 验证命令：`dotnet build FFXIVConfigEditor.sln`；`dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`；`git diff --cached --check`
- 剩余风险：`AppDataStore` / UI 编排层仍缺少直接测试；下一项应围绕临时目录、损坏 JSON、写入失败和缓存降级补覆盖。
