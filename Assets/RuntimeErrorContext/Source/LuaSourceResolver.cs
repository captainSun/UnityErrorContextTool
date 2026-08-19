using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class LuaSourceResolver : ISourceResolver
    {
        public bool CanResolve(ErrorStackFrame frame)
        {
            return frame != null
                   && frame.Language == ErrorLanguage.Lua
                   && !string.IsNullOrEmpty(frame.RuntimeFilePath)
                   && frame.RuntimeFilePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryResolve(ErrorStackFrame frame, out ResolvedSourceLocation location)
        {
            location = null;
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                List<string> sourceRoots = RuntimeErrorContextSettings.Current.LuaSourceRoots;
                for (int i = 0; i < sourceRoots.Count; i++)
                {
                    string configuredRoot = sourceRoots[i];
                    string luaRoot = Path.IsPathRooted(configuredRoot)
                        ? Path.GetFullPath(configuredRoot)
                        : Path.GetFullPath(Path.Combine(projectRoot, configuredRoot));
                    string absolutePath = ResolveAbsolutePath(frame.RuntimeFilePath, configuredRoot, luaRoot);
                    string rootWithSeparator = luaRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                               + Path.DirectorySeparatorChar;

                    if (string.IsNullOrEmpty(absolutePath)
                        || !absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(absolutePath))
                    {
                        continue;
                    }

                    string displayPath = GetDisplayPath(projectRoot, absolutePath);
                    location = new ResolvedSourceLocation
                    {
                        ProjectRelativePath = displayPath,
                        AbsolutePath = absolutePath,
                        LineNumber = frame.LineNumber
                    };
                    frame.SourceFilePath = displayPath;
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // 日志里的非法路径不能影响错误收集。
                return false;
            }
        }

        private static string ResolveAbsolutePath(string runtimePath, string configuredRoot, string luaRoot)
        {
            string normalizedPath = (runtimePath ?? string.Empty).Trim().TrimStart('@').Replace('\\', '/');
            if (Path.IsPathRooted(normalizedPath))
            {
                return Path.GetFullPath(normalizedPath);
            }

            string normalizedRoot = configuredRoot.Trim().Replace('\\', '/').Trim('/');
            int rootIndex = normalizedPath.IndexOf(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(normalizedRoot) && rootIndex >= 0)
            {
                normalizedPath = normalizedPath.Substring(rootIndex + normalizedRoot.Length + 1);
            }

            return Path.GetFullPath(Path.Combine(luaRoot, normalizedPath.TrimStart('/')));
        }

        private static string GetDisplayPath(string projectRoot, string absolutePath)
        {
            string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                       + Path.DirectorySeparatorChar;
            return absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                ? absolutePath.Substring(rootWithSeparator.Length).Replace('\\', '/')
                : absolutePath.Replace('\\', '/');
        }
    }
}
