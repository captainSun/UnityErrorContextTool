using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class XLuaErrorParser : IErrorParser
    {
        private const string LuaTracebackHeader = "stack traceback:";

        private static readonly Regex LuaFrameRegex = new Regex(
            @"^\s*(?:\[string\s+[""']?)?(?<path>@?.+?\.lua)(?:[""']?\])?:(?<line>\d+):\s*(?<description>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex LuaLocationRegex = new Regex(
            @"^\s*(?:\[string\s+[""']?)?(?<path>@?.+?\.lua)(?:[""']?\])?:(?<line>\d+):\s*(?<description>[^\r\n]*)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex LuaExceptionPrefixRegex = new Regex(
            @"^(?:XLua\.)?LuaException:\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FunctionRegex = new Regex(
            "in\\s+(?:function|field|method|upvalue)\\s+['\\\"](?<function>[^'\\\"]+)['\\\"]",
            RegexOptions.Compiled);

        public int Priority
        {
            get { return 200; }
        }

        public bool CanParse(RawLogEvent rawEvent)
        {
            if (rawEvent == null)
            {
                return false;
            }

            string message = rawEvent.Message ?? string.Empty;
            string stackTrace = rawEvent.StackTrace ?? string.Empty;
            return FindLoggerPrefix(message) != null
                   || message.IndexOf(LuaTracebackHeader, StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("LuaException:", StringComparison.OrdinalIgnoreCase) >= 0
                   || LuaLocationRegex.IsMatch(message)
                   || stackTrace.IndexOf("XLua.LuaException", StringComparison.Ordinal) >= 0;
        }

        public ParsedError Parse(RawLogEvent rawEvent)
        {
            string normalized = ErrorParsingUtility.NormalizeMessage(rawEvent.Message);
            string loggerPrefix = FindLoggerPrefix(normalized);
            bool loggedByLuaLogger = loggerPrefix != null;
            if (loggedByLuaLogger)
            {
                normalized = RemoveFirst(normalized, loggerPrefix);
            }

            int tracebackIndex = normalized.IndexOf(LuaTracebackHeader, StringComparison.OrdinalIgnoreCase);
            string message = tracebackIndex >= 0 ? normalized.Substring(0, tracebackIndex).Trim() : normalized.Trim();
            string luaStackTrace = tracebackIndex >= 0 ? normalized.Substring(tracebackIndex).Trim() : string.Empty;

            bool hasLuaExceptionPrefix = LuaExceptionPrefixRegex.IsMatch(message);
            message = LuaExceptionPrefixRegex.Replace(message, string.Empty).Trim();

            ParsedError result = ErrorParsingUtility.CreateBaseError(rawEvent, ErrorLanguage.Lua);
            result.Kind = ClassifyKind(rawEvent, loggedByLuaLogger, hasLuaExceptionPrefix, message);
            result.ErrorType = GetErrorType(result.Kind);
            result.Message = string.IsNullOrEmpty(message) ? "Lua Error" : message;
            result.LuaStackTrace = luaStackTrace;
            result.CSharpStackTrace = rawEvent.StackTrace ?? string.Empty;

            AddLuaFrames(result, message, luaStackTrace);
            AddCSharpFrames(result, rawEvent);
            result.PrimaryFrame = SelectPrimaryFrame(result);

            if (string.IsNullOrEmpty(result.LuaStackTrace))
            {
                result.LuaStackTrace = BuildLuaStackTrace(result.StackFrames);
            }

            return result;
        }

        private static ErrorKind ClassifyKind(
            RawLogEvent rawEvent,
            bool loggedByLuaLogger,
            bool hasLuaExceptionPrefix,
            string message)
        {
            if (rawEvent.LogType == UnityEngine.LogType.Assert)
            {
                return ErrorKind.Assert;
            }

            if (rawEvent.LogType == UnityEngine.LogType.Exception || hasLuaExceptionPrefix)
            {
                return ErrorKind.RuntimeException;
            }

            if (!loggedByLuaLogger)
            {
                return ErrorKind.RuntimeException;
            }

            return LooksLikeLuaRuntimeError(message)
                ? ErrorKind.CaughtExceptionLog
                : ErrorKind.ManualErrorLog;
        }

        private static bool LooksLikeLuaRuntimeError(string message)
        {
            if (!LuaLocationRegex.IsMatch(message))
            {
                return false;
            }

            string value = message.ToLowerInvariant();
            return value.Contains("attempt to")
                   || value.Contains("bad argument")
                   || value.Contains("nil value")
                   || value.Contains("stack overflow")
                   || value.Contains("syntax error")
                   || value.Contains("unexpected symbol")
                   || value.Contains("module '");
        }

        private static void AddLuaFrames(ParsedError error, string message, string luaStackTrace)
        {
            HashSet<string> addedFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Match headerMatch = LuaLocationRegex.Match(message);
            if (headerMatch.Success)
            {
                AddLuaFrame(error, headerMatch, message, addedFrames);
            }

            string[] lines = luaStackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                Match frameMatch = LuaFrameRegex.Match(lines[i]);
                if (frameMatch.Success)
                {
                    AddLuaFrame(error, frameMatch, lines[i], addedFrames);
                }
            }
        }

        private static void AddLuaFrame(
            ParsedError error,
            Match match,
            string rawText,
            HashSet<string> addedFrames)
        {
            string path = NormalizeLuaPath(match.Groups["path"].Value);
            int lineNumber;
            int.TryParse(match.Groups["line"].Value, out lineNumber);
            string key = path + ":" + lineNumber;
            if (!addedFrames.Add(key))
            {
                return;
            }

            string description = match.Groups["description"].Value.Trim();
            Match functionMatch = FunctionRegex.Match(description);
            error.StackFrames.Add(new ErrorStackFrame
            {
                Language = ErrorLanguage.Lua,
                FunctionName = functionMatch.Success ? functionMatch.Groups["function"].Value : description,
                ModuleName = Path.GetFileNameWithoutExtension(path),
                RuntimeFilePath = path,
                LineNumber = lineNumber,
                IsProjectCode = !IsLoggerFrame(path),
                RawText = rawText.Trim()
            });
        }

        private static void AddCSharpFrames(ParsedError error, RawLogEvent rawEvent)
        {
            CSharpErrorParser csharpParser = new CSharpErrorParser();
            if (!csharpParser.CanParse(rawEvent))
            {
                return;
            }

            ParsedError csharpError = csharpParser.Parse(rawEvent);
            error.StackFrames.AddRange(csharpError.StackFrames);
        }

        private static ErrorStackFrame SelectPrimaryFrame(ParsedError error)
        {
            ErrorStackFrame fallback = null;
            for (int i = 0; i < error.StackFrames.Count; i++)
            {
                ErrorStackFrame frame = error.StackFrames[i];
                if (frame.Language != ErrorLanguage.Lua)
                {
                    continue;
                }

                if (fallback == null)
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

        private static string BuildLuaStackTrace(List<ErrorStackFrame> frames)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Language == ErrorLanguage.Lua)
                {
                    builder.AppendLine(frames[i].RawText);
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string NormalizeLuaPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.StartsWith("@", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }

        private static bool IsLoggerFrame(string path)
        {
            List<string> ignoredFrames = RuntimeErrorContextSettings.Current.IgnoredLuaFrames;
            for (int i = 0; i < ignoredFrames.Count; i++)
            {
                string ignoredFrame = NormalizeLuaPath(ignoredFrames[i]);
                if (path.EndsWith(ignoredFrame, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindLoggerPrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            List<string> prefixes = RuntimeErrorContextSettings.Current.LuaLoggerPrefixes;
            for (int i = 0; i < prefixes.Count; i++)
            {
                if (value.IndexOf(prefixes[i], StringComparison.Ordinal) >= 0)
                {
                    return prefixes[i];
                }
            }

            return null;
        }

        private static string RemoveFirst(string value, string marker)
        {
            int index = value.IndexOf(marker, StringComparison.Ordinal);
            return index < 0 ? value : value.Remove(index, marker.Length);
        }

        private static string GetErrorType(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.RuntimeException:
                    return "LuaException";
                case ErrorKind.CaughtExceptionLog:
                    return "Lua捕获异常";
                case ErrorKind.ManualErrorLog:
                    return "Lua主动错误";
                case ErrorKind.Assert:
                    return "Lua Assert";
                default:
                    return "Lua Error";
            }
        }
    }
}
