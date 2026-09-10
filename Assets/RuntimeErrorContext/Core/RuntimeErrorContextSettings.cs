using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    [Serializable]
    internal sealed class RuntimeErrorContextSettingsData
    {
        public bool CollectByDefault = true;
        public bool ClearOnEnterPlay = true;
        public int MaxRecordCount = 100;
        public int SourceContextLineCount = 15;

        public List<string> LuaSourceRoots = new List<string>();
        public List<string> LuaLoggerPrefixes = new List<string>();
        public List<string> IgnoredLuaFrames = new List<string>();
        public List<string> IgnoredCSharpPaths = new List<string>();
        public List<string> CSharpLoggerMethods = new List<string>();
    }

    internal static class RuntimeErrorContextSettings
    {
        private const string EditorPrefsKeyPrefix = "RuntimeErrorContext.Settings.";
        private static RuntimeErrorContextSettingsData s_Current;

        public static RuntimeErrorContextSettingsData Current
        {
            get
            {
                if (s_Current == null)
                {
                    s_Current = Load();
                }

                return s_Current;
            }
        }

        public static void Save()
        {
            Normalize(Current);
            EditorPrefs.SetString(GetEditorPrefsKey(), JsonUtility.ToJson(Current));
        }

        public static void ResetToDefaults()
        {
            s_Current = CreateDefaults();
            Save();
        }

        private static RuntimeErrorContextSettingsData Load()
        {
            RuntimeErrorContextSettingsData result = CreateDefaults();
            string json = EditorPrefs.GetString(GetEditorPrefsKey(), string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, result);
                }
                catch (Exception)
                {
                    result = CreateDefaults();
                }
            }

            Normalize(result);
            return result;
        }

        private static RuntimeErrorContextSettingsData CreateDefaults()
        {
            return new RuntimeErrorContextSettingsData
            {
                CollectByDefault = true,
                ClearOnEnterPlay = true,
                MaxRecordCount = 100,
                SourceContextLineCount = 15,
                LuaSourceRoots = new List<string>
                {
                    "SourceRes/Lua",
                    "Assets/Lua",
                    "Assets/LuaScripts",
                    "Assets/Scripts/Lua"
                },
                LuaLoggerPrefixes = new List<string>
                {
                    "LUAXLogger:",
                    "LUA:"
                },
                IgnoredLuaFrames = new List<string>
                {
                    "utils/XLogger.lua",
                    "XLogger.lua",
                    "Logger.lua"
                },
                IgnoredCSharpPaths = new List<string>
                {
                    "Assets/3rd/",
                    "Assets/Plugins/"
                },
                CSharpLoggerMethods = new List<string>
                {
                    "XLogger:LogError"
                }
            };
        }

        private static void Normalize(RuntimeErrorContextSettingsData settings)
        {
            settings.MaxRecordCount = Mathf.Max(1, settings.MaxRecordCount);
            settings.SourceContextLineCount = Mathf.Clamp(settings.SourceContextLineCount, 0, 100);
            settings.LuaSourceRoots = NormalizeList(settings.LuaSourceRoots);
            settings.LuaLoggerPrefixes = NormalizeList(settings.LuaLoggerPrefixes);
            settings.IgnoredLuaFrames = NormalizeList(settings.IgnoredLuaFrames);
            settings.IgnoredCSharpPaths = NormalizeList(settings.IgnoredCSharpPaths);
            settings.CSharpLoggerMethods = NormalizeList(settings.CSharpLoggerMethods);
        }

        private static List<string> NormalizeList(List<string> values)
        {
            List<string> result = new List<string>();
            if (values == null)
            {
                return result;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i] != null ? values[i].Trim() : string.Empty;
                if (!string.IsNullOrEmpty(value) && !result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static string GetEditorPrefsKey()
        {
            string projectKey = Application.dataPath
                .Replace('\\', '_')
                .Replace('/', '_')
                .Replace(':', '_');
            return EditorPrefsKeyPrefix + projectKey;
        }
    }

    internal static class RuntimeErrorContextSettingsProvider
    {
        [MenuItem("Tools/Runtime Error Context Settings")]
        private static void OpenSettings()
        {
            SettingsService.OpenUserPreferences("Preferences/Runtime Error Context");
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            SettingsProvider provider = new SettingsProvider("Preferences/Runtime Error Context", SettingsScope.User)
            {
                label = "Runtime Error Context",
                guiHandler = DrawSettings,
                keywords = new HashSet<string>(new[] { "Runtime", "Error", "Lua", "StackTrace" })
            };
            return provider;
        }

        private static void DrawSettings(string searchContext)
        {
            RuntimeErrorContextSettingsData settings = RuntimeErrorContextSettings.Current;
            EditorGUILayout.LabelField("采集", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.CollectByDefault = EditorGUILayout.Toggle("新会话默认开启", settings.CollectByDefault);
            settings.ClearOnEnterPlay = EditorGUILayout.Toggle("进入 Play 时清空", settings.ClearOnEnterPlay);
            settings.MaxRecordCount = EditorGUILayout.IntField("最大错误数量", settings.MaxRecordCount);
            settings.SourceContextLineCount = EditorGUILayout.IntField("复制上下文行数", settings.SourceContextLineCount);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lua 源码根目录（每行一个，可使用绝对路径）", EditorStyles.boldLabel);
            settings.LuaSourceRoots = DrawStringList(settings.LuaSourceRoots, 72f);

            EditorGUILayout.LabelField("Lua Logger 前缀（每行一个）", EditorStyles.boldLabel);
            settings.LuaLoggerPrefixes = DrawStringList(settings.LuaLoggerPrefixes, 54f);

            EditorGUILayout.LabelField("忽略的 Lua 栈帧（每行一个）", EditorStyles.boldLabel);
            settings.IgnoredLuaFrames = DrawStringList(settings.IgnoredLuaFrames, 72f);

            EditorGUILayout.LabelField("忽略的 C# 路径（每行一个）", EditorStyles.boldLabel);
            settings.IgnoredCSharpPaths = DrawStringList(settings.IgnoredCSharpPaths, 72f);

            EditorGUILayout.LabelField("C# Logger 方法标记（每行一个）", EditorStyles.boldLabel);
            settings.CSharpLoggerMethods = DrawStringList(settings.CSharpLoggerMethods, 54f);

            if (EditorGUI.EndChangeCheck())
            {
                RuntimeErrorContextSettings.Save();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("恢复默认设置", GUILayout.Width(120f)))
            {
                RuntimeErrorContextSettings.ResetToDefaults();
            }
        }

        private static List<string> DrawStringList(List<string> values, float height)
        {
            string text = values != null ? string.Join("\n", values.ToArray()) : string.Empty;
            string newText = EditorGUILayout.TextArea(text, GUILayout.MinHeight(height));
            return new List<string>(newText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
