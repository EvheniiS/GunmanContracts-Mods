using System;

namespace ModSettings
{
    internal static class PointerGeometry
    {
        // Local board coordinates: front is -Z. Reject back-facing, parallel, distant and off-board rays.
        internal static bool Hit(float x, float y, float z, float dx, float dy, float dz,
            float scale, float width, float height, out float hitX, out float hitY)
        {
            hitX = hitY = 0;
            if (z >= 0 || dz <= 0.05f || scale <= 0) return false;
            float distance = -z / dz;
            hitX = x + dx * distance;
            hitY = y + dy * distance;
            return float.IsFinite(distance) && distance * scale <= 4f &&
                Math.Abs(hitX) < width / 2 && Math.Abs(hitY) < height / 2;
        }
    }
}
