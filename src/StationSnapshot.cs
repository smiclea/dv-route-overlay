using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        internal string NoteFor(string stationId, string pageLabel)
        {
            var text = new StringBuilder();
            text.Append("ROUTE SNAPSHOT #").Append(Revision).Append('\n').Append(pageLabel).Append('\n');
            StationVisit visit;
            if (Visits.TryGetValue(stationId, out visit))
            {
                text.Append("REACHED within ").Append(Km(Limit)).Append(" km\n");
                text.Append("First station track: ").Append(Km(visit.EntryDistance)).Append(" km ahead\n");
                text.Append("Tracks, in route order:\n");
                // Keep the slip readable; the full sequence remains in the refresh log.
                int shown = Math.Min(visit.Tracks.Count, 12);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) text.Append(" > ");
                    text.Append(visit.Tracks[i]);
                }
                if (shown < visit.Tracks.Count) text.Append(" (+").Append(visit.Tracks.Count - shown).Append(" more)");
                string yard = PageYard(pageLabel, stationId);
                if (!string.IsNullOrEmpty(yard))
                {
                    bool onPage = visit.Tracks.Exists(track => track.StartsWith(yard, StringComparison.OrdinalIgnoreCase));
                    text.Append('\n').Append(onPage ? "Includes tracks on this yard page." : "No labelled tracks on this yard page.");
                }
            }
            else
            {
                text.Append("NOT REACHED in this snapshot\n");
                text.Append("Look-ahead limit: ").Append(Km(Limit)).Append(" km\n");
                text.Append("Route traced: ").Append(Km(TracedDistance)).Append(" km\n");
                text.Append("It may lie beyond the traced route.\n");
                text.Append("Stop: ").Append(StopReason);
            }
            text.Append("\nReverser: ").Append(Reverser).Append(" | Use refresh key to update");
            return text.ToString();
        }

        private static string Km(double metres) { return (metres / 1000).ToString("0.0", CultureInfo.InvariantCulture); }

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
        private static string PageYard(string label, string stationId)
        {
            string page = NormalizePage(label);
            return page.StartsWith(stationId + "-", StringComparison.OrdinalIgnoreCase) ? page.Substring(stationId.Length + 1) : "";
        }
    }
}
