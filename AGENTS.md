# 项目协作约定

## 定位与结构

- Unity Editor 错误上下文工具，当前工程版本为 2022.3.62f3，最低版本声明为 2022.3。
- 工具代码位于 `Assets/RuntimeErrorContext/`，由 Editor-only asmdef 隔离；不直接依赖 XLua、AI SDK 或游戏业务代码。
- 数据流：`Capture → Parsing → Core/Repository → Context + Source → UI / Markdown`。
- `Capture` 接收并缓存日志；`Parsing` 提取错误和栈帧；`Core` 管理模型、聚合、配置和注册；`Source` 定位并读取源码；`Context` 构建快照和 Markdown；`UI` 使用 IMGUI 展示、复制和跳转。
- 使用说明和配置约定见 `Assets/RuntimeErrorContext/README.md`。

## 修改边界

- 保持 Editor-only，不向 Player 引入依赖；不自动上传日志、调用 AI 或修改业务源码。
- 日志回调可能来自后台线程，只做轻量入队；解析、Unity 编辑器 API 和界面更新在主线程执行。保持队列有界，“清空”必须同时清除待处理日志和聚合记录。
- 相同指纹累加次数、保留首次时间，并整体替换为最新样本；原始日志和解析栈必须一致。选中详情随记录更新。
- 保留 Lua traceback 的重复调用层次；同时包含 Lua 和 C# 帧时标记 `Mixed`。
- Context 与 Repository 数据分离；Resolver 使用栈帧副本。扩展失败应回退，避免产生被自身重复采集的错误日志。
- 包源码通过 PackageManager 的实际目录定位；Lua 使用配置根目录。保留路径范围检查，不将越界行号伪装成有效源码位置。
- 公开扩展入口为 `RuntimeErrorContextRegistry`、`IErrorParser`、`ISourceResolver`；修改接口时考虑独立 Editor 程序集的兼容性。
- 配置保存在本机 EditorPrefs，采集开关使用 SessionState；错误记录仅在内存中。场景名来自采集时缓存，源码来自构建上下文时的磁盘文件。

## 验证与维护

- 行为改动运行 `Tests/Editor` 下的相关测试；通过 Unity Test Runner 的 EditMode 执行 `RuntimeErrorContext.Editor.Tests`。集成测试会切换 Play Mode，优先在独立验证工程运行。
- 日志、解析、聚合和定位改动应覆盖相应边界用例；外部 IDE 实际启动与跳转需要单独验证，不能以自动化测试通过代替。
- 新增或移动 Unity 资产时保留对应 `.meta`；不要手改自动生成的 `.csproj`、`.sln`，不要提交 `Library/`、`Temp/`、`Logs/`。
- 保留用户已有改动；仅因本次功能需要才修改 Packages 或 ProjectSettings。行为或支持版本变化时同步 README。
