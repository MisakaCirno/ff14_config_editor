# UISAVE.DAT 二进制读写重构待办

本文档用于记录本轮围绕 `UISAVE.DAT`、`FMARKER` 和剪贴板标点导入导出的讨论结论。后续修复按本文档分阶段推进，每完成一项就更新状态。

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

## 基本原则

- `[x]` 真实游戏目录只能读取，不能写入、覆盖、复制回写或自动修复真实文件。
- `[x]` 代码注释优先使用中文；如果中途为了效率临时写英文，最终提交前统一改回中文。
- `[x]` 不为了旧版网游文件做专门兼容分支。旧文件只作为观察样本，帮助判断结构规律。
- `[ ]` 二进制解析层只做结构安全校验，避免把当前版本观察值当作未来永远不变的格式上限。
- `[x]` 未知字段、未知 section、尾部填充应尽量原样保留，保证 round-trip。
- `[ ]` 导入导出层可以做更严格的业务校验，例如地图 ID、坐标精度、剪贴板 JSON 完整性。

## 已完成的前置加固

- `[x]` 保存/恢复已改为 `SafeFileWriter` 原子写入。
- `[x]` `TestCMD` 已从 solution 中移除，仓库可复现。
- `[x]` 地图数据和服务器列表已改为启动前检查，并支持缓存模式。
- `[x]` 剪贴板导入标点已增加 `MapID`、坐标、点位完整性校验。
- `[x]` 本地 JSON 配置/缓存损坏时不再静默吞错，并避免覆盖损坏文件。
- `[x]` 打开/重载/保存 `UISAVE.DAT` 已增加 UI 异常边界。
- `[x]` 已新增 `FF14ConfigEditor.Tests` 测试项目，已有基础测试。

## 当前工作区已有的二进制层改动

以下内容已经在当前工作区出现，但仍需结合后续清单复核，不代表最终完成。

- `[~]` 新增 `UISaveBinaryReader`，集中做 `ReadExact` 和基础数值读取。
- `[~]` 新增 `UISaveFormatException`，用于格式错误并携带 offset、section index、expected length、remaining length 等信息。
- `[~]` `ConfigUISave.Load()` 已改为先解析到临时结构，成功后再替换对象状态。
- `[~]` `ParseEncryptedPart()` 已避免中途失败污染当前对象状态。
- `[x]` section header、section data、endFlag 截断场景已有更明确异常。
- `[x]` `sectionLength < 0` 和 section data 超出 payload 剩余长度已有拒绝。
- `[x]` `SectionFMARKER.ParseMarker()` 使用局部结果解析，全部成功后再替换 `_markerHeader`、`_markerTail` 和 `WayMarks`。
- `[~]` `SectionFMARKER` 构造函数解析后，调用方不应再重复调用 `ParseMarker()`。
- `[~]` `UISaveSection.ValidateForSave()` 已校验 section length、unknown 字段长度和 endFlag 长度。

## 外部参考结论

- `[x]` `PunishedPineapple/UISAVE_Reader` 支持以下理解：文件外层包含 8 字节版本、4 字节加密长度、4 字节未知头，然后是加密 payload；加密 payload 使用 XOR `0x31`。
- `[x]` `Lujiang0111/FFxivUisaveParser` 支持以下理解：`FMARKER` section data 是 16 字节头、若干个 104 字节标点预设、末尾 4 字节尾部。
- `[x]` `FFXIVClientStructs` 中 `MarkerPresetPlacement` 是 104 字节内存结构，能说明单个预设的语义，但它不是 `UISAVE.DAT` 文件结构的完整模型。
- `[x]` 卫月 `WaymarkPresetPlugin` 主要维护独立预设库，不是完整 `UISAVE.DAT` 解析器；它对共享 JSON 兼容性有参考价值，但不能直接当成本项目文件解析依据。
- `[ ]` 补查或复核 `HaselDebug` 中 section 名称映射，确认当前文件中新增 section index 的语义；未知时仍应原样保留。

## 真实文件只读观察

这些观察来自只读扫描，不能把真实文件放入仓库，也不能让自动化测试依赖真实路径。

- `[x]` 当前真实 `UISAVE.DAT` 文件存在 `encryptLength` 后的文件级尾部零填充。
- `[x]` 当前真实文件 section index 连续，且存在当前映射未覆盖的后续 section。
- `[x]` 当前真实文件 payload 内部未观察到额外 payload tail。
- `[x]` 当前真实文件 `FMARKER` 长度为 `3140 = 16 + 104 * 30 + 4`。
- `[x]` 部分旧样本 `FMARKER` 长度为 `540 = 16 + 104 * 5 + 4`。
- `[x]` 已观察样本中 `FMARKER` 最后 4 字节为零。
- `[x]` 已观察样本中 section endFlag 为 4 字节零。
- `[ ]` 后续测试只使用合成二进制数据，不读取或写入真实游戏目录。

## 阶段一：文件外层 envelope 和 round-trip

- `[x]` 新增 `fileTailRaw`，保存 `encryptLength` 之后的文件级尾部数据。
- `[x]` `Load()` 读取加密 payload 后，把剩余文件字节读入 `fileTailRaw`。
- `[x]` `Save()` 写回时在 encrypted data 后追加 `fileTailRaw`。
- `[x]` 不要求文件级 tail 必须全零；如需提示，可记录日志，但不要破坏未来兼容性。
- `[x]` 外层硬校验改为：`encryptLength >= 0` 且 `encryptLength <= fileSize - 16`。
- `[x]` 不设置文件总大小硬上限。
- `[x]` 所有长度计算使用 `long` 做边界判断，避免 `int` 加法溢出后绕过检查。
- `[ ]` 明确区分文件 offset 和 payload offset，异常信息中尽量写清楚来源。

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
- `[ ]` `SectionFunctionMap` 可补充已知 index，但不得影响未知 section 的保留与保存。

### 阶段二测试

- `[x]` 负 `sectionLength` 测试。
- `[x]` 超长 `sectionLength` 测试。
- `[x]` 缺失 endFlag 测试。
- `[x]` payload 剩余 1 字节时作为 payload tail 保留。
- `[x]` 未知 section index 能 load/save 原样保留。
- `[x]` section endFlag 非零时能原样保留，除非后续确认它必须为零。

## 阶段三：事务式解析状态

- `[~]` `Load()` 已先解析临时结构，成功后再替换对象状态。
- `[~]` `ParseEncryptedPart()` 已改为解析临时 payload 后再提交。
- `[x]` 复核 `ApplyParsedFile()` 是否包含外层新增的 `fileTailRaw`。
- `[ ]` 复核解析失败后 `fileFormatVersionRaw`、`fileUnknownRaw`、`payloadUnknownRaw`、`userIDRaw`、`Sections`、`payloadTailRaw`、`fileTailRaw` 都不被污染。
- `[ ]` UI 层继续使用临时 `ConfigUISave` 打开/重载文件，成功后再替换当前对象。

### 阶段三测试

- `[~]` 加载失败不替换已有 `Sections`。
- `[x]` 加载失败不替换 `fileTailRaw`。
- `[ ]` `ParseEncryptedPart()` 失败不替换 payload 相关状态。

## 阶段四：FMARKER 结构规则

- `[x]` `ParseMarker()` 使用局部结果解析，全部成功后再替换 `WayMarks`、`_markerHeader` 和 `_markerTail`。
- `[~]` 已移除或计划移除重复解析职责，构造函数解析后调用方不再手动 `ParseMarker()`。
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

- `[~]` FMARKER round-trip 测试。
- `[~]` `ParseMarker()` 重复调用不重复插入。
- `[~]` 重新解析时不残留旧 tail。
- `[~]` data 不足 16 字节时报错。
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
- `[ ]` 剪贴板导入默认拒绝 `MapID = 0`，除非后续明确支持空槽分享。
- `[ ]` 剪贴板导入默认要求 `MapID` 存在于当前地图数据中；加载原始文件仍允许未知 RegionID。
- `[ ]` 坐标导入对超过 3 位小数的值要明确处理：推荐拒绝，避免静默截断。
- `[ ]` 如果选择四舍五入，必须在错误信息和测试中说明；不要继续隐式 `(int)` 截断。
- `[ ]` 导出避免使用受系统区域影响的 `double.Parse(FormatCoordinate(...))`。
- `[ ]` 导出坐标建议使用 invariant culture，或直接由 raw int 转成稳定数值。
- `[ ]` `Name` 只作为展示字段，不作为可信校验字段。
- `[ ]` 是否要求至少一个 Active 点保持开放：不建议二进制层限制；导入层可以提示但不硬拒绝。
- `[ ]` 如果要严格判断 `Active` 是否缺失，可将共享 DTO 中 `Active` 改为 `bool?` 并补兼容策略。

### 阶段六测试

- `[ ]` `MapID` 缺失时报错。
- `[ ]` `MapID` 超出 `ushort` 范围时报错。
- `[ ]` `MapID = 0` 在剪贴板导入时报错。
- `[ ]` 未知 `MapID` 在剪贴板导入时报错。
- `[ ]` 缺少任意点位时报错。
- `[ ]` 非有限坐标时报错。
- `[ ]` raw int 范围外坐标时报错。
- `[ ]` 超过 3 位小数坐标时报错或按明确规则处理。
- `[ ]` 导出 JSON 在不同系统区域设置下保持稳定。

## 阶段七：异常与 UI 文案

- `[~]` 二进制层已有 `UISaveFormatException`。
- `[ ]` 所有格式异常信息统一为中文。
- `[ ]` 格式异常尽量带字段名、offset、section index、expected length、remaining length。
- `[ ]` UI 捕获 `UISaveFormatException` 时显示友好错误，并避免吞掉底层细节。
- `[ ]` 非格式错误继续按一般异常处理，例如权限、文件占用、IO 错误。
- `[ ]` 日志里区分“格式错误”“未知但保留”“警告”。

## 阶段八：测试和验证命令

- `[ ]` 每阶段至少运行 `dotnet test FF14ConfigEditor.Tests\FF14ConfigEditor.Tests.csproj`。
- `[ ]` 涉及 UI 项目或 solution 结构时运行 `dotnet build FFXIVConfigEditor.sln`。
- `[ ]` 测试数据使用合成字节流和临时目录，不依赖真实游戏文件。
- `[ ]` 涉及真实文件观察时，只运行只读扫描，并在结果中注明不修改真实文件。

## 暂不做的事情

- `[x]` 暂不设置 `UISAVE.DAT` 文件总大小上限。
- `[ ]` 暂不实现旧版游戏文件专用兼容。
- `[ ]` 暂不把卫月插件 JSON 当成本工具唯一共享格式。
- `[ ]` 暂不根据地图实际范围硬编码坐标合理性。
- `[x]` 暂不因为未知 section 或未知尾部内容拒绝加载。

## 推荐修复顺序

1. `[x]` 文件级 tail 保留：新增 `fileTailRaw`，确保真实文件 round-trip 不丢尾部。
2. `[x]` FMARKER 动态槽位：移除 30 槽硬上限，按当前 FMARKER section 中的完整 WayMark 结构推导槽位数量。
3. `[x]` FMARKER tail 固定长度：长度必须 4，内容先保留。
4. `[x]` payload 和 section 长度复核：统一 `long` 边界计算和异常信息。
5. `[x]` 保存前校验补齐：确保保存失败不写目标文件。
6. `[ ]` 剪贴板导入导出收紧：`MapID`、坐标精度、culture 稳定性。
7. `[ ]` section 名称映射补充：新增已知 section 名称，但不影响未知 section 保留。
8. `[ ]` UI 友好错误和日志分级。

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
