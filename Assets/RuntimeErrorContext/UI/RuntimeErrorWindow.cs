using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class RuntimeErrorWindow : EditorWindow
    {
        private const float ListWidth = 300f;
        private const int SourcePreviewLineCount = 5;
        private const float ListItemHeight = 68f;
        private const float ListItemHorizontalIndent = 12f;
        private readonly ErrorContextBuilder m_ContextBuilder = new ErrorContextBuilder();

        private Vector2 m_ListScroll;
        private Vector2 m_DetailScroll;
        private RuntimeErrorRecord m_SelectedRecord;
        private ErrorContext m_SelectedContext;
        private GUIStyle m_ListItemStyle;
        private bool m_ResetDetailFocus;
        private bool m_AgentConfigured;
        private string m_SetupMessage;
        private bool m_ShowAgentSettings;
        private string m_SearchQuery = string.Empty;
        private UnityEditor.IMGUI.Controls.SearchField m_SearchField;
        private List<RuntimeErrorRecord> m_PreviousVisibleRecords;
        private float m_ListViewportHeight;
        private const float ListItemSpacing = 4f;

        [MenuItem("Tools/Runtime Error Context")]
        private static void Open()
        {
            RuntimeErrorWindow window = GetWindow<RuntimeErrorWindow>();
            window.titleContent = new GUIContent("Runtime Errors");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAgentSetup();
            RuntimeErrorRepository.Changed += OnRepositoryChanged;
            RuntimeErrorCollector.StateChanged += Repaint;
            AgentContextSync.Changed += Repaint;
        }

        private void OnDisable()
        {
            RuntimeErrorRepository.Changed -= OnRepositoryChanged;
            RuntimeErrorCollector.StateChanged -= Repaint;
            AgentContextSync.Changed -= Repaint;
        }

        private void OnGUI()
        {
            if (m_ResetDetailFocus)
            {
                // IMGUI会复用文本控件的编辑缓存，切换记录时释放旧文本焦点。
                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;
                m_ResetDetailFocus = false;
            }

            DrawToolbar();
            DrawAgentSetup();
            if (!string.IsNullOrEmpty(AgentContextSync.LastError))
                EditorGUILayout.HelpBox(AgentContextSync.LastError, MessageType.Warning);
            EditorGUILayout.Space(2f);

            EditorGUILayout.BeginHorizontal();
            DrawErrorList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool collecting = GUILayout.Toggle(RuntimeErrorCollector.IsCollecting, "收集运行时错误", EditorStyles.toolbarButton);
            if (collecting != RuntimeErrorCollector.IsCollecting)
            {
                RuntimeErrorCollector.SetCollecting(collecting);
            }

            GUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(!AgentContextSync.IsEnabled && m_SelectedRecord == null);
            if (GUILayout.Button(AgentContextSync.IsEnabled ? "停止同步给 Agent" : "同步给 Agent", EditorStyles.toolbarButton))
                ToggleAgentSync();
            EditorGUI.EndDisabledGroup();
            GUILayout.Label(GetStatusText(), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("错误数: " + RuntimeErrorRepository.Count, EditorStyles.miniLabel);
            if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                RuntimeErrorCollector.Clear();
                ClearSelection();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void OnFocus() { RefreshAgentSetup(); }

        private void RefreshAgentSetup()
        {
            m_AgentConfigured = AgentProjectSetup.IsConfigured(AgentProjectSetup.ProjectRoot);
        }

        private bool ConfigureAgent()
        {
            bool success = AgentProjectSetup.Configure(AgentProjectSetup.ProjectRoot, out m_SetupMessage);
            RefreshAgentSetup();
            m_ShowAgentSettings = !success;
            if (success) m_SetupMessage = null;
            return success;
        }

        private void ToggleAgentSync()
        {
            if (AgentContextSync.IsEnabled)
            {
                AgentContextSync.SetEnabled(false, null);
                return;
            }
            RefreshAgentSetup();
            if (!m_AgentConfigured)
            {
                int choice = EditorUtility.DisplayDialogComplex("配置 Agent 接入",
                    "是否同时配置当前项目的 Agent 读取约定？将更新 AGENTS.md 和 .gitignore，保留已有内容。",
                    "配置并同步", "取消", "仅同步");
                if (choice == 1 || (choice == 0 && !ConfigureAgent())) return;
            }
            AgentContextSync.SetEnabled(true, m_SelectedRecord);
        }

        private void DrawAgentSetup()
        {
            bool hasSnapshot = AgentContextSync.LastWrittenAt != default(DateTime)
                && string.IsNullOrEmpty(AgentContextSync.LastError) && System.IO.File.Exists(AgentContextSync.OutputPath);
            string status = m_AgentConfigured ? "Agent：已就绪" : "Agent：尚未配置";
            if (!string.IsNullOrEmpty(AgentContextSync.LastError)) status = "Agent：同步失败";
            else if (hasSnapshot)
                status = "已同步：" + AgentContextSync.LastLocation + " · " + AgentContextSync.LastWrittenAt.ToString("HH:mm:ss");
            else if (AgentContextSync.IsEnabled) status = "Agent：请选择错误以继续同步";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(status, status), EditorStyles.miniLabel, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            if (hasSnapshot && GUILayout.Button("查看上下文文件", GUILayout.Width(110f)))
                EditorUtility.RevealInFinder(AgentContextSync.OutputPath);
            if (GUILayout.Button(m_ShowAgentSettings ? "设置 ▴" : "设置 ▾", GUILayout.Width(64f)))
            {
                m_ShowAgentSettings = !m_ShowAgentSettings;
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            // 未配置时保留首次接入引导；日常使用只占一行。
            if (!m_AgentConfigured || m_ShowAgentSettings)
            {
                EditorGUILayout.LabelField("在当前项目的 Agent 中说“分析当前 Unity 错误”。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.SelectableLabel(AgentContextSync.OutputPath, EditorStyles.miniLabel, GUILayout.Height(18f));
                if (!string.IsNullOrEmpty(m_SetupMessage))
                    EditorGUILayout.LabelField(m_SetupMessage, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(m_AgentConfigured ? "更新接入配置" : "配置 Agent 接入", GUILayout.Width(120f)))
                {
                    ConfigureAgent();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawErrorList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            EditorGUILayout.LabelField("错误列表", EditorStyles.boldLabel);
            if (m_SearchField == null) m_SearchField = new UnityEditor.IMGUI.Controls.SearchField();
            string query = m_SearchField.OnGUI(m_SearchQuery);
            if (query != m_SearchQuery)
            {
                SetSearchQuery(query);
                // 筛选会改变列表控件数量，下一轮 Layout 再绘制。
                GUIUtility.ExitGUI();
            }
            List<RuntimeErrorRecord> allRecords = RuntimeErrorRepository.GetRecords();
            List<RuntimeErrorRecord> records = allRecords.FindAll(record => MatchesSearch(record, m_SearchQuery));
            PreserveSelectedListPosition(records);
            if (!string.IsNullOrWhiteSpace(m_SearchQuery))
                EditorGUILayout.LabelField("匹配 " + records.Count + " / " + allRecords.Count + " 条", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("搜索消息、文件路径或函数名", EditorStyles.miniLabel);
            if (m_SelectedRecord != null && !records.Contains(m_SelectedRecord))
                EditorGUILayout.HelpBox("当前选中错误已被筛选隐藏，详情和同步目标保持不变。", MessageType.None);
            m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll, false, true);

            if (records.Count == 0)
            {
                EditorGUILayout.HelpBox(allRecords.Count > 0 ? "没有匹配的错误，请修改或清除搜索条件。" : RuntimeErrorCollector.IsCollecting
                    ? "尚未收集到运行时错误。"
                    : "采集已关闭，请手动开启。", MessageType.Info);
            }

            for (int i = 0; i < records.Count; i++)
            {
                RuntimeErrorRecord record = records[i];
                bool selected = ReferenceEquals(record, m_SelectedRecord);
                string location = GetCompactLocationText(record.Error.PrimaryFrame);
                string label = BuildListLabel(record, location);

                Rect itemRect = GUILayoutUtility.GetRect(0f, ListItemHeight, GUILayout.ExpandWidth(true));
                Rect cardRect = new Rect(itemRect.x + 6f, itemRect.y, Mathf.Max(0f, itemRect.width - 12f), itemRect.height);
                bool hovered = cardRect.Contains(Event.current.mousePosition);
                Color backgroundColor = selected
                    ? new Color(0.16f, 0.30f, 0.43f, 1f)
                    : hovered
                        ? new Color(0.22f, 0.22f, 0.22f, 1f)
                        : new Color(0.17f, 0.17f, 0.17f, 1f);

                EditorGUI.DrawRect(cardRect, backgroundColor);
                if (selected)
                {
                    Color selectionColor = new Color(0.40f, 0.72f, 1f, 1f);
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 4f, cardRect.height), selectionColor);
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), selectionColor);
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), selectionColor);
                    EditorGUI.DrawRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), selectionColor);
                }

                bool clicked = GUI.Button(cardRect, GUIContent.none, GUIStyle.none);
                Rect textRect = new Rect(
                    cardRect.x + ListItemHorizontalIndent,
                    cardRect.y + 5f,
                    Mathf.Max(0f, cardRect.width - ListItemHorizontalIndent * 2f),
                    cardRect.height - 10f);
                GUI.Label(textRect, new GUIContent(label, GetClassificationDescription(GetPrimaryLanguage(record.Error.PrimaryFrame), record.Error.LogType)), GetListItemStyle());
                if (clicked)
                {
                    SelectRecord(record);
                    // 不同记录的详情控件数量可能不同，下一轮Layout再绘制新详情。
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(ListItemSpacing);
            }

            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
                m_ListViewportHeight = GUILayoutUtility.GetLastRect().height;
            EditorGUILayout.EndVertical();
        }

        private void PreserveSelectedListPosition(List<RuntimeErrorRecord> records)
        {
            if (m_SelectedRecord != null && m_PreviousVisibleRecords != null)
            {
                int previousIndex = m_PreviousVisibleRecords.IndexOf(m_SelectedRecord);
                int currentIndex = records.IndexOf(m_SelectedRecord);
                float stride = ListItemHeight + ListItemSpacing;
                float previousTop = previousIndex * stride;
                // 用户已滚离选中项时不抢滚动；搜索隐藏或记录淘汰也不自动选中其他记录。
                if (previousIndex >= 0 && currentIndex >= 0 && previousIndex != currentIndex
                    && m_ListViewportHeight > 0f
                    && previousTop + ListItemHeight > m_ListScroll.y
                    && previousTop < m_ListScroll.y + m_ListViewportHeight)
                {
                    float adjusted = m_ListScroll.y + (currentIndex - previousIndex) * stride;
                    m_ListScroll.y = Mathf.Clamp(adjusted, 0f,
                        Mathf.Max(0f, records.Count * stride - m_ListViewportHeight));
                }
            }
            m_PreviousVisibleRecords = records;
        }

        private void SetSearchQuery(string query)
        {
            m_SearchQuery = query ?? string.Empty;
            m_ListScroll = Vector2.zero;
            m_PreviousVisibleRecords = null;
            Repaint();
        }

        internal static bool MatchesSearch(RuntimeErrorRecord record, string query)
        {
            string term = (query ?? string.Empty).Trim();
            if (term.Length == 0) return true;
            ParsedError error = record != null ? record.Error : null;
            if (error == null) return false;
            if (ContainsSearch(error.Message, term) || ContainsSearch(error.RawMessage, term)
                || ContainsSearch(error.ErrorType, term) || FrameMatchesSearch(error.PrimaryFrame, term)) return true;
            return error.StackFrames != null && error.StackFrames.Exists(frame => FrameMatchesSearch(frame, term));
        }

        private static bool FrameMatchesSearch(ErrorStackFrame frame, string term)
        {
            if (frame == null) return false;
            string pathTerm = term.Replace('\\', '/');
            return ContainsSearch(frame.FunctionName, term)
                || ContainsSearch((frame.RuntimeFilePath ?? string.Empty).Replace('\\', '/'), pathTerm)
                || ContainsSearch((frame.SourceFilePath ?? string.Empty).Replace('\\', '/'), pathTerm);
        }

        private static bool ContainsSearch(string text, string term)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("错误详情", EditorStyles.boldLabel);

            if (m_SelectedContext == null)
            {
                EditorGUILayout.HelpBox("请从左侧选择一条错误。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            ErrorContextInfo error = m_SelectedContext.Error;
            m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll, GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(error.ErrorType, EditorStyles.boldLabel);
            ErrorContextLocation primary = m_SelectedContext.PrimaryLocation;
            ErrorLanguage locationLanguage = GetLocationLanguage(primary.Language, primary.RuntimeFilePath, primary.LineNumber);
            GUILayout.Label(new GUIContent(GetClassificationLabel(locationLanguage, error.LogType) + "  ×"
                + m_SelectedContext.Aggregation.OccurrenceCount,
                GetClassificationDescription(locationLanguage, error.LogType)), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Unity 日志类型：" + error.LogType, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("错误信息", EditorStyles.boldLabel);
            DrawReadOnlyText("RuntimeErrorContext.Message", error.Message, 44f);
            EditorGUILayout.LabelField(new GUIContent("位置"), new GUIContent(
                GetLocationText(m_SelectedContext.PrimaryLocation), GetLocationText(m_SelectedContext.PrimaryLocation)));
            if (error.Language == ErrorLanguage.Mixed)
                EditorGUILayout.LabelField("调用栈：包含 Lua 和 C#", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            if (m_SelectedContext.SourceContexts.Count > 0)
            {
                EditorGUILayout.LabelField("相关源码", EditorStyles.boldLabel);
                DrawReadOnlyText("RuntimeErrorContext.Source", m_SelectedContext.SourceContexts[0].Content, 180f);
            }
            else
            {
                bool luaLocation = m_SelectedContext.PrimaryLocation.Language == ErrorLanguage.Lua;
                bool hasRoots = RuntimeErrorContextSettings.Current.LuaSourceRoots.Exists(root => !string.IsNullOrWhiteSpace(root));
                string sourceMessage = m_SelectedContext.PrimaryLocation.IsResolved
                    ? "已定位到源码文件，但无法读取对应行，请检查文件访问权限和行号是否超出范围。"
                    : luaLocation && hasRoots
                        ? "已配置 Lua 根目录，但未找到该位置的源码，请检查日志路径与根目录映射。"
                        : "未定位到可读取的项目源码。";
                EditorGUILayout.HelpBox(sourceMessage, MessageType.None);
                if (luaLocation && !m_SelectedContext.PrimaryLocation.IsResolved)
                {
                    if (GUILayout.Button(hasRoots ? "检查 Lua 路径配置" : "配置 Lua 源码根目录"))
                        SettingsService.OpenUserPreferences("Preferences/Runtime Error Context");
                }
            }
            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(error.LuaStackTrace))
            {
                EditorGUILayout.LabelField("Lua 调用栈", EditorStyles.boldLabel);
                DrawReadOnlyText("RuntimeErrorContext.LuaStack", error.LuaStackTrace, 130f);
                EditorGUILayout.Space();
            }

            if (!string.IsNullOrEmpty(error.CSharpStackTrace))
            {
                EditorGUILayout.LabelField(error.Language == ErrorLanguage.Lua || error.Language == ErrorLanguage.Mixed ? "C# 外层调用栈" : "C# 调用栈",
                    EditorStyles.boldLabel);
                DrawReadOnlyText("RuntimeErrorContext.CSharpStack", error.CSharpStackTrace, 130f);
            }


            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(m_SelectedContext == null || !m_SelectedContext.PrimaryLocation.IsResolved);
            if (GUILayout.Button("打开源码", GUILayout.Height(26f)))
            {
                OpenSelectedSource();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("复制分析上下文", GUILayout.Height(26f)))
            {
                ErrorContext currentContext = m_ContextBuilder.Build(m_SelectedRecord, includeCallers: true);
                GUIUtility.systemCopyBuffer = ErrorContextFormatter.ToMarkdown(currentContext);
                ShowNotification(new GUIContent("分析上下文已复制"));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawReadOnlyText(string controlName, string content, float minHeight)
        {
            string text = content ?? string.Empty;
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.textArea,
                GUILayout.MinHeight(minHeight), GUILayout.ExpandWidth(true));
            GUI.SetNextControlName(controlName);
            // 详情是快照，只允许选择复制；按内容计算高度，避免长源码被裁切。
            EditorGUI.SelectableLabel(rect, text, EditorStyles.textArea);
        }

        private void SelectRecord(RuntimeErrorRecord record)
        {
            m_SelectedRecord = record;
            m_SelectedContext = m_ContextBuilder.Build(record, SourcePreviewLineCount);
            AgentContextSync.Select(record);
            m_DetailScroll = Vector2.zero;
            EditorGUIUtility.editingTextField = false;
            m_ResetDetailFocus = true;
            Repaint();
        }

        private void OpenSelectedSource()
        {
            ErrorContextLocation location = m_SelectedContext.PrimaryLocation;
            bool opened;
            if (location.SourceFilePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || location.SourceFilePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(location.SourceFilePath);
                opened = asset != null && AssetDatabase.OpenAsset(asset, location.LineNumber);
                if (!opened)
                {
                    opened = OpenExternalSourceWithDefaultEditor(location.AbsolutePath, location.LineNumber);
                }
            }
            else
            {
                opened = OpenExternalSourceWithDefaultEditor(
                    location.AbsolutePath, location.LineNumber);
            }

            if (!opened)
            {
                ShowNotification(new GUIContent("无法打开源码，请检查外部代码编辑器设置"));
            }
        }

        private void OnRepositoryChanged()
        {
            if (m_SelectedRecord != null)
            {
                List<RuntimeErrorRecord> records = RuntimeErrorRepository.GetRecords();
                if (!records.Contains(m_SelectedRecord))
                {
                    ClearSelection();
                }
                else if (m_SelectedContext == null
                         || m_SelectedContext.Aggregation.OccurrenceCount != m_SelectedRecord.OccurrenceCount
                         || m_SelectedContext.Aggregation.LastOccurredAt != m_SelectedRecord.LastOccurredAt)
                {
                    m_SelectedContext = m_ContextBuilder.Build(m_SelectedRecord, SourcePreviewLineCount);
                }
            }

            Repaint();
        }

        private void ClearSelection()
        {
            m_SelectedRecord = null;
            m_SelectedContext = null;
            EditorGUIUtility.editingTextField = false;
            m_ResetDetailFocus = true;
        }

        private static string GetStatusText()
        {
            if (!RuntimeErrorCollector.IsCollecting)
            {
                return "状态：未收集";
            }

            string status = EditorApplication.isPlaying ? "状态：正在收集" : "状态：已开启，等待 Play";
            if (RuntimeErrorCollector.DroppedEventCount > 0)
            {
                status += "；队列已满，丢弃 " + RuntimeErrorCollector.DroppedEventCount + " 条";
            }
            return status;
        }

        private static string GetLocationText(ErrorContextLocation location)
        {
            if (location == null || string.IsNullOrEmpty(location.RuntimeFilePath))
            {
                return "未解析";
            }

            string path = !string.IsNullOrEmpty(location.SourceFilePath)
                ? location.SourceFilePath
                : location.RuntimeFilePath;
            return path + ":" + location.LineNumber;
        }

        private static string GetSummary(string message)
        {
            string value = string.IsNullOrEmpty(message) ? "无错误信息" : message;
            int lineEnd = value.IndexOf('\n');
            if (lineEnd >= 0)
            {
                value = value.Substring(0, lineEnd);
            }

            const int maxLength = 34;
            return value.Length > maxLength ? value.Substring(0, maxLength) + "..." : value;
        }

        private static string BuildListLabel(RuntimeErrorRecord record, string location)
        {
            ErrorLanguage primaryLanguage = GetPrimaryLanguage(record.Error.PrimaryFrame);
            string languageTag = ColorText("[" + GetLanguageText(primaryLanguage) + "]",
                GetLanguageColor(primaryLanguage));
            string kindTag = ColorText("[" + GetLogTypeText(record.Error.LogType) + "]", GetLogTypeColor(record.Error.LogType));
            string content = string.Format("  x{0}\n{1}\n{2}", record.OccurrenceCount,
                GetSummary(record.Error.Message), location);
            return languageTag + " " + kindTag + ColorText(content, new Color(0.92f, 0.92f, 0.92f, 1f));
        }

        private static string GetCompactLocationText(ErrorStackFrame frame)
        {
            if (frame == null || string.IsNullOrEmpty(frame.RuntimeFilePath))
            {
                return "未解析";
            }

            string path = frame.RuntimeFilePath.Replace('\\', '/');
            int lastSlash = path.LastIndexOf('/');
            int previousSlash = lastSlash > 0 ? path.LastIndexOf('/', lastSlash - 1) : -1;
            if (previousSlash >= 0)
            {
                path = path.Substring(previousSlash + 1);
            }

            return path + ":" + frame.LineNumber;
        }

        private GUIStyle GetListItemStyle()
        {
            if (m_ListItemStyle != null)
            {
                return m_ListItemStyle;
            }

            m_ListItemStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                richText = true,
                wordWrap = false
            };
            Color textColor = new Color(0.88f, 0.88f, 0.88f);
            m_ListItemStyle.normal.textColor = textColor;
            m_ListItemStyle.hover.textColor = textColor;
            m_ListItemStyle.active.textColor = textColor;
            m_ListItemStyle.focused.textColor = textColor;
            return m_ListItemStyle;
        }

        private static void DrawColoredDetailField(string label, string value, Color color)
        {
            Color originalColor = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(label, value);
            GUI.contentColor = originalColor;
        }

        private static bool OpenExternalSourceWithDefaultEditor(string absolutePath, int lineNumber)
        {
            string editorPath = GetConfiguredEditorPath();
            if (string.IsNullOrEmpty(editorPath) || !System.IO.File.Exists(editorPath))
            {
                return false;
            }

            string editorName = System.IO.Path.GetFileNameWithoutExtension(editorPath).ToLowerInvariant();
            string arguments;
            if (editorName.Contains("rider") || editorName.Contains("idea"))
            {
                arguments = string.Format("--line {0} \"{1}\"", lineNumber, absolutePath);
            }
            else if (editorName.Contains("code") || editorName.Contains("cursor"))
            {
                arguments = string.Format("--goto \"{0}:{1}:1\"", absolutePath, lineNumber);
            }
            else if (editorName.Contains("devenv"))
            {
                arguments = string.Format("\"{0}\" /Command \"Edit.Goto {1}\"", absolutePath, lineNumber);
            }
            else
            {
                arguments = string.Format("\"{0}\"", absolutePath);
            }

            try
            {
                System.Diagnostics.Process process = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = editorPath,
                        Arguments = arguments,
                        UseShellExecute = true
                    });
                return process != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetConfiguredEditorPath()
        {
            string editorPath = EditorPrefs.GetString("kScriptsDefaultApp", string.Empty);
            if (!string.IsNullOrEmpty(editorPath) && System.IO.File.Exists(editorPath))
            {
                return editorPath;
            }

            // IDE Package存在时通过反射读取，不形成编译期依赖。
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type codeEditorType = assemblies[i].GetType("Unity.CodeEditor.CodeEditor", false);
                if (codeEditorType == null)
                {
                    continue;
                }

                System.Reflection.PropertyInfo property = codeEditorType.GetProperty(
                    "CurrentEditorInstallation",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                editorPath = property != null ? property.GetValue(null, null) as string : string.Empty;
                if (!string.IsNullOrEmpty(editorPath))
                {
                    return editorPath;
                }
            }

            return string.Empty;
        }

        private static string ColorText(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        private static Color GetLanguageColor(ErrorLanguage language)
        {
            switch (language)
            {
                case ErrorLanguage.CSharp:
                    return new Color(0.40f, 0.70f, 1f, 1f);
                case ErrorLanguage.Lua:
                    return new Color(0.75f, 0.52f, 1f, 1f);
                case ErrorLanguage.Mixed:
                    return new Color(0.45f, 0.85f, 0.85f, 1f);
                default:
                    return new Color(0.70f, 0.70f, 0.70f, 1f);
            }
        }

        private static Color GetLogTypeColor(LogType type)
        {
            switch (type)
            {
                case LogType.Exception: return new Color(1f, 0.48f, 0.48f);
                case LogType.Error: return new Color(1f, 0.74f, 0.36f);
                case LogType.Assert: return new Color(1f, 0.84f, 0.40f);
                default: return Color.gray;
            }
        }

        internal static ErrorLanguage GetPrimaryLanguage(ErrorStackFrame frame)
        {
            return frame == null ? ErrorLanguage.Unknown
                : GetLocationLanguage(frame.Language, frame.RuntimeFilePath, frame.LineNumber);
        }

        private static ErrorLanguage GetLocationLanguage(ErrorLanguage language, string path, int line)
        {
            return !string.IsNullOrEmpty(path) && line > 0
                && (language == ErrorLanguage.Lua || language == ErrorLanguage.CSharp)
                ? language : ErrorLanguage.Unknown;
        }

        internal static string GetClassificationLabel(ErrorLanguage language, LogType type)
        {
            return "[" + GetLanguageText(language) + "] [" + GetLogTypeText(type) + "]";
        }

        private static string GetClassificationDescription(ErrorLanguage language, LogType type)
        {
            return "报错位置：" + GetLanguageText(language)
                + "。按主错误位置判断，表示建议优先检查的代码，不保证本地源码可读取或已确认根因。"
                + "\n按 Unity 原始日志类型显示，不推断是否主动打印或捕获异常。"
                + "\nUnity 原始日志类型：" + type + "。分类不表示严重程度或业务是否中断。";
        }

        private static string GetLanguageText(ErrorLanguage language)
        {
            switch (language)
            {
                case ErrorLanguage.CSharp: return "C#";
                case ErrorLanguage.Lua: return "Lua";
                default: return "未定位";
            }
        }

        private static string GetLogTypeText(LogType type)
        {
            switch (type)
            {
                case LogType.Exception: return "异常";
                case LogType.Error: return "错误";
                case LogType.Assert: return "断言失败";
                default: return type.ToString();
            }
        }
    }
}
