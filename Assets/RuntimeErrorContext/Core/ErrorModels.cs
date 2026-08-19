using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    public enum ErrorLanguage
    {
        Unknown,
        CSharp,
        Lua,
        Mixed
    }

    public enum ErrorKind
    {
        Unknown,
        RuntimeException,
        CaughtExceptionLog,
        ManualErrorLog,
        Assert
    }

    public sealed class RawLogEvent
    {
        public LogType LogType;
        public string Message;
        public string StackTrace;
        public DateTime Timestamp;
    }

    public sealed class ErrorStackFrame
    {
        public ErrorLanguage Language;
        public string FunctionName;
        public string ModuleName;
        public string RuntimeFilePath;
        public string SourceFilePath;
        public int LineNumber;
        public bool IsProjectCode;
        public string RawText;
    }

    public sealed class ParsedError
    {
        public ErrorLanguage Language;
        public ErrorKind Kind;
        public LogType LogType;
        public string ErrorType;
        public string Message;
        public List<ErrorStackFrame> StackFrames = new List<ErrorStackFrame>();
        public ErrorStackFrame PrimaryFrame;
        public string RawMessage;
        public string RawStackTrace;
        public string LuaStackTrace;
        public string CSharpStackTrace;
        public DateTime Timestamp;
    }

    internal sealed class RuntimeErrorRecord
    {
        public string Fingerprint;
        public ParsedError Error;
        public DateTime FirstOccurredAt;
        public DateTime LastOccurredAt;
        public int OccurrenceCount;
    }

    public sealed class ResolvedSourceLocation
    {
        public string ProjectRelativePath;
        public string AbsolutePath;
        public int LineNumber;
    }

    internal sealed class SourceContext
    {
        public ResolvedSourceLocation Location;
        public string Content;
        public int StackIndex;
        public bool IsPrimary;
        public ErrorLanguage Language;
        public string FunctionName;
    }

    internal sealed class ErrorContextInfo
    {
        public ErrorLanguage Language;
        public ErrorKind Kind;
        public LogType LogType;
        public string ErrorType;
        public string Message;
        public string RawMessage;
        public string RawStackTrace;
        public string LuaStackTrace;
        public string CSharpStackTrace;
    }

    internal sealed class ErrorContextAggregation
    {
        public string Fingerprint;
        public int OccurrenceCount;
        public DateTime FirstOccurredAt;
        public DateTime LastOccurredAt;
    }

    internal sealed class ErrorContextLocation
    {
        public ErrorLanguage Language;
        public string FunctionName;
        public string RuntimeFilePath;
        public string SourceFilePath;
        public int LineNumber;
        public bool IsResolved;
    }

    internal sealed class ErrorContextFrame
    {
        public int Index;
        public ErrorLanguage Language;
        public string FunctionName;
        public string ModuleName;
        public string RuntimeFilePath;
        public string SourceFilePath;
        public int LineNumber;
        public bool IsProjectCode;
        public bool IsPrimary;
        public string RawText;
    }

    internal sealed class ErrorContextEnvironment
    {
        public string UnityVersion;
        public string SceneName;
        public string Platform;
    }

    internal sealed class ErrorContext
    {
        public int SchemaVersion;
        public ErrorContextInfo Error;
        public ErrorContextAggregation Aggregation;
        public ErrorContextLocation PrimaryLocation;
        public List<ErrorContextFrame> StackFrames = new List<ErrorContextFrame>();
        public List<SourceContext> SourceContexts = new List<SourceContext>();
        public ErrorContextEnvironment Environment;
    }
}
