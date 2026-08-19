using System.Collections.Generic;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    public interface IErrorParser
    {
        int Priority { get; }
        bool CanParse(RawLogEvent rawEvent);
        ParsedError Parse(RawLogEvent rawEvent);
    }

    internal sealed class ErrorParserRegistry
    {
        private readonly List<IErrorParser> m_Parsers = new List<IErrorParser>();

        public ErrorParserRegistry(IEnumerable<IErrorParser> parsers)
        {
            m_Parsers.AddRange(parsers);
            SortParsers();
        }

        public void Register(IErrorParser parser)
        {
            if (parser == null || m_Parsers.Contains(parser))
            {
                return;
            }

            m_Parsers.Add(parser);
            SortParsers();
        }

        public ParsedError Parse(RawLogEvent rawEvent)
        {
            for (int i = 0; i < m_Parsers.Count; i++)
            {
                IErrorParser parser = m_Parsers[i];
                if (parser.CanParse(rawEvent))
                {
                    return parser.Parse(rawEvent);
                }
            }

            return null;
        }

        private void SortParsers()
        {
            m_Parsers.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }
    }
}
