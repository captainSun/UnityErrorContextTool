# Runtime Error Context

Runtime Error Context 是一个轻量的 Unity Editor 运行时错误上下文工具。它在 Play Mode 中收集 C# 与 Lua 错误，将原始日志转换为可定位、可聚合、可复制给 Agent 分析的 `ErrorContext`。

工具本身不依赖 AI、MCP、XLua程序集或游戏业务代码。AI只是 `ErrorContext` 的消费者，不参与日志捕获、栈解析和源码定位。

## 功能

- 收集 `Exception`、`Error` 和 `Assert`。
- 区分 C#、Lua和未知错误。
- 区分运行时异常、捕获后打印、主动错误和断言。
- 解析 C# StackTrace和常见 Lua `stack traceback`。
- 识别主错误文件、函数和行号。
- 定位 `Assets/`、`Packages/` 下的 C# 与 Lua源码，并读取错误行附近代码。
- 聚合相同错误并显示发生次数。
- 分开展示 Lua调用栈和 C#外层调用栈。
- 使用 Unity配置的默认IDE打开源码。
- 复制稳定的 Markdown分析上下文。
- 通过 Settings配置不同项目的 Lua路径和日志约定。

## 环境要求

- 推荐 Unity 2020.3或更高版本。
- 已在 Unity `2022.3.62f3` 验证。
- 工具只在 Editor中编译，不会进入Player。
- 不使用 Lua的项目仍可使用全部 C#错误能力。

## 安装

将完整的 `RuntimeErrorContext` 目录复制到目标项目的 `Assets` 下：

```text
Assets/
└── RuntimeErrorContext/
    ├── RuntimeErrorContext.Editor.asmdef
    ├── Capture/
    ├── Context/
    ├── Core/
    ├── Parsing/
    ├── Source/
    └── UI/
```

复制时应保留目录内的 `.meta` 文件。`RuntimeErrorContext.Editor.asmdef` 会确保工具只进入Editor程序集，因此目录不要求放在 `Assets/Editor` 下。

等待 Unity完成编译后，可以从以下菜单打开工具：

```text
Tools > Runtime Error Context
```

设置入口：

```text
Tools > Runtime Error Context Settings
```

也可以从以下位置打开：

```text
Edit > Preferences > Runtime Error Context
```

## 快速使用

1. 打开 `Tools > Runtime Error Context`。
2. 确认顶部“收集运行时错误”已经开启。
3. 进入 Play Mode。
4. 复现错误。
5. 从左侧错误列表选择一条记录。
6. 查看错误消息、Lua/C#调用栈和相关源码。
7. 点击“打开源码”跳转到默认IDE。
8. 点击“复制分析上下文”，将结果发送给 Codex、ChatGPT、Claude或其他Agent。

默认情况下，新Unity会话会开启收集，并在进入 Play Mode时清空上一轮记录。

## 错误分类

工具使用两个独立维度描述错误。

### 语言

- `C#`
- `Lua`
- `Mixed`
- `Unknown`

### 错误性质

- `RuntimeException`：未捕获或明确的运行时异常。
- `CaughtExceptionLog`：异常被业务捕获后通过Logger打印。
- `ManualErrorLog`：业务主动调用错误日志。
- `Assert`：断言错误。
- `Unknown`：无法可靠分类。

分类采用日志与调用栈特征，不修改游戏业务，也不注入Lua虚拟机。

## Settings说明

所有设置按项目保存在本机 `EditorPrefs`，不会生成项目资产，也不会提交到版本控制。

### 新会话默认开启

控制重新打开 Unity后是否默认开启错误收集。当前Unity会话仍可通过窗口顶部开关随时修改。

### 进入 Play时清空

开启后，每次进入 Play Mode前清除上一轮错误记录。

### 最大错误数量

控制最多保留多少种不同错误。相同错误只增加次数，不占用新的记录。

### 源码上下文行数

控制主错误行前后读取的源码行数。默认值为15，即最多读取约31行。

### Lua源码根目录

每行填写一个目录。支持项目相对路径和绝对路径，工具会按顺序查找第一个存在的源码文件。

常见配置：

```text
SourceRes/Lua
Assets/Lua
Assets/LuaScripts
Assets/Scripts/Lua
```

例如日志位置为：

```text
model/UserModel.lua:1668
```

Lua根目录为：

```text
Assets/Game/Lua
```

则工具会查找：

```text
Assets/Game/Lua/model/UserModel.lua
```

### Lua Logger前缀

用于识别 Lua主动错误打印。例如：

```text
LUAXLogger:
LUA:
[LuaError]
```

如果主动错误被误判为运行时异常，应将项目实际使用的Logger前缀添加到这里。

### 忽略的 Lua栈帧

用于跳过Logger包装层，选择真正的业务调用位置。例如：

```text
utils/XLogger.lua
Logger.lua
framework/LogUtil.lua
```

### 忽略的 C#路径

这些目录下的栈帧不会优先作为业务错误位置。例如：

```text
Assets/3rd/
Assets/Plugins/
Assets/ThirdParty/
Assets/Generated/
```

### C# Logger方法标记

用于识别“异常被捕获后主动打印”。填写Logger在调用栈中出现的方法名，例如：

```text
XLogger:LogError
GameLogger:Error
LogService:LogException
```

修改解析规则后，建议清空已有错误并重新复现，因为已经收集的记录不会自动重新解析。

## ErrorContext流程

```text
Unity Log
    ↓
RuntimeErrorCollector
    ↓
ErrorParserRegistry
    ↓
ParsedError / ErrorStackFrame
    ↓
RuntimeErrorRepository
    ↓
SourceResolverRegistry
    ↓
ErrorContextBuilder
    ↓
ErrorContext
    ├── EditorWindow
    ├── Markdown Formatter
    └── Future Agent / AI Consumer
```

`ErrorContext` 是独立快照，包含：

- 错误语言、性质、类型和消息。
- 原始消息和原始调用栈。
- 聚合指纹、发生次数和时间。
- 主错误位置。
- 结构化StackFrame列表。
- 解析后的源码上下文。
- Unity版本、场景和平台。

Context构建完成后不会随Repository中的原始记录变化。

## 扩展Parser和Resolver

工具通过Registry管理Parser和SourceResolver：

```csharp
RuntimeErrorContextRegistry.RegisterParser(customParser);
RuntimeErrorContextRegistry.RegisterSourceResolver(customResolver);
```

适合扩展：

- 其他Lua框架或特殊日志格式。
- 加密Lua和 `.bytes` 文件。
- AssetBundle中的Lua Source Manifest。
- 公司内部Logger格式。
- 项目外部共享源码目录。

Parser、Resolver及其注册方法是公开API，可以从独立的Editor程序集注册扩展。自定义Resolver无法解析某个栈帧时应返回 `false`，Registry会继续尝试后续Resolver。

## 源码跳转

- `Assets`、`Packages` 下的 C#源码通过 `AssetDatabase.OpenAsset` 打开。
- Assets外部的 Lua源码使用 Unity Preferences中配置的默认脚本编辑器。
- 已支持 Rider、IntelliJ IDEA、VS Code、Cursor和 Visual Studio参数。
- 工具不对 `Unity.CodeEditor` 产生编译期依赖。

如果无法打开Lua源码，请先确认：

1. Unity Preferences中已经配置 External Script Editor。
2. Lua源码根目录配置正确。
3. 日志中的相对路径能够映射到本地源码。

## 常见问题

### Lua错误可以收集，但没有源码

检查 `Lua源码根目录`，确保配置的是包含 `model/`、`uisystem/` 等目录的上一级目录。

### Lua主动错误显示为运行时异常

将实际日志前缀加入 `Lua Logger前缀`。

### 主位置落在 Logger.lua

将Logger文件加入 `忽略的 Lua栈帧`。

### C#错误定位到第三方目录

将目录加入 `忽略的 C#路径`。

### 修改Settings后旧记录没有变化

已有错误不会重新解析。请清空列表并重新复现错误。

## 当前限制

- 只收集 Editor Play Mode日志。
- 不处理IL2CPP Native Crash和真机符号化。
- 不采集局部变量、upvalue或GameObject状态。
- 当前只读取主错误位置的一份源码上下文。
- 特殊Lua虚拟路径可能需要自定义 `ISourceResolver`。
- 工具不会自动调用AI，也不会自动修改业务代码。

## 安全边界

- 工具代码只在Editor中编译。
- 默认关闭Player运行时依赖。
- 不修改XLua、Logger和游戏业务。
- 不上传日志或源码。
- “复制分析上下文”只写入本机剪贴板。
