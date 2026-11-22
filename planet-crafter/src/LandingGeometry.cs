// ChatGPT C56: capsule support geometry, independently testable without Unity.
using System;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal static class LandingGeometry
    {
        public static bool TryPivotY(float floor, float normalY, float height, float radius,
            float centerOffsetY, float skin, out float pivot)
        {
            pivot = 0;
            if (!Finite(floor) || !Finite(normalY) || !Finite(height) || !Finite(radius)
                || !Finite(centerOffsetY) || !Finite(skin) || normalY <= 0 || normalY > 1.001f
                || radius <= 0 || height < radius * 2 || skin < 0) return false;
            // On a slope the lower sphere needs r / normalY vertically, not just r.
            float support = radius / normalY - radius;
            pivot = floor + height / 2 - centerOffsetY + support + Math.Max(skin, .03f);
            return Finite(pivot);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
