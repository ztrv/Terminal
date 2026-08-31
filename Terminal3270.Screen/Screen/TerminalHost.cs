namespace Terminal3270.Screen;

/// <summary>
/// The embeddable entry point: hand it a buffer, it owns the console until you
/// stop it. This is what the host application calls.
/// </summary>
public sealed class TerminalHost
{
    private readonly ScreenBuffer _buffer;
    private readonly object _gate = new();
    private volatile bool _running;

    /// <summary>How long to wait between polls when no key is pending.</summary>
    public TimeSpan IdlePoll { get; init; } = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Raised for every mapped keystroke, inside the render lock, before the
    /// buffer sees it. Useful for diagnostics, macro recording, or an echo demo.
    /// </summary>
    public event Action<TerminalAction>? KeyPressed;

    public TerminalHost(ScreenBuffer buffer) => _buffer = buffer;

    public void Stop() => _running = false;

    public void Run(CancellationToken cancel = default)
    {
        using var session = ConsoleVtSession.Begin();
        var renderer = new ConsoleRenderer();

        _running = true;
        renderer.Invalidate();
        lock (_gate) renderer.Paint(_buffer);

        while (_running && !cancel.IsCancellationRequested)
        {
            var dirty = false;

            // Drain everything pending before painting, so held-down keys and
            // pasted text don't render one character per frame.
            while (Console.KeyAvailable)
            {
                var action = ConsoleKeyMap.Map(Console.ReadKey(intercept: true));
                if (action is TerminalAction.Quit) { _running = false; break; }

                lock (_gate)
                {
                    KeyPressed?.Invoke(action);   // inside the lock: handlers may write
                    _buffer.Apply(action);
                }
                dirty = true;
            }

            // There is no resize event on System.Console — polling is the only
            // way, which is the real reason this loop exists at all.
            if (renderer.PollResize()) dirty = true;

            // The lock matters once Post() is in use: without it a background
            // thread can mutate rows mid-frame and you paint a torn screen.
            if (dirty) lock (_gate) renderer.Paint(_buffer);
            else Thread.Sleep(IdlePoll);
        }
    }

    /// <summary>Call from a background thread when host data arrives.</summary>
    public void Post(Action<ScreenBuffer> update)
    {
        lock (_gate) update(_buffer);
    }
}
