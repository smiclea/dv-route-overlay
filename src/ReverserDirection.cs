using System;

namespace DVRouteOverlay
{
    internal static class ReverserDirection
    {
        // The game's common reverser control is normalized to 0=reverse, 0.5=neutral, 1=forward.
        // Steam cutoff positions use the same range, so either side of neutral selects direction.
        internal static int FromControlValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid reverser control value.");
            if (Math.Abs(value - 0.5f) <= 0.0001f) return 0;
            return value > 0.5f ? 1 : -1;
        }
    }
}
