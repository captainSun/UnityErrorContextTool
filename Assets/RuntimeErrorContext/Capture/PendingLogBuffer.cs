using System;
using System.Collections.Generic;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class PendingLogBuffer
    {
        private readonly object m_Gate = new object();
        private readonly Queue<RawLogEvent> m_Events = new Queue<RawLogEvent>();
        private readonly int m_Capacity;
        private long m_DroppedCount;

        public PendingLogBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Capacity = capacity;
        }

        public long DroppedCount { get { lock (m_Gate) return m_DroppedCount; } }

        public bool TryEnqueue(RawLogEvent logEvent)
        {
            lock (m_Gate)
            {
                if (m_Events.Count >= m_Capacity)
                {
                    m_DroppedCount++;
                    return false;
                }
                m_Events.Enqueue(logEvent);
                return true;
            }
        }

        public bool TryDequeue(out RawLogEvent logEvent)
        {
            lock (m_Gate)
            {
                logEvent = m_Events.Count > 0 ? m_Events.Dequeue() : null;
                return logEvent != null;
            }
        }

        public void Clear()
        {
            lock (m_Gate)
            {
                m_Events.Clear();
                m_DroppedCount = 0;
            }
        }
    }
}
