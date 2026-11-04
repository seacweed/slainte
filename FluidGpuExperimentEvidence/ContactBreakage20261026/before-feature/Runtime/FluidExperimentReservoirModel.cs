using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Area-preserving finite reservoir and an analytically integrated neck response.</summary>
    public static class FluidExperimentReservoirModel
    {
        public static float SurfaceHeight(Vector2[] uprightProjection, float fillFraction, out float height)
        {
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (Vector2 point in uprightProjection)
            { bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y); }
            height = Mathf.Max(.0001f, top - bottom);
            if (uprightProjection.Length < 3 || !float.IsFinite(height)) { height = 0; return 0; }
            float fraction = Mathf.Clamp01(fillFraction);
            if (fraction <= 0) return bottom;
            if (fraction >= 1) return top;
            double target = AreaBelow(uprightProjection, float.PositiveInfinity) * fraction;
            float low = bottom, high = top;
            for (int i = 0; i < 20; i++)
            {
                float middle = (low + high) * .5f;
                if (AreaBelow(uprightProjection, middle) < target) low = middle; else high = middle;
            }
            return (low + high) * .5f;
        }

        public static double AreaBelow(Vector2[] polygon, float height)
        {
            if (polygon == null || polygon.Length < 3) return 0;
            Vector2 first = default, previous = default, a = polygon[polygon.Length - 1];
            bool haveVertex = false, aInside = a.y <= height;
            double area = 0;
            foreach (Vector2 b in polygon)
            {
                bool bInside = b.y <= height;
                if (aInside != bInside)
                {
                    float t = (height - a.y) / (b.y - a.y);
                    AddVertex(new Vector2(Mathf.Lerp(a.x, b.x, t), height), ref first, ref previous, ref haveVertex, ref area);
                }
                if (bInside) AddVertex(b, ref first, ref previous, ref haveVertex, ref area);
                a = b; aInside = bInside;
            }
            if (haveVertex) area += (double)previous.x * first.y - (double)first.x * previous.y;
            return System.Math.Abs(area) * .5;
        }

        private static void AddVertex(Vector2 point, ref Vector2 first, ref Vector2 previous, ref bool found, ref double area)
        {
            if (found) area += (double)previous.x * point.y - (double)point.x * previous.y;
            else { first = point; found = true; }
            previous = point;
        }

        public static float Rate(float initial, float target, float duration, float responseTime)
            => target + (initial - target) * Mathf.Exp(-Mathf.Max(0, duration) / Mathf.Max(.001f, responseTime));

        public static float Integral(float initial, float target, float duration, float responseTime)
        {
            float time = Mathf.Max(0, duration), tau = Mathf.Max(.001f, responseTime);
            return Mathf.Max(0, target * time + (initial - target) * tau * (1 - Mathf.Exp(-time / tau)));
        }

        public static float BirthTime(float initial, float target, float duration, float responseTime, float amount)
        {
            float low = 0, high = Mathf.Max(0, duration);
            for (int i = 0; i < 20; i++)
            {
                float middle = (low + high) * .5f;
                if (Integral(initial, target, middle, responseTime) < amount) low = middle; else high = middle;
            }
            return (low + high) * .5f;
        }
    }
}
