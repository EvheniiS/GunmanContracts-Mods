namespace VRHolsterCustomization
{
    // A canceled/completed hold cannot rearm until the buttons are released.
    internal sealed class AdjustmentHold
    {
        bool ready;
        float started;
        internal bool Active { get; private set; }

        internal bool TryStart(bool held, bool eligible, float now)
        {
            if (!held) { ready = true; return false; }
            if (!ready || Active) return false;
            ready = false;
            if (!eligible) return false;
            Active = true; started = now;
            return true;
        }

        internal bool Mature(float now) => Active && now - started >= 2f;
        internal void Cancel() { Active = ready = false; }
    }
}
