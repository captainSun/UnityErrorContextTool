using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class CSharpErrorParser : IErrorParser
    {
        private static readonly Regex UnityLocationRegex = new Regex(
            @"\(at\s+(?<path>.+):(?<line>\d+)\)",
            RegexOptions.Compiled);

        private static readonly Regex DotNetLocationRegex = new Regex(
            @"^\s*at\s+(?<method>.*?)\s+in\s+(?<path>.+):line\s+(?<line>\d+)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public int Priority
        {
            get { return 100; }
        }

        public bool CanParse(RawLogEvent rawEvent)
        {
            if (rawEvent == null || string.IsNullOrEmpty(rawEvent.StackTrace))
            {
                return false;
            }

            return UnityLocationRegex.IsMatch(rawEvent.StackTrace)
                   || DotNetLocationRegex.IsMatch(rawEvent.StackTrace);
        }

        public ParsedError Parse(RawLogEvent rawEvent)
        {
            ParsedError result = ErrorParsingUtility.CreateBaseError(rawEvent, ErrorLanguage.CSharp);
            result.Kind = ClassifyKind(rawEvent);
            result.CSharpStackTrace = rawEvent.StackTrace ?? string.Empty;
            string[] lines = rawEvent.StackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string previousMethod = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                Match dotNetMatch = DotNetLocationRegex.Match(line);
                if (dotNetMatch.Success)
                {
                    AddFrame(result, line, dotNetMatch.Groups["method"].Value,
                        dotNetMatch.Groups["path"].Value, dotNetMatch.Groups["line"].Value);
                    previousMethod = string.Empty;
                    continue;
                }

                Match unityMatch = UnityLocationRegex.Match(line);
                if (unityMatch.Success)
                {
                    string method = line.Substring(0, unityMatch.Index).Trim();
                    if (string.IsNullOrEmpty(method))
                    {
                        method = previousMethod;
                    }

                    AddFrame(result, line, method, unityMatch.Groups["path"].Value,
                        unityMatch.Groups["line"].Value);
                    previousMethod = string.Empty;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("(at ", StringComparison.Ordinal))
                {
                    previousMethod = line.Trim();
                }
            }

            result.PrimaryFrame = SelectPrimaryFrame(result);
            return result;
        }

        private static ErrorKind ClassifyKind(RawLogEvent rawEvent)
        {
            if (rawEvent.LogType == UnityEngine.LogType.Assert)
            {
                return ErrorKind.Assert;
            }

            if (rawEvent.LogType == UnityEngine.LogType.Exception)
            {
                return ErrorKind.RuntimeException;
            }

            bool loggedByConfiguredLogger = ContainsConfiguredLoggerMethod(rawEvent.StackTrace);
            if (loggedByConfiguredLogger && ErrorParsingUtility.ContainsExceptionText(rawEvent.Message))
            {
                return ErrorKind.CaughtExceptionLog;
            }

            return ErrorKind.ManualErrorLog;
        }

        private static void AddFrame(ParsedError error, string rawText, string method, string path, string lineText)
        {
            int lineNumber;
            int.TryParse(lineText, out lineNumber);
            string normalizedPath = ErrorParsingUtility.NormalizePath(path);

            error.StackFrames.Add(new ErrorStackFrame
            {
                Language = ErrorLanguage.CSharp,
                FunctionName = method,
                RuntimeFilePath = normalizedPath,
                LineNumber = lineNumber,
                IsProjectCode = IsProjectCode(normalizedPath),
                RawText = rawText
            });
        }

        private static ErrorStackFrame SelectPrimaryFrame(ParsedError error)
        {
            ErrorStackFrame fallback = null;
            for (int i = 0; i < error.StackFrames.Count; i++)
            {
                ErrorStackFrame frame = error.StackFrames[i];
                if (fallback == null && IsUnityProjectPath(frame.RuntimeFilePath))
                {
                    fallback = frame;
                }

                if (frame.IsProjectCode)
                {
                    return frame;
                }
            }

            return fallback;
        }

        private static bool IsProjectCode(string path)
        {
            if (!IsUnityProjectPath(path))
            {
                return false;
            }

            List<string> ignoredPaths = RuntimeErrorContextSettings.Current.IgnoredCSharpPaths;
            for (int i = 0; i < ignoredPaths.Count; i++)
            {
                string ignoredPath = ErrorParsingUtility.NormalizePath(ignoredPaths[i]);
                if (path.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUnityProjectPath(string path)
        {
            return !string.IsNullOrEmpty(path)
                   && (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsConfiguredLoggerMethod(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            List<string> loggerMethods = RuntimeErrorContextSettings.Current.CSharpLoggerMethods;
            for (int i = 0; i < loggerMethods.Count; i++)
            {
                if (stackTrace.IndexOf(loggerMethods[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static class ErrorParsingUtility
    {
        private static readonly Regex RichTextRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex LoggerPrefixRegex = new Regex(
            @"^\s*\[\d+\]\s*Error:\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ParsedError CreateBaseError(RawLogEvent rawEvent, ErrorLanguage language)
        {
            string message = NormalizeMessage(rawEvent.Message);
            return new ParsedError
            {
                Language = language,
                LogType = rawEvent.LogType,
                ErrorType = GetErrorType(message, rawEvent.LogType),
                Message = message,
                RawMessage = rawEvent.Message ?? string.Empty,
                RawStackTrace = rawEvent.StackTrace ?? string.Empty,
                LuaStackTrace = string.Empty,
                CSharpStackTrace = rawEvent.StackTrace ?? string.Empty,
                Timestamp = rawEvent.Timestamp
            };
        }

        public static string NormalizeMessage(string message)
        {
            string normalized = RichTextRegex.Replace(message ?? string.Empty, string.Empty);
            normalized = LoggerPrefixRegex.Replace(normalized, string.Empty);
            return normalized.Replace("\r\n", "\n").Trim();
        }

        public static string NormalizePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            int assetsIndex = normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                return normalized.Substring(assetsIndex + 1);
            }

            int packagesIndex = normalized.IndexOf("/Packages/", StringComparison.OrdinalIgnoreCase);
            if (packagesIndex >= 0)
            {
                return normalized.Substring(packagesIndex + 1);
            }

            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                return normalized.Substring(2);
            }

            return normalized;
        }

        public static bool ContainsExceptionText(string message)
        {
            string value = message ?? string.Empty;
            return value.IndexOf("Exception:", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("ArgumentException", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("InvalidOperationException", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetErrorType(string message, UnityEngine.LogType logType)
        {
            int separator = message.IndexOf(':');
            if (separator > 0)
            {
                string candidate = message.Substring(0, separator).Trim();
                if (candidate.IndexOf('\n') < 0 && candidate.Length <= 128)
                {
                    return candidate;
                }
            }

            return logType.ToString();
        }
    }
}
