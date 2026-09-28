namespace GrabFix;

// Real-time, bounded input intent. Kept independent of Unity so release/expiry/scene
// semantics can be tested without launching a VR game.
internal sealed class GripWindow
{
    bool initialized, held, used;
    internal double PressAt { get; private set; } = double.NegativeInfinity;
    internal int PressFrame { get; private set; } = -1;

    internal bool Update(bool down, bool activated, bool occupied, double now, int frame)
    {
        bool edge = down && (!held || !initialized) && activated;
        if (!initialized) { initialized = true; held = down; }
        if (edge) { PressAt = now; PressFrame = frame; used = occupied; }
        if (!down || occupied) used = true;
        held = down;
        return edge;
    }

    internal bool Open(double now, int frame, double duration)
        => initialized && held && !used && frame > PressFrame && duration > 0 && now >= PressAt && now - PressAt <= duration;
    internal void Consume() => used = true;
    internal void Reset() { initialized = false; held = used = false; PressAt = double.NegativeInfinity; PressFrame = -1; }
}
