# Unity Error Context Tool

在 Unity Editor 中收集 C# / Lua 运行时日志，定位源码，并把当前选中的错误整理成可直接交给 Agent 的 Markdown 上下文。

**使用流程：复现错误 → 搜索并选中 → 查看源码 → 复制或同步给 Agent。**

## 功能概览

| 能力 | 说明 |
| --- | --- |
| 错误采集与聚合 | 收集 Play Mode 中的 Exception、Error、Assert；相同错误累加次数并保留最新样本 |
| C# / Lua 定位 | 解析调用栈，定位 Assets、Packages 或配置的 Lua 根目录下的源码 |
| 源码上下文 | 窗口预览主位置上下各 5 行；导出主位置及最多两个业务调用位置源码 |
| 搜索错误 | 搜索消息、类型、文件路径和函数名，支持大小写无关匹配 |
| 稳定浏览 | 强化选中标记；列表重排时补偿可见选中项的滚动位置 |
| Agent 文件同步 | 自动更新宿主项目的 `.error-context/current.md`，无需反复复制粘贴 |
| 一键项目接入 | 补充 AGENTS.md 读取约定和 Git 忽略规则，保留原有内容 |
| IDE 跳转 | 使用 Unity 配置的默认脚本编辑器打开主错误位置 |

工具仅在 Editor 中编译，不进入 Player；不依赖 XLua 程序集、AI SDK 或 MCP，不自动上传日志、启动 AI 分析或修改业务代码。

## 排错流程

1. 打开 **Tools > Runtime Error Context**。
2. 确认“收集运行时错误”开启，进入 Play Mode 并复现问题。
3. 在列表中搜索并选中目标错误，查看错误消息、位置、附近源码和调用栈。
4. 点击“打开源码”跳转 IDE，或点击“复制分析上下文”取得 Markdown。

Lua 项目需要在 **Tools > Runtime Error Context Settings** 中设置源码根目录。例如日志为 `model/UserModel.lua:1671`，实际文件为 `SourceRes/Lua/model/UserModel.lua`，根目录填写 `SourceRes/Lua`。

## 不复制，直接交给 Agent

1. 点击“配置 Agent 接入”，自动补充目标项目的 `AGENTS.md` 和 `.gitignore`。已有内容保留，重复配置不会重复追加。
2. 选中错误，点击“同步给 Agent”。
3. 在能够访问**该游戏项目目录**的 Agent 中说：**“分析当前 Unity 错误。”**

生成文件：

```text
游戏项目/
├── Assets/
├── Packages/
├── AGENTS.md
└── .error-context/
    └── current.md
```

文件只包含当前选中的一条错误。切换选中项会立即更新；同一错误重复发生时最多每 0.5 秒合并更新一次。其他错误不会抢占同步目标。搜索隐藏当前选中项时，详情和同步目标仍保留。

清空记录、停止同步、同步记录被淘汰或 Domain Reload 时删除快照。文件更新不会自动唤醒 Agent；需要在 Agent 中发起分析。源码读取自生成上下文时的磁盘文件，不是报错时的历史版本。

## 标签与边界

列表标签表示 **报错位置 + Unity 原始日志类型**，例如 `[Lua] [错误]`、`[C#] [异常]`。位置语言表示建议先检查的代码，不等于已确认根因；日志类型不判断是否主动打印或是否已捕获。

例如 `pcall` 捕获的 Lua 异常通过 LogError 输出，仍显示“错误”。捕获后生成的 traceback 可能只是日志打印栈，工具不能恢复已经丢失的原始异常调用链。

目前不提供 JSON 导出、真机日志接入、IL2CPP 符号化或局部变量采集。记录保存在内存中，不提供跨会话历史记录。

## 文档与验证

详见 [功能说明、设置、扩展接口及常见问题](Assets/RuntimeErrorContext/README.md)。

测试程序集为 `RuntimeErrorContext.Editor.Tests`，通过 Unity Test Runner 的 EditMode 执行。最近一次完整回归为 **62 项全部通过**，包含搜索、选中项滚动补偿、Lua 前缀路径解析、Agent 配置与同步。集成测试会切换 Play Mode，建议使用独立验证工程。

实际 IDE 跳转及界面交互仍需在使用环境中验证。
