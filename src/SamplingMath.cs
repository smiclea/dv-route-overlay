using System;

namespace DVRouteOverlay
{
    internal static class SamplingMath
    {
        internal static double SampleSpan(double from, double to, int step, int count, double length)
        {
            if (count < 1 || double.IsNaN(length) || double.IsInfinity(length) || length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            // Preserve exact endpoints: to*count/count can be larger than to by one ULP.
            double span = step <= 0 ? from : step >= count ? to : from + (to - from) * ((double)step / count);
            if (double.IsNaN(span) || double.IsInfinity(span))
                throw new ArgumentOutOfRangeException(nameof(span));
            return Math.Max(0, Math.Min(length, span));
        }

        internal static int FindInterval<T>(T[] points, double span, Func<T, double> spanOf)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("At least two track points are required.");
            int low = 0, high = points.Length - 1;
            while (low + 1 < high)
            {
                int middle = low + (high - low) / 2;
                if (spanOf(points[middle]) <= span) low = middle;
                else high = middle;
            }
            return low;
        }
    }
}
