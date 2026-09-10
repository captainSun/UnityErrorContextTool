using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    public class RuntimeErrorRegressionTests
    {
        private string m_SourceFile;
        private int m_MaxRecords;
        private List<string> m_LuaRoots;

        [SetUp]
        public void SetUp()
        {
            RuntimeErrorRepository.Clear();
            m_MaxRecords = RuntimeErrorContextSettings.Current.MaxRecordCount;
            m_LuaRoots = RuntimeErrorContextSettings.Current.LuaSourceRoots;
            m_SourceFile = Path.Combine(Application.temporaryCachePath, "error-context-" + Guid.NewGuid().ToString("N") + ".lua");
            File.WriteAllLines(m_SourceFile, Enumerable.Range(1, 10).Select(i => "line " + i));
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeErrorRepository.Clear();
            RuntimeErrorContextSettings.Current.MaxRecordCount = m_MaxRecords;
            RuntimeErrorContextSettings.Current.LuaSourceRoots = m_LuaRoots;
            File.Delete(m_SourceFile);
        }

        private static RawLogEvent Log(string message = "InvalidOperationException: sample", string caller = "First")
        {
            return new RawLogEvent
            {
                Message = message,
                StackTrace = "Game.Fail() (at Assets/Game.cs:5)\nGame." + caller + "() (at Assets/Caller.cs:9)",
                LogType = LogType.Exception,
                Timestamp = new DateTime(2026, 9, 10, 12, 0, 0),
                SceneName = "ErrorScene"
            };
        }

        [Test]
        public void DuplicateRecordUsesLatestCompleteSampleAndKeepsFirstTimestamp()
        {
            var first = new CSharpErrorParser().Parse(Log());
            var record = RuntimeErrorRepository.AddOrUpdate(first, false);
            var secondLog = Log(caller: "Second");
            secondLog.Timestamp = secondLog.Timestamp.AddSeconds(2);
            var second = new CSharpErrorParser().Parse(secondLog);
            Assert.AreSame(record, RuntimeErrorRepository.AddOrUpdate(second, false));
            Assert.AreEqual(2, record.OccurrenceCount);
            Assert.AreEqual(first.Timestamp, record.FirstOccurredAt);
            Assert.AreEqual(second.Timestamp, record.LastOccurredAt);
            Assert.AreSame(second, record.Error);
            StringAssert.Contains("Second", record.Error.CSharpStackTrace);
            StringAssert.Contains("Second", record.Error.StackFrames[1].FunctionName);
        }

        [Test]
        public void CapacityEvictsLeastRecentlyOccurredRecord()
        {
            RuntimeErrorContextSettings.Current.MaxRecordCount = 2;
            var first = RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log("one")), false);
            var secondLog = Log("two"); secondLog.Timestamp = secondLog.Timestamp.AddSeconds(1);
            var second = RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(secondLog), false);
            var repeat = Log("one"); repeat.Timestamp = repeat.Timestamp.AddSeconds(2);
            RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(repeat), false);
            var third = Log("three"); third.Timestamp = third.Timestamp.AddSeconds(3);
            RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(third), false);
            CollectionAssert.Contains(RuntimeErrorRepository.GetRecords(), first);
            CollectionAssert.DoesNotContain(RuntimeErrorRepository.GetRecords(), second);
            Assert.AreEqual(2, RuntimeErrorRepository.Count);
        }

        [Test]
        public void BufferBoundsConcurrentProducersAndCountsDrops()
        {
            var buffer = new PendingLogBuffer(32);
            Parallel.For(0, 1000, i => buffer.TryEnqueue(Log()));
            Assert.AreEqual(968, buffer.DroppedCount);
            int count = 0;
            while (buffer.TryDequeue(out var value)) { Assert.NotNull(value); count++; }
            Assert.AreEqual(32, count);
            buffer.Clear();
            Assert.AreEqual(0, buffer.DroppedCount);
            Assert.IsTrue(buffer.TryEnqueue(Log()));
            buffer.Clear();
            Assert.IsFalse(buffer.TryDequeue(out _));
        }

        [Test]
        public void BufferPreservesAcceptedOrder()
        {
            var buffer = new PendingLogBuffer(2);
            buffer.TryEnqueue(Log("one")); buffer.TryEnqueue(Log("two"));
            Assert.IsFalse(buffer.TryEnqueue(Log("three")));
            buffer.TryDequeue(out var first); buffer.TryDequeue(out var second);
            Assert.AreEqual("one", first.Message); Assert.AreEqual("two", second.Message);
        }

        [TestCase("Game.Fail() (at Assets/Game.cs:5)")]
        [TestCase("at Game.Fail() in E:\\Project\\Assets\\Game.cs:line 5")]
        public void CSharpSupportsUnityAndDotNetLocations(string stack)
        {
            var raw = Log(); raw.StackTrace = stack;
            var parsed = new CSharpErrorParser().Parse(raw);
            Assert.AreEqual("Assets/Game.cs", parsed.PrimaryFrame.RuntimeFilePath);
            Assert.AreEqual(5, parsed.PrimaryFrame.LineNumber);
            Assert.AreEqual(ErrorKind.RuntimeException, parsed.Kind);
        }

        [TestCase(LogType.Assert, ErrorKind.Assert)]
        [TestCase(LogType.Error, ErrorKind.ManualErrorLog)]
        public void CSharpClassifiesLogKind(LogType logType, ErrorKind kind)
        {
            var raw = Log("sample"); raw.LogType = logType;
            Assert.AreEqual(kind, new CSharpErrorParser().Parse(raw).Kind);
        }

        [Test]
        public void LuaPreservesRecursionAndUsesTracebackFunctionForHeader()
        {
            var raw = Log("LuaException: model/Test.lua:5: attempt to index a nil value\nstack traceback:\n model/Test.lua:5: in function 'recur'\n model/Test.lua:5: in function 'recur'\n main.lua:8: in function 'start'");
            var parsed = new XLuaErrorParser().Parse(raw);
            var lua = parsed.StackFrames.Where(frame => frame.Language == ErrorLanguage.Lua).ToList();
            Assert.AreEqual(3, lua.Count);
            Assert.AreEqual("recur", parsed.PrimaryFrame.FunctionName);
            Assert.AreEqual(ErrorLanguage.Mixed, parsed.Language);
        }

        [Test]
        public void LuaAddsDistinctHeaderBeforeTraceback()
        {
            var raw = Log("foo.lua:3: attempt to call nil\nstack traceback:\n main.lua:8: in function 'start'");
            raw.StackTrace = "";
            var parsed = new XLuaErrorParser().Parse(raw);
            Assert.AreEqual(2, parsed.StackFrames.Count);
            Assert.AreEqual("foo.lua", parsed.PrimaryFrame.RuntimeFilePath);
            Assert.AreEqual(ErrorLanguage.Lua, parsed.Language);
        }

        [TestCase("[string \"@model/Test.lua\"]:5: attempt to index nil")]
        [TestCase("@model/Test.lua:5: attempt to index nil")]
        public void LuaAcceptsStringAndAtPaths(string message)
        {
            var parsed = new XLuaErrorParser().Parse(Log(message));
            Assert.AreEqual("model/Test.lua", parsed.PrimaryFrame.RuntimeFilePath);
            Assert.AreEqual(5, parsed.PrimaryFrame.LineNumber);
        }

        [Test]
        public void ParserContinuesAfterThrowAndNullResult()
        {
            var registry = new ErrorParserRegistry(new IErrorParser[] { new ThrowingParser(), new NullParser(), new CSharpErrorParser() });
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ThrowingParser.Parse failed"));
            Assert.AreEqual(ErrorLanguage.CSharp, registry.Parse(Log()).Language);
        }

        [Test]
        public void UnknownLogFallsBackWithoutLosingRawData()
        {
            var raw = Log("opaque failure"); raw.StackTrace = "no symbols";
            var parsed = RuntimeErrorContextRegistry.Parse(raw);
            Assert.AreEqual(ErrorLanguage.Unknown, parsed.Language);
            Assert.AreEqual(raw.StackTrace, parsed.RawStackTrace);
        }

        [Test]
        public void ContextIsIndependentAndUsesCapturedScene()
        {
            var error = new CSharpErrorParser().Parse(Log());
            var record = RuntimeErrorRepository.AddOrUpdate(error, false);
            var context = new ErrorContextBuilder().Build(record);
            error.Message = "changed"; error.StackFrames[0].FunctionName = "changed"; record.OccurrenceCount++;
            Assert.AreEqual("InvalidOperationException: sample", context.Error.Message);
            Assert.AreEqual("Game.Fail()", context.StackFrames[0].FunctionName);
            Assert.AreEqual(1, context.Aggregation.OccurrenceCount);
            Assert.AreEqual("ErrorScene", context.Environment.SceneName);
        }

        [Test]
        public void ResolverFailureDoesNotContaminateFallbackOrRepository()
        {
            var record = RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log()), false);
            var builder = new ErrorContextBuilder(new ISourceResolver[] { new ThrowingResolver(), new FileResolver(m_SourceFile) });
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ThrowingResolver.Resolve failed"));
            var context = builder.Build(record);
            Assert.IsTrue(context.PrimaryLocation.IsResolved);
            Assert.AreEqual(1, context.SourceContexts.Count);
            Assert.AreEqual("Assets/Game.cs", record.Error.PrimaryFrame.RuntimeFilePath);
            Assert.IsNull(record.Error.PrimaryFrame.SourceFilePath);
            StringAssert.Contains(">  5 | line 5", context.SourceContexts[0].Content);
        }

        [Test]
        public void SourceReaderReturnsRequestedWindow()
        {
            var source = SourceContextReader.Read(new ResolvedSourceLocation { AbsolutePath = m_SourceFile, LineNumber = 5 }, 1);
            StringAssert.Contains("4 | line 4", source.Content);
            StringAssert.Contains("> 5 | line 5", source.Content);
            StringAssert.Contains("6 | line 6", source.Content);
            StringAssert.DoesNotContain("line 3", source.Content);
            StringAssert.DoesNotContain("line 7", source.Content);
        }

        [TestCase(11)]
        [TestCase(int.MaxValue)]
        public void SourceReaderRejectsOutOfRangeLine(int line)
        {
            Assert.IsNull(SourceContextReader.Read(new ResolvedSourceLocation { AbsolutePath = m_SourceFile, LineNumber = line }));
        }

        [Test]
        public void LuaResolverFindsExternalRootAndRejectsTraversal()
        {
            RuntimeErrorContextSettings.Current.LuaSourceRoots = new List<string> { Path.GetDirectoryName(m_SourceFile) };
            var resolver = new LuaSourceResolver();
            var frame = new ErrorStackFrame { Language = ErrorLanguage.Lua, RuntimeFilePath = Path.GetFileName(m_SourceFile), LineNumber = 5 };
            Assert.IsTrue(resolver.TryResolve(frame, out var location));
            Assert.AreEqual(Path.GetFullPath(m_SourceFile), location.AbsolutePath);
            frame.RuntimeFilePath = "../outside.lua";
            Assert.IsFalse(resolver.TryResolve(frame, out _));
        }

        [Test]
        public void CSharpResolverFindsActualPackageFile()
        {
            const string prefix = "Packages/com.unity.test-framework/";
            var package = PackageInfo.FindForAssetPath(prefix.TrimEnd('/'));
            Assert.NotNull(package);
            var file = Directory.GetFiles(package.resolvedPath, "*.cs", SearchOption.AllDirectories).First();
            var relative = prefix + file.Substring(package.resolvedPath.Length).TrimStart('/', '\\').Replace('\\', '/');
            var frame = new ErrorStackFrame { Language = ErrorLanguage.CSharp, RuntimeFilePath = relative, LineNumber = 1 };
            Assert.IsTrue(new CSharpSourceResolver().TryResolve(frame, out var location));
            Assert.AreEqual(Path.GetFullPath(file), location.AbsolutePath);
            Assert.AreEqual(relative, location.ProjectRelativePath);
        }

        [Test]
        public void CSharpResolverRejectsProjectTraversal()
        {
            var frame = new ErrorStackFrame { Language = ErrorLanguage.CSharp, RuntimeFilePath = "../outside.cs" };
            Assert.IsFalse(new CSharpSourceResolver().TryResolve(frame, out _));
        }

        [Test]
        public void SelectedWindowRefreshesAndClearsWithRepository()
        {
            var window = ScriptableObject.CreateInstance<RuntimeErrorWindow>();
            try
            {
                var record = RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log()), false);
                typeof(RuntimeErrorWindow).GetMethod("SelectRecord", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { record });
                RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log(caller: "Second")), true);
                var field = typeof(RuntimeErrorWindow).GetField("m_SelectedContext", BindingFlags.NonPublic | BindingFlags.Instance);
                var context = (ErrorContext)field.GetValue(window);
                Assert.AreEqual(2, context.Aggregation.OccurrenceCount);
                StringAssert.Contains("Second", context.Error.CSharpStackTrace);
                RuntimeErrorCollector.Clear();
                Assert.IsNull(field.GetValue(window));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void SwitchingErrorsUpdatesSourceFileLineAndClearsMissingSource()
        {
            string otherFile = m_SourceFile + ".other.lua";
            File.WriteAllLines(otherFile, Enumerable.Range(1, 10).Select(i => "other source " + i));
            RuntimeErrorContextSettings.Current.LuaSourceRoots = new List<string> { Path.GetDirectoryName(m_SourceFile) };
            var window = ScriptableObject.CreateInstance<RuntimeErrorWindow>();
            try
            {
                var select = typeof(RuntimeErrorWindow).GetMethod("SelectRecord", BindingFlags.NonPublic | BindingFlags.Instance);
                var field = typeof(RuntimeErrorWindow).GetField("m_SelectedContext", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var target in new[]
                {
                    new { File = m_SourceFile, Line = 2, Text = "line 2" },
                    new { File = otherFile, Line = 4, Text = "other source 4" },
                    new { File = m_SourceFile, Line = 8, Text = "line 8" }
                })
                {
                    var raw = Log(target.File.Replace('\\', '/') + ":" + target.Line + ": attempt to index nil");
                    raw.StackTrace = string.Empty;
                    var record = RuntimeErrorRepository.AddOrUpdate(new XLuaErrorParser().Parse(raw), false);
                    select.Invoke(window, new object[] { record });
                    var context = (ErrorContext)field.GetValue(window);
                    Assert.AreEqual(Path.GetFullPath(target.File), context.SourceContexts[0].Location.AbsolutePath);
                    Assert.AreEqual(target.Line, context.PrimaryLocation.LineNumber);
                    StringAssert.Contains("> " + target.Line.ToString().PadLeft(Math.Min(10, target.Line + 5).ToString().Length) + " | " + target.Text,
                        context.SourceContexts[0].Content);
                }
                var unknown = Log("no source location"); unknown.StackTrace = string.Empty;
                select.Invoke(window, new object[] { RuntimeErrorRepository.AddOrUpdate(new FallbackErrorParser().Parse(unknown), false) });
                var empty = (ErrorContext)field.GetValue(window);
                Assert.IsEmpty(empty.SourceContexts);
                Assert.IsFalse(empty.PrimaryLocation.IsResolved);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                File.Delete(otherFile);
            }
        }

        [Test]
        public void SwitchingErrorsEndsPreviousTextEditingSession()
        {
            var window = ScriptableObject.CreateInstance<RuntimeErrorWindow>();
            bool wasEditing = EditorGUIUtility.editingTextField;
            try
            {
                var select = typeof(RuntimeErrorWindow).GetMethod("SelectRecord", BindingFlags.NonPublic | BindingFlags.Instance);
                select.Invoke(window, new object[] { RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log()), false) });
                EditorGUIUtility.editingTextField = true;
                select.Invoke(window, new object[] { RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log("second error")), false) });
                Assert.IsFalse(EditorGUIUtility.editingTextField, "切换记录后不能保留旧TextArea的编辑状态");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                EditorGUIUtility.editingTextField = wasEditing;
            }
        }

        [TestCase(1, 1, 6)]
        [TestCase(13, 8, 18)]
        [TestCase(25, 20, 25)]
        public void WindowSourcePreviewKeepsFiveLinesEachSideRegardlessOfExportSetting(int target, int first, int last)
        {
            File.WriteAllLines(m_SourceFile, Enumerable.Range(1, 25).Select(i => "line " + i));
            RuntimeErrorContextSettings.Current.LuaSourceRoots = new List<string> { Path.GetDirectoryName(m_SourceFile) };
            int previousRange = RuntimeErrorContextSettings.Current.SourceContextLineCount;
            RuntimeErrorContextSettings.Current.SourceContextLineCount = 100;
            var window = ScriptableObject.CreateInstance<RuntimeErrorWindow>();
            try
            {
                var raw = Log(m_SourceFile.Replace('\\', '/') + ":" + target + ": attempt to index nil");
                raw.StackTrace = string.Empty;
                var record = RuntimeErrorRepository.AddOrUpdate(new XLuaErrorParser().Parse(raw), false);
                var type = typeof(RuntimeErrorWindow);
                type.GetMethod("SelectRecord", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { record });
                var contextField = type.GetField("m_SelectedContext", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int refresh = 0; refresh < 2; refresh++)
                {
                    var context = (ErrorContext)contextField.GetValue(window);
                    var lines = context.SourceContexts[0].Content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    Assert.AreEqual(last - first + 1, lines.Length);
                    StringAssert.EndsWith("| line " + first, lines[0].TrimEnd('\r'));
                    StringAssert.EndsWith("| line " + last, lines[lines.Length - 1].TrimEnd('\r'));
                    StringAssert.StartsWith("> ", lines[target - first]);
                    RuntimeErrorRepository.AddOrUpdate(new XLuaErrorParser().Parse(raw), true);
                }
                // 窗口缩短预览不会缩短用户复制给分析工具的上下文。
                var exported = new ErrorContextBuilder().Build(record);
                Assert.AreEqual(25, exported.SourceContexts[0].Content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
            }
            finally
            {
                RuntimeErrorContextSettings.Current.SourceContextLineCount = previousRange;
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void MarkdownContainsLatestSampleAndSource()
        {
            var record = RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log()), false);
            RuntimeErrorRepository.AddOrUpdate(new CSharpErrorParser().Parse(Log(caller: "Second")), false);
            var context = new ErrorContextBuilder(new[] { new FileResolver(m_SourceFile) }).Build(record);
            var markdown = ErrorContextFormatter.ToMarkdown(context);
            StringAssert.Contains("Occurrences: 2", markdown);
            StringAssert.Contains("Scene: ErrorScene", markdown);
            StringAssert.Contains("Second", markdown);
            StringAssert.Contains("line 5", markdown);
        }

        private sealed class ThrowingParser : IErrorParser
        {
            public int Priority => 500;
            public bool CanParse(RawLogEvent raw) => throw new InvalidOperationException("test parser failure");
            public ParsedError Parse(RawLogEvent raw) => null;
        }
        private sealed class NullParser : IErrorParser
        {
            public int Priority => 400;
            public bool CanParse(RawLogEvent raw) => true;
            public ParsedError Parse(RawLogEvent raw) => null;
        }
        private sealed class ThrowingResolver : ISourceResolver
        {
            public bool CanResolve(ErrorStackFrame frame) => true;
            public bool TryResolve(ErrorStackFrame frame, out ResolvedSourceLocation location)
            {
                location = null; frame.RuntimeFilePath = "corrupted";
                throw new InvalidOperationException("test resolver failure");
            }
        }
        private sealed class FileResolver : ISourceResolver
        {
            private readonly string m_Path;
            public FileResolver(string path) { m_Path = path; }
            public bool CanResolve(ErrorStackFrame frame) => frame.RuntimeFilePath == "Assets/Game.cs";
            public bool TryResolve(ErrorStackFrame frame, out ResolvedSourceLocation location)
            {
                frame.SourceFilePath = m_Path;
                location = new ResolvedSourceLocation { AbsolutePath = m_Path, ProjectRelativePath = m_Path, LineNumber = 5 };
                return true;
            }
        }
    }
}
