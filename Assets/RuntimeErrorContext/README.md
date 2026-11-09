# Runtime Error Context 使用指南

Unity Editor 错误上下文工具：收集日志、解析 C# / Lua 栈、定位源码、聚合记录，并通过窗口、剪贴板或本地 Markdown 文件提供分析上下文。

## 安装与入口

支持 Unity 2022.3+，验证版本为 2022.3.62f3。由 Editor-only asmdef 隔离，无需放入 Editor 文件夹，不依赖 XLua 程序集。

- **Assets 安装**：复制整个 RuntimeErrorContext 目录到目标工程 Assets 下，保留 `.meta`。
- **UPM 安装**：将目录作为嵌入包放入 `Packages/com.sunshuo.runtime-error-context`，或通过 Package Manager 的 Add package from disk 选择本目录的 `package.json`。
- 不要在同一工程同时安装两份。

窗口：**Tools > Runtime Error Context**。
设置：**Tools > Runtime Error Context Settings**，或 **Edit > Preferences > Runtime Error Context**。

## 日常排错

1. 开启“收集运行时错误”，进入 Play Mode。
2. 复现错误，在左侧列表选择记录。
3. 查看消息、主位置源码及 Lua / C# 调用栈。
4. 点击“打开源码”“复制分析上下文”，或开启“同步给 Agent”。

默认新 Unity 会话开启采集，进入 Play Mode 前清空上一轮记录。采集范围是 Exception、Error 和 Assert，不包含普通 Log / Warning。

### 搜索与选中项

搜索框按完整消息、错误类型、主位置及其他栈帧的文件路径和函数名匹配。大小写不敏感，路径支持正反斜杠，忽略搜索词首尾空白。清除搜索恢复全部列表。

搜索不删除记录、不改变选中项或同步目标。选中项被隐藏时会提示，详情仍可查看。

列表按最近发生时间排序。重复报错或新日志引起重排时，若选中项原本可见，会补偿滚动位置，尽量保留其屏幕位置；到列表首尾时受滚动范围限制。手动滚离、搜索隐藏或记录淘汰不会触发强行拉回。修改搜索词从列表顶部显示结果。

### 详情与标签

错误消息显示在源码之前。选中卡片使用蓝色背景及边框；消息、源码和栈文本支持只读选择复制。

标签分为两个维度：

| 维度 | 标签 | 含义 |
| --- | --- | --- |
| 报错位置 | Lua / C# / 未定位 | 按主错误位置的语言、路径和行号判断，不保证本地文件可读取或已确认根因 |
| Unity 日志类型 | 异常 / 错误 / 断言失败 | 分别对应 Exception / Error / Assert，不判断是否被捕获，也不是严重程度评级 |

混合栈在详情中注明“包含 Lua 和 C#”。内部 ErrorKind 保留兼容性及原有聚合用途，不作为新的来源推断标签。

## 源码上下文与复制

- 窗口只预览主位置上下各 **5 行**，最多 11 行，用 `>` 标出目标行。
- 复制和同步时，主位置默认上下各 **15 行**，可在设置中调整。
- 额外收集最多 **两个业务调用位置**，各上下 5 行。
- 跳过非业务帧、配置的 Logger 和重复文件行号；无法定位或读取时继续查找后续帧。完整解析栈不因片段去重而删除递归层次。

复制内容包括错误消息、日志类型、聚合次数与时间、主位置、相关源码、Lua / C# 栈、Unity 版本、场景和平台。

源码来自构建上下文时的磁盘文件，场景来自采集时缓存。Context 是独立快照，不会随 Repository 原始对象变化；详情更新或复制时重新构建。

## Agent 接入与同步

### 首次配置

点击“配置 Agent 接入”，无需先产生错误。工具在**当前 Unity 项目根目录**：

- 创建或补充 `AGENTS.md`，只更新 `runtime-error-context:start/end` 标记内的读取约定，保留其他内容。
- 创建或补充 `.gitignore`，添加 `/.error-context/`，避免重复添加。
- 检查写入权限，标记损坏或只读文件时显示原因。

配置阶段不创建空的错误快照。已经手工编写、但不在插件标记内的规则会保留。移植到其他项目后，需要在目标项目再次配置。

### 同步指定错误

选中记录后点击“同步给 Agent”，输出至 `.error-context/current.md`。未完成接入配置时，可以选择“配置并同步”或“仅同步”。

文件包含一条选中错误的完整分析上下文、UTC 生成时间和错误指纹：

- 切换选择立即更新文件，其他记录不会抢占目标。
- 重复报错合并更新，最多每 0.5 秒写入一次。
- 关闭窗口后继续跟踪已选择记录。
- 同步开关在当前 Unity 会话保留。
- 清空、停止同步、选中记录被淘汰或 Domain Reload 时删除快照；清空或 Reload 后需再次选择错误才能继续同步。
- 写入采用同目录临时文件替换，失败提示不产生可被自身采集的错误日志。

配置成功后顶部收成一行状态，显示位置、更新时间及“查看上下文文件”（在文件管理器中显示）。展开“设置”可查看完整路径、使用说明和更新配置入口。

在有权限访问该工程的 Agent 中说“分析当前 Unity 错误”。Agent 按 AGENTS.md 读取文件，并继续检查项目源码。同步文件不会自动启动分析，也不调用模型或上传内容。

## 设置说明

设置按项目存储于本机 EditorPrefs，不生成项目资产。采集开关使用 SessionState。

| 设置 | 用途 |
| --- | --- |
| 新会话默认开启 | 重新打开 Unity 时是否默认采集 |
| 进入 Play 时清空 | 开始新一轮运行前清除记录和同步快照 |
| 最大错误数量 | 最多保留的不同指纹记录数；超出时淘汰最近发生时间最早的一条 |
| 复制上下文行数 | 主位置导出范围，默认上下各 15 行；不改变窗口预览范围 |
| Lua 源码根目录 | 按顺序映射 Lua 日志路径，支持项目相对路径和绝对路径 |
| Lua Logger 前缀 | 识别特殊 Lua 日志格式及内部错误性质，不等于确认主动报错来源 |
| 忽略的 Lua 栈帧 | 排除 Logger 包装层，优先选择业务位置 |
| 忽略的 C# 路径 | 降低第三方目录作为主位置的优先级 |
| C# Logger 方法标记 | 内部异常日志识别及额外源码片段的 Logger 过滤 |

Lua 路径示例：日志 `model/UserModel.lua:1671`，实际文件 `SourceRes/Lua/model/UserModel.lua`，根目录填写 `SourceRes/Lua`。不要填写到 `model` 子目录。

常见根目录还有 `Assets/Lua`、`Assets/LuaScripts`、`Assets/Scripts/Lua`。忽略帧可填写 `utils/XLogger.lua` 等路径后缀。

修改解析规则后，已有记录不会自动重新解析，建议清空并重新复现。修改 Lua 根目录后的当前详情自动刷新尚未实现；可重新选择记录以重建源码上下文。

## 常见问题

### 根目录已配置，仍然没有源码

分别检查解析位置、根目录映射、文件是否存在、行号是否有效。界面会区分“已配置但路径未匹配”和“文件已定位但对应行不可读取”；设置入口不表示配置丢失。

支持从“业务说明：model/User.lua:行号”消息头中提取路径，保留原始消息，兼容盘符、带空格路径和常见 `[string "@..."]` 格式。升级解析逻辑后须重新采集旧记录。

### pcall 捕获后，traceback 与报错位置不同

错误消息可能保留原始文件行号，但在 pcall 返回后由 Logger 生成的 traceback 可能只反映打印时的调用栈。工具不能恢复已丢失的异常栈；补充的源码片段来自日志记录的调用位置，不保证是异常发生前的直接调用者。

### Lua 异常为什么显示“错误”

如果日志经 LogError 输出，其 Unity 原始类型就是 Error。标签不推断主动打印、捕获状态或严重程度。

### 主位置落在 Logger 或第三方代码

配置忽略的 Lua 栈帧或 C# 路径。仅依据已有日志定位，不保证选出的主位置就是根因。

### 无法跳转 IDE

先确认 Unity Preferences 的 External Script Editor 配置。Assets / Packages 文件优先使用 AssetDatabase.OpenAsset，失败时尝试默认编辑器；外部 Lua 文件直接使用默认编辑器。已实现 Rider、IDEA、VS Code、Cursor、Visual Studio 的参数支持，实际启动与行号跳转需在本机验证。

### Agent 找不到上下文

确认 Agent 打开的是发生错误的游戏工程，并在该工程选择记录、开启同步。清空、停止同步或 Domain Reload 后文件会被删除，需重新选择。搜索隐藏选中项不会停止其同步。

## 架构与扩展

```text
Capture → Parsing → Repository → Context + Source → UI / Markdown / 本地同步
```

日志线程仅轻量入队，解析、Unity API 和界面更新在主线程执行。队列上限 256 条，每次 Editor update 最多处理 50 条；消息和栈分别最多保留 16K / 64K 字符。队列满时丢弃新日志并显示计数，清空同时处理队列、记录和丢弃计数。

公开扩展接口：

```csharp
RuntimeErrorContextRegistry.RegisterParser(customParser);
RuntimeErrorContextRegistry.RegisterSourceResolver(customResolver);
```

实现 `IErrorParser` 或 `ISourceResolver`，可从独立 Editor 程序集注册。适用于特殊 Lua 格式、外部源码路径、加密或打包脚本的自定义映射。

Parser 返回 null 或扩展抛出异常时尝试后续实现。Resolver 使用栈帧副本，失败修改不影响 Repository；扩展故障每种在当前 Domain 内最多警告一次。包源码通过 PackageManager 实际安装目录定位。ErrorContext、Builder、Repository 仍为内部 API。

## 边界与测试

- 仅收集 Editor Play Mode 日志，不进入 Player，不处理真机日志和 IL2CPP Native Crash。
- 不采集局部变量、upvalue、GameObject 或业务状态，不自动修改业务源码。
- 不上传日志、不调用 AI、不提供 JSON 导出。
- 记录仅存在内存中，Domain Reload 和重新启动不恢复历史。
- 指纹包含消息与主位置而非完整调用链；动态消息可能拆成多条，不同调用链可能合并为最新样本。
- 路径范围检查保留，越界行号不会展示伪造的末行片段。

通过 **Window > General > Test Runner > EditMode** 运行 `RuntimeErrorContext.Editor.Tests`。依赖 Unity Test Framework，当前验证使用 1.1.33。集成测试会切换 Play Mode，推荐在独立工程运行。

最近完整回归：Unity 2022.3.62f3，**62 项全部通过**。覆盖解析、递归和混合栈、聚合、队列、源码解析、扩展回退、Markdown、项目接入、文件同步、搜索及选中项滚动补偿。Assets 和 UPM 嵌入包方式均做过接入验证。实际界面观感和外部 IDE 跳转需在使用环境验证。
