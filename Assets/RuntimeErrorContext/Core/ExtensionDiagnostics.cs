using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal static class ExtensionDiagnostics
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static void Report(object extension, string operation, Exception exception)
        {
            string key = extension.GetType().FullName + "." + operation;
            // 每种扩展故障只警告一次，且不产生会被工具再次采集的 Error 日志。
            if (Reported.Add(key))
            {
                Debug.LogWarning("Runtime Error Context: " + key + " failed; trying fallback. "
                                 + exception.Message);
            }
        }
    }
}
