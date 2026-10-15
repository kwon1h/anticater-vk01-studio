using System;

public enum ClickGesture { Single, Double, Triple, Long }

// Monotonic milliseconds; native input repeat must not create additional clicks.
public sealed class ClickDetector
{
    private readonly int doubleMs, tripleMs, longMs;
    private readonly Action<ClickGesture> emit;
    private int count;
    private bool down, cancelled, longFired;
    private long started, deadline;
    public bool IsDown { get { return down; } }
    public bool LongFired { get { return longFired; } }
    public bool Cancelled { get { return cancelled; } }
    public int PendingCount { get { return count; } }
    public long HoldMs(long now) { return down ? Math.Max(0, now - started) : 0; }
    public long WaitMs(long now) { return !down && count > 0 ? Math.Max(0, deadline - now) : 0; }

    public ClickDetector(int gapMs, int longMs, Action<ClickGesture> emit)
        : this(gapMs, gapMs, longMs, emit) { }

    public ClickDetector(int doubleMs, int tripleMs, int longMs, Action<ClickGesture> emit)
    {
        if (doubleMs < 120 || doubleMs > 1000 || tripleMs < 120 || tripleMs > 1000 || longMs < 150 || longMs > 3000)
            throw new ArgumentOutOfRangeException("Gesture timing outside supported range");
        this.doubleMs = doubleMs;
        this.tripleMs = tripleMs;
        this.longMs = longMs;
        this.emit = emit;
    }

    public void Down(long now)
    {
        if (down) return;
        if (count > 0 && now > deadline) Finish();
        down = true;
        cancelled = longFired = false;
        started = now;
    }

    public void Up(long now)
    {
        if (!down) return;
        down = false;
        if (cancelled || longFired || now < started) { Reset(); return; }
        if (now - started >= longMs)
        {
            Reset();
            emit(ClickGesture.Long);
            return;
        }
        count++;
        if (count == 3) { Finish(); return; }
        deadline = now + (count == 1 ? doubleMs : tripleMs);
    }

    public void Rotation()
    {
        count = 0;
        cancelled = true;
    }

    public void Tick(long now)
    {
        if (down && !cancelled && !longFired && now - started >= longMs)
        {
            count = 0;
            longFired = true;
            emit(ClickGesture.Long);
        }
        if (!down && count > 0 && now >= deadline) Finish();
    }

    public void Reset() { down = cancelled = longFired = false; count = 0; }

    private void Finish()
    {
        int completed = count;
        count = 0;
        if (completed == 1) emit(ClickGesture.Single);
        else if (completed == 2) emit(ClickGesture.Double);
        else if (completed == 3) emit(ClickGesture.Triple);
    }
}
