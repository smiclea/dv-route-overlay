using System;
using System.Linq;
using DVRouteOverlay;

class Program
{
    sealed class Track
    {
        internal double Length = 100;
        internal RouteWalker.Connection<Track> In, Out;
        internal StationTrackAddress Address;
    }
    static RouteWalker.Result<Track> Walk(Track start, double span = 25, bool forward = true, double limit = 1000)
        => RouteWalker.Walk(start, span, forward, limit, t => t.Length, (t, d) => d ? t.Out : t.In);
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static void Main()
    {
        var a = new Track(); var b = new Track(); var c = new Track();
        a.Out = new RouteWalker.Connection<Track>(b, true);
        var r = Walk(a);
        Check(r.Distance == 175 && r.Segments.Count == 2 && r.StopReason == "End of connected track", "Starts at the train, ends at disconnected track");
        a.Out = new RouteWalker.Connection<Track>(b, false);
        b.In = new RouteWalker.Connection<Track>(c, true);
        r = Walk(a);
        Check(r.Distance == 275 && r.Segments[1].From == 100 && r.Segments[1].To == 0 && r.Segments[2].To == 100,
            "Preserves direction when entering the last endpoint of a track");
        r = Walk(a, limit: 120);
        Check(r.Distance == 120 && r.Segments.Last().To == 55 && r.StopReason == "Distance limit", "Clips the final segment exactly to the distance limit");
        a.In = new RouteWalker.Connection<Track>(c, false);
        r = Walk(a, forward: false);
        Check(r.Distance == 125 && r.Segments[0].To == 0 && r.Segments[1].From == 100, "Walks backward from the train without drawing track behind it");
        a.Out = new RouteWalker.Connection<Track>(a, true);
        r = Walk(a);
        Check(r.Segments.Count == 1 && r.StopReason == "Loop", "Stops at a repeated directed track");
        var selected = b;
        Func<Track, bool, RouteWalker.Connection<Track>> next = (t, d) => t == a
            ? new RouteWalker.Connection<Track>(selected, true) : default;
        var first = RouteWalker.Walk(a, 0, true, 1000, t => t.Length, next);
        selected = c;
        var second = RouteWalker.Walk(a, 0, true, 1000, t => t.Length, next);
        Check(first.Segments[1].Track == b && second.Segments[1].Track == c && first.Segments.Count == 2,
            "Uses only the current switch selection and updates when it changes");
        r = Walk(new Track { Length = double.NaN });
        Check(r.Distance == 0 && r.StopReason == "Track geometry unavailable", "Handles missing geometry without an infinite walk");
        r = Walk(new Track(), span: 150);
        Check(r.Distance == 0, "Clamps a stale train span at the endpoint");
        a.Out = new RouteWalker.Connection<Track>(b, true);
        b.Out = new RouteWalker.Connection<Track>(a, false);
        a.In = default;
        r = Walk(a);
        Check(r.Segments.Count == 3 && r.Segments[2].From == 100 && r.Segments[2].To == 0,
            "Can revisit a track in the opposite direction before stopping");
        SamplingAndReverserChecks();
        StationSnapshotChecks();
        Console.WriteLine("All 29 route, sampling, reverser and station snapshot checks passed.");
    }

    static void SamplingAndReverserChecks()
    {
        double length = 123.4;
        int count = 9;
        double legacyEndpoint = length * count / count;
        Check(legacyEndpoint > length && SamplingMath.SampleSpan(0, length, count, count, length) == length,
            "Regression: exact endpoint avoids the floating-point overshoot that blanked the map");
        Check(SamplingMath.SampleSpan(3936.294475673646, 10764.051885703115, 456, 456, 10764.051885703115) == 10764.051885703115,
            "Regression: partial-track sampling preserves the exact final endpoint");
        bool bounded = true;
        for (int step = 0; step <= count; step++)
        {
            double forward = SamplingMath.SampleSpan(-0.01, length + 0.01, step, count, length);
            double reverse = SamplingMath.SampleSpan(length + 0.01, -0.01, step, count, length);
            bounded &= forward >= 0 && forward <= length && reverse >= 0 && reverse <= length;
        }
        Check(bounded, "Every forward/reverse sample is clamped to the available point-set span");

        var spans = new[] { 0.0, 0.2, 3.5, 12.0, 12.0, 99.0, 123.4 };
        bool lookupMatches = true;
        foreach (double sample in new[] { 0.0, 0.1, 0.2, 1.0, 3.5, 11.0, 12.0, 13.0, 99.0, 123.4 })
        {
            int expected = 0;
            while (expected < spans.Length - 2 && spans[expected + 1] <= sample) expected++;
            lookupMatches &= SamplingMath.FindInterval(spans, sample, value => value) == expected;
        }
        Check(lookupMatches, "Binary lookup matches track interval boundaries, including duplicate spans");
        var longTrack = Enumerable.Range(0, 100000).Select(i => (double)i).ToArray();
        int comparisons = 0;
        int index = SamplingMath.FindInterval(longTrack, 98765.5, value => { comparisons++; return value; });
        Check(index == 98765 && comparisons <= 17, "Long-track lookup uses logarithmic work instead of rescanning every point");

        Check(ReverserDirection.FromControlValue(1) == 1 && ReverserDirection.FromControlValue(0) == -1,
            "Forward/reverse come only from the normalized reverser value");
        Check(ReverserDirection.FromControlValue(0.5f) == 0 && ReverserDirection.FromControlValue(0.50001f) == 0,
            "Neutral never assumes a direction or reuses movement history");
        Check(ReverserDirection.FromControlValue(0.6f) == 1 && ReverserDirection.FromControlValue(0.4f) == -1,
            "Analog steam cutoff selects the side of neutral");
        bool invalidRejected = true;
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 2f })
        {
            try { ReverserDirection.FromControlValue(invalid); invalidRejected = false; }
            catch (ArgumentOutOfRangeException) { }
        }
        Check(invalidRejected, "Invalid reverser values cannot silently fall back to a direction");
        bool invalidSpanRejected = false;
        try { SamplingMath.SampleSpan(0, double.NaN, 9, 9, length); }
        catch (ArgumentOutOfRangeException) { invalidSpanRejected = true; }
        Check(invalidSpanRejected, "Corrupt span data is rejected with a useful failure instead of invalid vertices");
    }

    static void StationSnapshotChecks()
    {
        var approach = new Track();
        var a1 = new Track { Address = new StationTrackAddress { StationId = "SM", TrackLabel = "A1S" } };
        var a1Again = new Track { Address = new StationTrackAddress { StationId = "SM", TrackLabel = "A1S" } };
        var b2 = new Track { Address = new StationTrackAddress { StationId = "SM", TrackLabel = "B2I" } };
        var beyond = new Track { Address = new StationTrackAddress { StationId = "CME", TrackLabel = "A1S" } };
        approach.Out = new RouteWalker.Connection<Track>(a1, true);
        a1.Out = new RouteWalker.Connection<Track>(a1Again, false);
        a1Again.In = new RouteWalker.Connection<Track>(b2, true);
        b2.Out = new RouteWalker.Connection<Track>(beyond, true);
        var route = Walk(approach, limit: 320);
        var snapshot = StationSnapshot.Build(route, track => track.Address, 320, 7, "forward");
        Check(snapshot.Visits["SM"].EntryDistance == 75, "Station distance includes the remaining partial approach track");
        Check(snapshot.Visits["SM"].Tracks.SequenceEqual(new[] { "A1S", "B2I" }), "Station track order handles reversed segments and consecutive duplicate labels");
        Check(!snapshot.Visits.ContainsKey("CME"), "A station beyond the selected route limit is not marked as reached");
        var atStation = StationSnapshot.Build(Walk(a1, limit: 40), track => track.Address, 40, 8, "reverse");
        Check(atStation.Visits["SM"].EntryDistance == 0, "Starting inside a station records zero distance to its first track");
        Check(StationSnapshot.MatchPage("[SM-A]", new[] { "SM", "CME" }) == "SM" &&
            StationSnapshot.MatchPage("[SM-B]", new[] { "SM", "CME" }) == "SM", "Different yard pages map to the same station");
        Check(StationSnapshot.MatchPage("[ST-E-A]", new[] { "ST", "ST-E" }) == "ST-E" &&
            StationSnapshot.MatchPage("[SME-A]", new[] { "SM" }) == null, "Page matching respects station boundaries and prefers the longest exact station ID");
        Check(StationSnapshot.MatchPage("[Cover]", new[] { "SM" }) == null &&
            StationSnapshot.MatchPage("[Legend]", new[] { "SM" }) == null, "Cover and legend cannot accidentally receive station notes");
        string note = snapshot.NoteFor("CME", "CME-A");
        Check(note.Contains("NOT REACHED in this snapshot") && note.Contains("Route traced: 0.3 km") && note.Contains("beyond the traced route"),
            "Unreached station text describes the bounded snapshot without claiming the station is off-route");
        Check(snapshot.NoteFor("SM", "SM-A").Contains("Includes tracks on this yard page") &&
            snapshot.NoteFor("SM", "SM-C").Contains("No labelled tracks on this yard page"), "Notes distinguish the current yard page from other yards at the station");
        var zeroRoute = new RouteWalker.Result<Track>();
        zeroRoute.Segments.Add(new RouteWalker.Segment<Track> { Track = a1, From = 100, To = 100 });
        var zero = StationSnapshot.Build(zeroRoute, track => track.Address, 15000, 9, "forward");
        Check(zero.Visits.Count == 0 && snapshot.NoteFor("SM", "SM-A").Contains("SNAPSHOT #7"),
            "Zero-length station contact is excluded and cached notes retain their refresh identity");
    }
}
