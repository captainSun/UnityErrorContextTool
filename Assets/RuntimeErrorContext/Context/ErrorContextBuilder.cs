using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class ErrorContextBuilder
    {
        private const int CurrentSchemaVersion = 1;
        private readonly List<ISourceResolver> m_SourceResolvers;

        public ErrorContextBuilder(IEnumerable<ISourceResolver> sourceResolvers = null)
        {
            m_SourceResolvers = sourceResolvers != null ? new List<ISourceResolver>(sourceResolvers) : null;
        }

        public ErrorContext Build(RuntimeErrorRecord record, int? sourceContextLineCount = null)
        {
            ParsedError parsedError = record != null ? record.Error : null;
            ErrorContext context = new ErrorContext
            {
                SchemaVersion = CurrentSchemaVersion,
                Error = CreateErrorInfo(parsedError),
                Aggregation = CreateAggregation(record),
                PrimaryLocation = CreateLocation(parsedError != null ? parsedError.PrimaryFrame : null),
                Environment = new ErrorContextEnvironment
                {
                    UnityVersion = Application.unityVersion,
                    SceneName = parsedError != null ? parsedError.SceneName ?? string.Empty : string.Empty,
                    Platform = Application.platform.ToString()
                }
            };

            int primaryStackIndex = CopyStackFrames(context, parsedError);
            if (parsedError == null || parsedError.PrimaryFrame == null)
            {
                return context;
            }

            // Resolver可能回写SourceFilePath，因此使用副本，避免修改Repository里的原始错误。
            List<ISourceResolver> sourceResolvers = m_SourceResolvers ?? RuntimeErrorContextRegistry.GetSourceResolvers();
            for (int i = 0; i < sourceResolvers.Count; i++)
            {
                ISourceResolver resolver = sourceResolvers[i];
                // 扩展可能修改栈帧后失败，每次尝试都使用干净的副本。
                ErrorStackFrame frame = CloneStackFrame(parsedError.PrimaryFrame);
                ResolvedSourceLocation location;
                try
                {
                    if (!resolver.CanResolve(frame) || !resolver.TryResolve(frame, out location)
                        || location == null)
                    {
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    ExtensionDiagnostics.Report(resolver, "Resolve", exception);
                    continue;
                }

                context.PrimaryLocation.SourceFilePath = location.ProjectRelativePath;
                context.PrimaryLocation.AbsolutePath = location.AbsolutePath;
                context.PrimaryLocation.LineNumber = location.LineNumber;
                context.PrimaryLocation.IsResolved = true;
                if (primaryStackIndex >= 0 && primaryStackIndex < context.StackFrames.Count)
                {
                    context.StackFrames[primaryStackIndex].SourceFilePath = location.ProjectRelativePath;
                    context.StackFrames[primaryStackIndex].LineNumber = location.LineNumber;
                }

                SourceContext sourceContext = SourceContextReader.Read(location,
                    sourceContextLineCount ?? RuntimeErrorContextSettings.Current.SourceContextLineCount);
                if (sourceContext != null)
                {
                    sourceContext.StackIndex = primaryStackIndex;
                    sourceContext.IsPrimary = true;
                    sourceContext.Language = frame.Language;
                    sourceContext.FunctionName = frame.FunctionName ?? string.Empty;
                    context.SourceContexts.Add(sourceContext);
                }

                break;
            }

            return context;
        }

        private static ErrorContextInfo CreateErrorInfo(ParsedError error)
        {
            if (error == null)
            {
                return new ErrorContextInfo
                {
                    Language = ErrorLanguage.Unknown,
                    Kind = ErrorKind.Unknown,
                    LogType = LogType.Log,
                    ErrorType = string.Empty,
                    Message = string.Empty,
                    RawMessage = string.Empty,
                    RawStackTrace = string.Empty,
                    LuaStackTrace = string.Empty,
                    CSharpStackTrace = string.Empty
                };
            }

            return new ErrorContextInfo
            {
                Language = error.Language,
                Kind = error.Kind,
                LogType = error.LogType,
                ErrorType = error.ErrorType ?? string.Empty,
                Message = error.Message ?? string.Empty,
                RawMessage = error.RawMessage ?? string.Empty,
                RawStackTrace = error.RawStackTrace ?? string.Empty,
                LuaStackTrace = error.LuaStackTrace ?? string.Empty,
                CSharpStackTrace = error.CSharpStackTrace ?? string.Empty
            };
        }

        private static ErrorContextAggregation CreateAggregation(RuntimeErrorRecord record)
        {
            return new ErrorContextAggregation
            {
                Fingerprint = record != null ? record.Fingerprint ?? string.Empty : string.Empty,
                OccurrenceCount = record != null ? record.OccurrenceCount : 0,
                FirstOccurredAt = record != null ? record.FirstOccurredAt : default(DateTime),
                LastOccurredAt = record != null ? record.LastOccurredAt : default(DateTime)
            };
        }

        private static ErrorContextLocation CreateLocation(ErrorStackFrame frame)
        {
            if (frame == null)
            {
                return new ErrorContextLocation
                {
                    Language = ErrorLanguage.Unknown,
                    FunctionName = string.Empty,
                    RuntimeFilePath = string.Empty,
                    SourceFilePath = string.Empty,
                    IsResolved = false
                };
            }

            return new ErrorContextLocation
            {
                Language = frame.Language,
                FunctionName = frame.FunctionName ?? string.Empty,
                RuntimeFilePath = frame.RuntimeFilePath ?? string.Empty,
                SourceFilePath = frame.SourceFilePath ?? string.Empty,
                LineNumber = frame.LineNumber,
                IsResolved = !string.IsNullOrEmpty(frame.SourceFilePath)
            };
        }

        private static int CopyStackFrames(ErrorContext context, ParsedError error)
        {
            if (error == null)
            {
                return -1;
            }

            int primaryIndex = -1;
            if (error.StackFrames == null)
            {
                return -1;
            }

            for (int i = 0; i < error.StackFrames.Count; i++)
            {
                ErrorStackFrame source = error.StackFrames[i];
                bool isPrimary = error.PrimaryFrame != null && ReferenceEquals(source, error.PrimaryFrame);
                if (isPrimary)
                {
                    primaryIndex = i;
                }

                context.StackFrames.Add(CreateContextFrame(source, i, isPrimary));
            }

            if (error.PrimaryFrame != null && primaryIndex < 0)
            {
                primaryIndex = context.StackFrames.Count;
                context.StackFrames.Add(CreateContextFrame(error.PrimaryFrame, primaryIndex, true));
            }

            return primaryIndex;
        }

        private static ErrorContextFrame CreateContextFrame(ErrorStackFrame frame, int index, bool isPrimary)
        {
            return new ErrorContextFrame
            {
                Index = index,
                Language = frame != null ? frame.Language : ErrorLanguage.Unknown,
                FunctionName = frame != null ? frame.FunctionName ?? string.Empty : string.Empty,
                ModuleName = frame != null ? frame.ModuleName ?? string.Empty : string.Empty,
                RuntimeFilePath = frame != null ? frame.RuntimeFilePath ?? string.Empty : string.Empty,
                SourceFilePath = frame != null ? frame.SourceFilePath ?? string.Empty : string.Empty,
                LineNumber = frame != null ? frame.LineNumber : 0,
                IsProjectCode = frame != null && frame.IsProjectCode,
                IsPrimary = isPrimary,
                RawText = frame != null ? frame.RawText ?? string.Empty : string.Empty
            };
        }

        private static ErrorStackFrame CloneStackFrame(ErrorStackFrame frame)
        {
            return new ErrorStackFrame
            {
                Language = frame.Language,
                FunctionName = frame.FunctionName,
                ModuleName = frame.ModuleName,
                RuntimeFilePath = frame.RuntimeFilePath,
                SourceFilePath = frame.SourceFilePath,
                LineNumber = frame.LineNumber,
                IsProjectCode = frame.IsProjectCode,
                RawText = frame.RawText
            };
        }
    }

    internal static class ErrorContextFormatter
    {
        public static string ToMarkdown(ErrorContext context)
        {
            if (context == null || context.Error == null)
            {
                return string.Empty;
            }

            ErrorContextInfo error = context.Error;
            ErrorContextAggregation aggregation = context.Aggregation;
            ErrorContextEnvironment environment = context.Environment;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Unity Runtime Error");
            builder.AppendLine();
            builder.Append("- Schema Version: ").AppendLine(context.SchemaVersion.ToString());
            builder.Append("- Type: ").AppendLine(error.ErrorType);
            builder.Append("- Language: ").AppendLine(error.Language.ToString());
            builder.Append("- Kind: ").AppendLine(error.Kind.ToString());
            builder.Append("- Log Type: ").AppendLine(error.LogType.ToString());
            builder.Append("- Occurrences: ").AppendLine(aggregation != null
                ? aggregation.OccurrenceCount.ToString()
                : "0");
            if (aggregation != null && aggregation.FirstOccurredAt != default(DateTime))
            {
                builder.Append("- First Occurred: ").AppendLine(aggregation.FirstOccurredAt.ToString("O"));
                builder.Append("- Last Occurred: ").AppendLine(aggregation.LastOccurredAt.ToString("O"));
            }

            builder.Append("- Unity: ").AppendLine(environment != null ? environment.UnityVersion : string.Empty);
            builder.Append("- Scene: ").AppendLine(environment == null || string.IsNullOrEmpty(environment.SceneName)
                ? "<None>"
                : environment.SceneName);
            builder.Append("- Platform: ").AppendLine(environment != null ? environment.Platform : string.Empty);

            if (context.PrimaryLocation != null && !string.IsNullOrEmpty(context.PrimaryLocation.RuntimeFilePath))
            {
                builder.Append("- Location: ")
                    .Append(!string.IsNullOrEmpty(context.PrimaryLocation.SourceFilePath)
                        ? context.PrimaryLocation.SourceFilePath
                        : context.PrimaryLocation.RuntimeFilePath)
                    .Append(':').AppendLine(context.PrimaryLocation.LineNumber.ToString());
            }

            builder.AppendLine();
            builder.AppendLine("## Message");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(error.Message);
            builder.AppendLine("```");

            for (int i = 0; i < context.SourceContexts.Count; i++)
            {
                SourceContext source = context.SourceContexts[i];
                builder.AppendLine();
                builder.AppendLine("## Relevant Source");
                builder.AppendLine();
                builder.Append("File: ").AppendLine(source.Location.ProjectRelativePath);
                builder.AppendLine();
                string codeLanguage = source.Location.ProjectRelativePath.EndsWith(".lua") ? "lua" : "csharp";
                builder.Append("```").AppendLine(codeLanguage);
                builder.Append(source.Content);
                builder.AppendLine("```");
            }

            AppendStackTrace(builder, "Lua Stack Trace", error.LuaStackTrace);
            AppendStackTrace(builder, "C# Stack Trace", error.CSharpStackTrace);
            return builder.ToString();
        }

        private static void AppendStackTrace(StringBuilder builder, string title, string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return;
            }

            builder.AppendLine();
            builder.Append("## ").AppendLine(title);
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(stackTrace);
            builder.AppendLine("```");
        }
    }
}
