namespace GameAle.EditorTools.RuntimeErrorContext
{
    public interface ISourceResolver
    {
        bool CanResolve(ErrorStackFrame frame);
        bool TryResolve(ErrorStackFrame frame, out ResolvedSourceLocation location);
    }
}
