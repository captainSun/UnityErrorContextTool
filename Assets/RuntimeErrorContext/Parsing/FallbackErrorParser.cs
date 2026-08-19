namespace GameAle.EditorTools.RuntimeErrorContext
{
    internal sealed class FallbackErrorParser : IErrorParser
    {
        public int Priority
        {
            get { return 0; }
        }

        public bool CanParse(RawLogEvent rawEvent)
        {
            return rawEvent != null;
        }

        public ParsedError Parse(RawLogEvent rawEvent)
        {
            ParsedError result = ErrorParsingUtility.CreateBaseError(rawEvent, ErrorLanguage.Unknown);
            result.Kind = rawEvent.LogType == UnityEngine.LogType.Assert
                ? ErrorKind.Assert
                : ErrorKind.Unknown;
            return result;
        }
    }
}
