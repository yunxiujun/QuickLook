namespace QuickLook.Enhancements;

// Track consumed key events ourselves. A suppressed low-level keyboard event must
// not depend on GetAsyncKeyState to remain active.
internal sealed class HoldRepeat
{
    private long _next;
    private int _interval;
    internal bool Active { get; private set; }

    internal void Start(long now, int delay, int interval)
    {
        _next = now + delay;
        _interval = interval;
        Active = true;
    }

    internal bool Tick(long now)
    {
        if (!Active || now < _next) return false;
        _next = now + _interval;
        return true;
    }

    internal void Stop() => Active = false;
}
