using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor.PackageManager;

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
                if (normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    PackageInfo package = PackageInfo.FindForAssetPath(normalizedPath);
                    if (package != null)
                    {
                        string prefix = "Packages/" + package.name + "/";
                        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
                        string packageRoot = Path.GetFullPath(package.resolvedPath)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        string packagePath = Path.GetFullPath(Path.Combine(packageRoot, normalizedPath.Substring(prefix.Length)));
                        if (!packagePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)
                            || !File.Exists(packagePath)) return false;
                        location = new ResolvedSourceLocation
                        {
                            ProjectRelativePath = normalizedPath,
                            AbsolutePath = packagePath,
                            LineNumber = frame.LineNumber
                        };
                        frame.SourceFilePath = normalizedPath;
                        return true;
                    }
                }
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

            int targetLine = Math.Max(1, location.LineNumber);
            int safeContextLineCount = Math.Max(0, Math.Min(100, contextLineCount));
            long endLine = (long)targetLine + safeContextLineCount;
            int startLine = Math.Max(1, targetLine - safeContextLineCount);
            List<string> lines = new List<string>();
            try
            {
                using (StreamReader reader = new StreamReader(location.AbsolutePath, Encoding.UTF8, true))
                {
                    string line;
                    for (long number = 1; number <= endLine && (line = reader.ReadLine()) != null; number++)
                    {
                        if (number >= startLine) lines.Add(line);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            // 越界行号通常说明源码已变化，不把文件末行伪装成错误位置。
            if (lines.Count == 0 || (long)startLine + lines.Count - 1 < targetLine)
            {
                return null;
            }

            int numberWidth = ((long)startLine + lines.Count - 1).ToString().Length;
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(startLine + i == targetLine ? "> " : "  ");
                builder.Append((startLine + i).ToString().PadLeft(numberWidth));
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
