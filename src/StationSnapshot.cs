using System;
using System.Collections.Generic;

namespace DVRouteOverlay
{
    internal sealed class StationTrackAddress
    {
        internal string StationId, TrackLabel;
    }

    internal sealed class StationVisit
    {
        internal double EntryDistance;
        internal readonly List<string> Tracks = new List<string>();
    }

    internal sealed class StationSnapshot
    {
        internal readonly Dictionary<string, StationVisit> Visits = new Dictionary<string, StationVisit>(StringComparer.OrdinalIgnoreCase);
        internal double Limit, TracedDistance;
        internal int Revision;
        internal string Reverser, StopReason;

        internal static StationSnapshot Build<T>(RouteWalker.Result<T> route, Func<T, StationTrackAddress> addressOf,
            double limit, int revision, string reverser) where T : class
        {
            var snapshot = new StationSnapshot { Limit = limit, TracedDistance = route.Distance,
                Revision = revision, Reverser = reverser, StopReason = route.StopReason };
            double distance = 0;
            foreach (var segment in route.Segments)
            {
                double covered = Math.Abs(segment.To - segment.From);
                if (covered <= 0) continue;
                var address = addressOf(segment.Track);
                if (address != null && !string.IsNullOrEmpty(address.StationId))
                {
                    StationVisit visit;
                    if (!snapshot.Visits.TryGetValue(address.StationId, out visit))
                    {
                        visit = new StationVisit { EntryDistance = distance };
                        snapshot.Visits.Add(address.StationId, visit);
                    }
                    string label = string.IsNullOrEmpty(address.TrackLabel) ? "Unlabelled track" : address.TrackLabel;
                    if (visit.Tracks.Count == 0 || visit.Tracks[visit.Tracks.Count - 1] != label) visit.Tracks.Add(label);
                }
                distance += covered;
            }
            return snapshot;
        }

        internal string FooterFor(string stationId)
        {
            StationVisit visit;
            if (!Visits.TryGetValue(stationId, out visit) || visit.Tracks.Count == 0)
                return "Not passing (in snapshot)";
            int shown = Math.Min(visit.Tracks.Count, 8);
            string sequence = string.Join(" -> ", visit.Tracks.GetRange(0, shown));
            return "Passing through " + sequence + (shown < visit.Tracks.Count ? " (+" + (visit.Tracks.Count - shown) + " more)" : "");
        }

        internal static string MatchPage(string label, IEnumerable<string> stationIds)
        {
            string page = NormalizePage(label);
            string found = null;
            foreach (string id in stationIds)
                if (!string.IsNullOrEmpty(id) && (page.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    page.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)) && (found == null || id.Length > found.Length)) found = id;
            return found;
        }

        internal static string NormalizePage(string label) { return (label ?? "").Trim().Trim('[', ']').Trim(); }
    }
}
