# Unity Error Context Tool

一个轻量的 Unity Editor 收集运行时错误并构建上下文的工具。
在 Play Mode 中收集 C# 与 Lua 错误，并生成包含调用栈、源码位置和相关代码片段的分析上下文。

## 功能概览

- 收集并聚合 `Exception`、`Error` 和 `Assert`
- 解析 C#、Lua 及混合调用栈
- 定位 `Assets/`、`Packages/` 和已配置 Lua 根目录中的源码
- 支持用 Unity 默认 IDE 打开错误位置
- 生成适合交给 Codex、ChatGPT 等 Agent 的 Markdown 上下文
- Editor-only，不会进入 Player，也不会上传日志或源码

## 环境

- Unity 2022.3 或更高版本
- 当前工程使用 Unity 2022.3.62f3

## 使用

打开工程后，通过以下菜单启动工具：

```text
Tools > Runtime Error Context
```

完整安装方法、配置项和扩展接口请参阅[工具文档](Assets/RuntimeErrorContext/README.md)。
