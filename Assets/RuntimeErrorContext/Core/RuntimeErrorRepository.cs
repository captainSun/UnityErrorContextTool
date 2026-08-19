using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal static class RuntimeErrorRepository
    {
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Dictionary<string, RuntimeErrorRecord> Records =
            new Dictionary<string, RuntimeErrorRecord>();

        public static event Action Changed;

        public static int Count
        {
            get { return Records.Count; }
        }

        public static RuntimeErrorRecord AddOrUpdate(ParsedError error, bool notifyChanged)
        {
            string fingerprint = BuildFingerprint(error);
            RuntimeErrorRecord record;
            if (Records.TryGetValue(fingerprint, out record))
            {
                record.LastOccurredAt = error.Timestamp;
                record.OccurrenceCount++;
                record.Error.RawMessage = error.RawMessage;
                record.Error.RawStackTrace = error.RawStackTrace;
            }
            else
            {
                while (Records.Count >= RuntimeErrorContextSettings.Current.MaxRecordCount)
                {
                    RemoveOldestRecord();
                }

                record = new RuntimeErrorRecord
                {
                    Fingerprint = fingerprint,
                    Error = error,
                    FirstOccurredAt = error.Timestamp,
                    LastOccurredAt = error.Timestamp,
                    OccurrenceCount = 1
                };
                Records.Add(fingerprint, record);
            }

            if (notifyChanged)
            {
                RaiseChanged();
            }

            return record;
        }

        public static void NotifyChanged()
        {
            RaiseChanged();
        }

        public static List<RuntimeErrorRecord> GetRecords()
        {
            List<RuntimeErrorRecord> result = new List<RuntimeErrorRecord>(Records.Values);
            result.Sort((left, right) => right.LastOccurredAt.CompareTo(left.LastOccurredAt));
            return result;
        }

        public static void Clear()
        {
            if (Records.Count == 0)
            {
                return;
            }

            Records.Clear();
            RaiseChanged();
        }

        private static string BuildFingerprint(ParsedError error)
        {
            ErrorStackFrame frame = error.PrimaryFrame;
            StringBuilder builder = new StringBuilder();
            builder.Append(error.Language).Append('|');
            builder.Append(error.Kind).Append('|');
            builder.Append(error.LogType).Append('|');
            builder.Append(error.ErrorType).Append('|');
            builder.Append(WhitespaceRegex.Replace(error.Message ?? string.Empty, " ")).Append('|');
            if (frame != null)
            {
                builder.Append(frame.RuntimeFilePath).Append('|');
                builder.Append(frame.LineNumber).Append('|');
                builder.Append(frame.FunctionName);
            }

            return builder.ToString();
        }

        private static void RemoveOldestRecord()
        {
            RuntimeErrorRecord oldest = null;
            foreach (RuntimeErrorRecord record in Records.Values)
            {
                if (oldest == null || record.LastOccurredAt < oldest.LastOccurredAt)
                {
                    oldest = record;
                }
            }

            if (oldest != null)
            {
                Records.Remove(oldest.Fingerprint);
            }
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
