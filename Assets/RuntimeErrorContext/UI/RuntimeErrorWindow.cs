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
            RuntimeErrorRepository.Changed += OnRepositoryChanged;
            RuntimeErrorCollector.StateChanged += Repaint;
        }

        private void OnDisable()
        {
            RuntimeErrorRepository.Changed -= OnRepositoryChanged;
            RuntimeErrorCollector.StateChanged -= Repaint;
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

        private void DrawErrorList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            EditorGUILayout.LabelField("错误列表", EditorStyles.boldLabel);
            m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll, false, true);

            List<RuntimeErrorRecord> records = RuntimeErrorRepository.GetRecords();
            if (records.Count == 0)
            {
                EditorGUILayout.HelpBox(RuntimeErrorCollector.IsCollecting
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
                    ? new Color(0.25f, 0.29f, 0.34f, 1f)
                    : hovered
                        ? new Color(0.22f, 0.22f, 0.22f, 1f)
                        : new Color(0.17f, 0.17f, 0.17f, 1f);

                EditorGUI.DrawRect(cardRect, backgroundColor);
                if (selected)
                {
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 3f, cardRect.height),
                        GetKindColor(record.Error.Kind));
                }

                bool clicked = GUI.Button(cardRect, GUIContent.none, GUIStyle.none);
                Rect textRect = new Rect(
                    cardRect.x + ListItemHorizontalIndent,
                    cardRect.y + 5f,
                    Mathf.Max(0f, cardRect.width - ListItemHorizontalIndent * 2f),
                    cardRect.height - 10f);
                GUI.Label(textRect, label, GetListItemStyle());
                if (clicked)
                {
                    SelectRecord(record);
                    // 不同记录的详情控件数量可能不同，下一轮Layout再绘制新详情。
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(4f);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
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
            DrawColoredDetailField("语言", GetLanguageText(error.Language), GetLanguageColor(error.Language));
            DrawColoredDetailField("错误性质", GetKindText(error.Kind), GetKindColor(error.Kind));
            DrawColoredDetailField("错误类型", error.ErrorType, GetKindColor(error.Kind));
            EditorGUILayout.LabelField("次数", m_SelectedContext.Aggregation.OccurrenceCount.ToString());
            EditorGUILayout.LabelField("位置", GetLocationText(m_SelectedContext.PrimaryLocation));
            EditorGUILayout.Space();

            if (m_SelectedContext.SourceContexts.Count > 0)
            {
                EditorGUILayout.LabelField("相关源码", EditorStyles.boldLabel);
                DrawReadOnlyText("RuntimeErrorContext.Source", m_SelectedContext.SourceContexts[0].Content, 180f);
            }
            else
            {
                EditorGUILayout.HelpBox("未定位到可读取的项目源码。", MessageType.None);
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("错误信息", EditorStyles.boldLabel);
            DrawReadOnlyText("RuntimeErrorContext.Message", error.Message, 70f);
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
                ErrorContext currentContext = m_ContextBuilder.Build(m_SelectedRecord);
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
            string languageTag = ColorText("[" + GetLanguageText(record.Error.Language) + "]",
                GetLanguageColor(record.Error.Language));
            string kindTag = ColorText("[" + GetKindText(record.Error.Kind) + "]",
                GetKindColor(record.Error.Kind));
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

        private static Color GetKindColor(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.RuntimeException:
                    return new Color(1f, 0.38f, 0.38f, 1f);
                case ErrorKind.CaughtExceptionLog:
                    return new Color(1f, 0.68f, 0.30f, 1f);
                case ErrorKind.ManualErrorLog:
                    return new Color(1f, 0.84f, 0.35f, 1f);
                case ErrorKind.Assert:
                    return new Color(0.95f, 0.78f, 0.28f, 1f);
                default:
                    return new Color(0.70f, 0.70f, 0.70f, 1f);
            }
        }

        private static string GetLanguageText(ErrorLanguage language)
        {
            switch (language)
            {
                case ErrorLanguage.CSharp:
                    return "C#";
                case ErrorLanguage.Lua:
                    return "Lua";
                case ErrorLanguage.Mixed:
                    return "混合";
                default:
                    return "未知";
            }
        }

        private static string GetKindText(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.RuntimeException:
                    return "运行时异常";
                case ErrorKind.CaughtExceptionLog:
                    return "捕获异常";
                case ErrorKind.ManualErrorLog:
                    return "主动错误";
                case ErrorKind.Assert:
                    return "断言";
                default:
                    return "未分类";
            }
        }
    }
}
