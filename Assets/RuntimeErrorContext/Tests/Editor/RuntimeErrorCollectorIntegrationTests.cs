using System;
using System.Collections;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    public class RuntimeErrorCollectorIntegrationTests
    {
        private const string PreviousCollectingKey = "RuntimeErrorContext.Tests.PreviousCollecting";

        [UnityTest]
        public IEnumerator CapturesPlayModeMainAndWorkerLogsAndClearsPendingEvents()
        {
            SessionState.SetBool(PreviousCollectingKey, RuntimeErrorCollector.IsCollecting);
            yield return new EnterPlayMode();
            RuntimeErrorCollector.SetCollecting(true);
            RuntimeErrorCollector.Clear();
            var scene = SceneManager.CreateScene("ErrorContextIntegrationScene");
            SceneManager.SetActiveScene(scene);
            for (int i = 0; i < 2; i++)
            {
                LogAssert.Expect(LogType.Error, "context integration main");
                Debug.LogError("context integration main");
            }
            LogAssert.Expect(LogType.Error, "context integration worker");
            var worker = new Thread(() => Debug.LogError("context integration worker"));
            worker.Start();
            Assert.IsTrue(worker.Join(5000));
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (RuntimeErrorRepository.GetRecords().Sum(record => record.OccurrenceCount) < 3
                   && EditorApplication.timeSinceStartup < deadline) yield return null;
            var records = RuntimeErrorRepository.GetRecords();
            Assert.AreEqual(3, records.Sum(record => record.OccurrenceCount));
            Assert.AreEqual(2, records.Single(record => record.Error.Message == "context integration main").OccurrenceCount);
            foreach (var record in records) Assert.AreEqual("ErrorContextIntegrationScene", record.Error.SceneName);

            LogAssert.Expect(LogType.Error, "context integration pending");
            Debug.LogError("context integration pending");
            RuntimeErrorCollector.Clear();
            yield return null;
            yield return null;
            Assert.AreEqual(0, RuntimeErrorRepository.Count);

            RuntimeErrorCollector.SetCollecting(false);
            LogAssert.Expect(LogType.Error, "context integration disabled");
            Debug.LogError("context integration disabled");
            yield return null;
            Assert.AreEqual(0, RuntimeErrorRepository.Count);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RuntimeErrorCollector.Clear();
            RuntimeErrorCollector.SetCollecting(SessionState.GetBool(PreviousCollectingKey, true));
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
