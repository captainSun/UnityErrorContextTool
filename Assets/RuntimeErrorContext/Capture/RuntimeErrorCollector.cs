using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    [InitializeOnLoad]
    internal static class RuntimeErrorCollector
    {
        private const string CollectingSessionKey = "RuntimeErrorContext.IsCollecting.DefaultOn";
        private const int MaxEventsPerUpdate = 50;
        private const int MaxMessageLength = 16 * 1024;
        private const int MaxStackTraceLength = 64 * 1024;

        private static readonly PendingLogBuffer PendingEvents = new PendingLogBuffer(256);

        private static volatile bool s_IsCollecting;
        private static volatile bool s_AcceptLogs;
        private static volatile string s_ActiveSceneName;

        public static long DroppedEventCount { get { return PendingEvents.DroppedCount; } }

        public static event Action StateChanged;

        public static bool IsCollecting
        {
            get { return s_IsCollecting; }
        }

        static RuntimeErrorCollector()
        {
            s_AcceptLogs = EditorApplication.isPlayingOrWillChangePlaymode;
            s_ActiveSceneName = SceneManager.GetActiveScene().name;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            bool sessionState = SessionState.GetBool(
                CollectingSessionKey,
                RuntimeErrorContextSettings.Current.CollectByDefault);
            ApplyCollectingState(sessionState);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene current)
        {
            s_ActiveSceneName = current.name;
        }

        public static void Clear()
        {
            ClearPendingEvents();
            RuntimeErrorRepository.Clear();
            AgentContextSync.Clear();
            RaiseStateChanged();
        }

        public static void SetCollecting(bool collecting)
        {
            if (s_IsCollecting == collecting)
            {
                return;
            }

            ApplyCollectingState(collecting);
            SessionState.SetBool(CollectingSessionKey, collecting);
            RaiseStateChanged();
        }

        private static void ApplyCollectingState(bool collecting)
        {
            Application.logMessageReceivedThreaded -= OnLogReceived;
            s_IsCollecting = collecting;
            if (collecting)
            {
                Application.logMessageReceivedThreaded += OnLogReceived;
            }
            else
            {
                ClearPendingEvents();
            }
        }

        private static void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            if (!s_IsCollecting || !s_AcceptLogs
                || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert))
            {
                return;
            }

            PendingEvents.TryEnqueue(new RawLogEvent
            {
                LogType = type,
                Message = Truncate(condition, MaxMessageLength),
                StackTrace = Truncate(stackTrace, MaxStackTraceLength),
                Timestamp = DateTime.Now,
                SceneName = s_ActiveSceneName
            });
        }

        private static void Update()
        {
            if (!s_IsCollecting)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                // 进入 Play 的过渡期保留初始化日志。
                return;
            }

            ProcessPendingEvents(MaxEventsPerUpdate);
        }

        private static void ProcessPendingEvents(int limit)
        {
            int processedCount = 0;
            RawLogEvent rawEvent;
            while (processedCount < limit && PendingEvents.TryDequeue(out rawEvent))
            {
                ParsedError error = RuntimeErrorContextRegistry.Parse(rawEvent);
                if (error != null)
                {
                    RuntimeErrorRepository.AddOrUpdate(error, false);
                }

                processedCount++;
            }

            if (processedCount > 0)
            {
                RuntimeErrorRepository.NotifyChanged();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 在退出 Edit Mode 时清理，避免丢失 Play 初始化阶段产生的错误。
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (RuntimeErrorContextSettings.Current.ClearOnEnterPlay) Clear();
                s_ActiveSceneName = SceneManager.GetActiveScene().name;
                s_AcceptLogs = true;
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                s_ActiveSceneName = SceneManager.GetActiveScene().name;
                s_AcceptLogs = true;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                s_AcceptLogs = false;
                // 队列有界，退出时排空，保留最后一帧及退出阶段的错误。
                if (s_IsCollecting) ProcessPendingEvents(256);
            }
        }

        private static void ClearPendingEvents()
        {
            PendingEvents.Clear();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength);
        }

        private static void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
