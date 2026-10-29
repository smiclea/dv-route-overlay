using System;
using System.Collections.Generic;

namespace DVRouteOverlay
{
    // This walks one already-selected connection per track; it never searches alternatives.
    internal static class RouteWalker
    {
        internal struct Connection<T> where T : class
        {
            internal T Track;
            internal bool Increasing;
            internal Connection(T track, bool increasing) { Track = track; Increasing = increasing; }
        }

        internal struct Segment<T> where T : class
        {
            internal T Track;
            internal double From, To;
        }

        internal sealed class Result<T> where T : class
        {
            internal readonly List<Segment<T>> Segments = new List<Segment<T>>();
            internal double Distance;
            internal string StopReason;
        }

        internal static Result<T> Walk<T>(T start, double span, bool increasing, double limit,
            Func<T, double> lengthOf, Func<T, bool, Connection<T>> next) where T : class
        {
            var result = new Result<T>();
            var visited = new HashSet<Tuple<T, bool>>();
            T track = start;
            while (track != null && result.Segments.Count < 2048)
            {
                if (!visited.Add(Tuple.Create(track, increasing)))
                { result.StopReason = "Loop"; return result; }
                double length = lengthOf(track);
                if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
                { result.StopReason = "Track geometry unavailable"; return result; }
                span = Math.Max(0, Math.Min(length, span));
                double available = increasing ? length - span : span;
                double distance = Math.Min(available, Math.Max(0, limit - result.Distance));
                result.Segments.Add(new Segment<T> { Track = track, From = span,
                    To = Math.Max(0, Math.Min(length, span + (increasing ? distance : -distance))) });
                result.Distance += distance;
                if (result.Distance >= limit)
                { result.StopReason = "Distance limit"; return result; }
                var connection = next(track, increasing);
                track = connection.Track;
                increasing = connection.Increasing;
                span = track != null && !increasing ? lengthOf(track) : 0;
            }
            result.StopReason = track == null ? "End of connected track" : "Track limit";
            return result;
        }
    }
}
