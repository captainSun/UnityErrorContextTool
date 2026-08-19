using System.Collections.Generic;

namespace GameAle.EditorTools.RuntimeErrorContext
{
    public static class RuntimeErrorContextRegistry
    {
        private static readonly ErrorParserRegistry ParserRegistry = new ErrorParserRegistry(
            new IErrorParser[]
            {
                new XLuaErrorParser(),
                new CSharpErrorParser(),
                new FallbackErrorParser()
            });

        private static readonly List<ISourceResolver> SourceResolvers = new List<ISourceResolver>
        {
            new LuaSourceResolver(),
            new CSharpSourceResolver()
        };

        public static ParsedError Parse(RawLogEvent rawEvent)
        {
            return ParserRegistry.Parse(rawEvent);
        }

        public static List<ISourceResolver> GetSourceResolvers()
        {
            return new List<ISourceResolver>(SourceResolvers);
        }

        public static void RegisterParser(IErrorParser parser)
        {
            ParserRegistry.Register(parser);
        }

        public static void RegisterSourceResolver(ISourceResolver resolver)
        {
            if (resolver != null && !SourceResolvers.Contains(resolver))
            {
                SourceResolvers.Insert(0, resolver);
            }
        }
    }
}
