namespace SephiriaSkins.Core;
// Preparation is isolated. A failed read/decode must never tear down the active theme.
public sealed class ThemeTransaction<T> where T : class, IDisposable
{
    public T? Current { get; private set; }
    public bool TryPrepare(Func<T> prepare, Action<T?, T?> commit, out string? error)
    {
        T? next = null;
        try { next = prepare(); }
        catch (Exception e) { error = e.Message; return false; }
        var previous = Current;
        try { commit(previous, next); Current = next; }
        catch (Exception e)
        {
            next.Dispose(); error = e.Message;
            return false;
        }
        previous?.Dispose(); error = null; return true;
    }
    public void Restore(Action<T?, T?> commit)
    {
        var old = Current; commit(old, null); Current = null; old?.Dispose();
    }
}
