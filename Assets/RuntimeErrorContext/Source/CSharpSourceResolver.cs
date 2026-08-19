using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class CSharpSourceResolver : ISourceResolver
    {
        public bool CanResolve(ErrorStackFrame frame)
        {
            return frame != null
                   && frame.Language == ErrorLanguage.CSharp
                   && !string.IsNullOrEmpty(frame.RuntimeFilePath)
                   && frame.RuntimeFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryResolve(ErrorStackFrame frame, out ResolvedSourceLocation location)
        {
            location = null;
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string normalizedPath = ErrorParsingUtility.NormalizePath(frame.RuntimeFilePath);
                string absolutePath = Path.IsPathRooted(normalizedPath)
                    ? Path.GetFullPath(normalizedPath)
                    : Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));

                string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                           + Path.DirectorySeparatorChar;
                if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(absolutePath))
                {
                    return false;
                }

                string relativePath = absolutePath.Substring(rootWithSeparator.Length).Replace('\\', '/');
                location = new ResolvedSourceLocation
                {
                    ProjectRelativePath = relativePath,
                    AbsolutePath = absolutePath,
                    LineNumber = frame.LineNumber
                };
                frame.SourceFilePath = relativePath;
                return true;
            }
            catch (Exception)
            {
                // 非法日志路径不应影响错误列表和后续记录。
                return false;
            }
        }
    }

    internal static class SourceContextReader
    {
        public static SourceContext Read(ResolvedSourceLocation location)
        {
            return Read(location, RuntimeErrorContextSettings.Current.SourceContextLineCount);
        }

        public static SourceContext Read(ResolvedSourceLocation location, int contextLineCount)
        {
            if (location == null || string.IsNullOrEmpty(location.AbsolutePath))
            {
                return null;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(location.AbsolutePath, Encoding.UTF8);
            }
            catch (Exception)
            {
                return null;
            }

            if (lines.Length == 0)
            {
                return null;
            }

            int targetIndex = Math.Max(0, Math.Min(lines.Length - 1, location.LineNumber - 1));
            int safeContextLineCount = Math.Max(0, contextLineCount);
            int start = Math.Max(0, targetIndex - safeContextLineCount);
            int end = Math.Min(lines.Length - 1, targetIndex + safeContextLineCount);
            int numberWidth = (end + 1).ToString().Length;
            StringBuilder builder = new StringBuilder();

            for (int i = start; i <= end; i++)
            {
                builder.Append(i == targetIndex ? "> " : "  ");
                builder.Append((i + 1).ToString().PadLeft(numberWidth));
                builder.Append(" | ");
                builder.AppendLine(lines[i]);
            }

            return new SourceContext
            {
                Location = location,
                Content = builder.ToString()
            };
        }
    }
}
