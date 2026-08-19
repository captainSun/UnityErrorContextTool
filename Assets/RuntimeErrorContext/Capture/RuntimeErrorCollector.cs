using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    [InitializeOnLoad]
    internal static class RuntimeErrorCollector
    {
        private const string CollectingSessionKey = "RuntimeErrorContext.IsCollecting.DefaultOn";
        private const int MaxEventsPerUpdate = 50;
        private const int MaxMessageLength = 16 * 1024;
        private const int MaxStackTraceLength = 64 * 1024;

        private static readonly ConcurrentQueue<RawLogEvent> PendingEvents =
            new ConcurrentQueue<RawLogEvent>();

        private static bool s_IsCollecting;

        public static event Action StateChanged;

        public static bool IsCollecting
        {
            get { return s_IsCollecting; }
        }

        static RuntimeErrorCollector()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            bool sessionState = SessionState.GetBool(
                CollectingSessionKey,
                RuntimeErrorContextSettings.Current.CollectByDefault);
            ApplyCollectingState(sessionState);
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
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }

            PendingEvents.Enqueue(new RawLogEvent
            {
                LogType = type,
                Message = Truncate(condition, MaxMessageLength),
                StackTrace = Truncate(stackTrace, MaxStackTraceLength),
                Timestamp = DateTime.Now
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
                ClearPendingEvents();
                return;
            }

            int processedCount = 0;
            RawLogEvent rawEvent;
            while (processedCount < MaxEventsPerUpdate && PendingEvents.TryDequeue(out rawEvent))
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
            if (state == PlayModeStateChange.ExitingEditMode
                && RuntimeErrorContextSettings.Current.ClearOnEnterPlay)
            {
                ClearPendingEvents();
                RuntimeErrorRepository.Clear();
            }
        }

        private static void ClearPendingEvents()
        {
            RawLogEvent ignored;
            while (PendingEvents.TryDequeue(out ignored))
            {
            }
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
