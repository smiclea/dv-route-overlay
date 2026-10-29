using System;
using System.Collections.Generic;
using System.Globalization;
using DV.PointSet;
using UnityEngine;
using UnityModManagerNet;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DVRouteOverlay
{
    public sealed class OverlayController : MonoBehaviour
    {
        internal string Status { get; private set; } = "Enter a locomotive, open the world map, and press the refresh key.";
        private readonly Dictionary<MapMarkersController, MapRibbon> maps = new Dictionary<MapMarkersController, MapRibbon>();
        private readonly List<Vector3> routePoints = new List<Vector3>();
        private readonly StationManualNotes stationNotes = new StationManualNotes();
        private TrainCar lastCar;
        private bool refreshRequested;
        private bool lastShowOverlay = true;
        private bool lastShowStationNotes = true;
        private float nextErrorLog;
        private int refreshSequence;
        private string refreshStage;
        private RailTrack samplingTrack;
        private double samplingFrom, samplingTo, samplingLength, samplingSpan;
        private static readonly Func<EquiPointSet.Point, double> PointSpan = point => point.span;

        internal void RequestRefresh() { refreshRequested = true; }

        private void Update()
        {
            try
            {
                CheckLocomotive();
                if (Main.Settings.ShowOverlay != lastShowOverlay)
                {
                    lastShowOverlay = Main.Settings.ShowOverlay;
                    foreach (var map in maps.Values) map.SetVisible(lastShowOverlay);
                }
                if (Main.Settings.ShowStationNotes != lastShowStationNotes)
                {
                    lastShowStationNotes = Main.Settings.ShowStationNotes;
                    stationNotes.SetVisible(lastShowStationNotes);
                }
                // Ignore the hotkey while UMM is open, including its keybinding capture popup.
                // The settings button queues the same one-shot refresh for this Update.
                if ((!UnityModManager.UI.Instance || !UnityModManager.UI.Instance.Opened) && Main.Settings.RefreshKey.Down())
                    refreshRequested = true;
                if (!refreshRequested) return;
                refreshRequested = false;
                RefreshWithDiagnostics();
            }
            catch (Exception ex)
            {
                Clear();
                Status = "Overlay unavailable; see the Unity Mod Manager log.";
                if (Time.unscaledTime >= nextErrorLog)
                {
                    Main.Entry.Logger.Error(ex.ToString());
                    nextErrorLog = Time.unscaledTime + 30;
                }
            }
        }

        private void CheckLocomotive()
        {
            // Track identity only. Direction is read exclusively from the reverser on request.
            // Standing on a wagon must not replace the last locomotive with a car lacking a reverser.
            var car = PlayerManager.LastLoco;
            if (car != lastCar)
            {
                Clear();
                lastCar = car;
                Status = "Enter a locomotive, open the world map, and press the refresh key.";
            }
        }

        private void RefreshWithDiagnostics()
        {
            int request = ++refreshSequence;
            var clock = Stopwatch.StartNew();
            refreshStage = "reading locomotive/reverser";
            samplingTrack = null;
            Log("Refresh #{0} requested; loco={1}, range={2:0} km, overlay={3}.", request,
                lastCar ? lastCar.ID : "none", Main.Settings.DistanceKm, Main.Settings.ShowOverlay);
            try { Refresh(request, clock); }
            catch (Exception ex)
            {
                // Sampling finishes before touching the existing meshes. Keep that snapshot if a refresh fails.
                Status = "Refresh failed; any previous snapshot is retained. See the UMM log.";
                Main.Entry.Logger.Error(string.Format(CultureInfo.InvariantCulture,
                    "Refresh #{0} failed during {1} after {2:0.0} ms; samplingTrack={3}, from={4:R}, to={5:R}, length={6:R}, sample={7:R}.\n{8}",
                    request, refreshStage, clock.Elapsed.TotalMilliseconds,
                    samplingTrack ? samplingTrack.name : "none", samplingFrom, samplingTo, samplingLength, samplingSpan, ex));
            }
        }

        private static void Log(string format, params object[] args)
        { Main.Entry.Logger.Log(string.Format(CultureInfo.InvariantCulture, format, args)); }

        private void RejectRefresh(int request, string message)
        {
            Clear();
            Status = message;
            Log("Refresh #{0}: {1}", request, message);
        }

        private void Refresh(int request, Stopwatch clock)
        {
            // LastLoco keeps the overlay useful while you step out to throw a switch.
            var car = lastCar;
            if (!car) { RejectRefresh(request, "Enter a locomotive to display its route."); return; }
            var sim = car.SimController;
            var controls = sim ? sim.controlsOverrider : null;
            var reverser = controls ? controls.Reverser : null;
            if (!reverser) { RejectRefresh(request, "This locomotive's reverser is unavailable; no direction was assumed."); return; }
            float reverserValue = reverser.Value;
            int direction = ReverserDirection.FromControlValue(reverserValue);
            Log("Refresh #{0}: reverser={1:R} ({2}), source={3}.", request, reverserValue,
                direction > 0 ? "forward" : direction < 0 ? "reverse" : "neutral", reverser.portId);
            if (direction == 0) { RejectRefresh(request, "Reverser is neutral. Select forward or reverse, then refresh."); return; }
            var bogie = direction > 0 ? car.FrontBogie : car.RearBogie;
            if (!bogie || !bogie.fullyInitialized || bogie.HasDerailed || !bogie.track || bogie.traveller == null)
            { RejectRefresh(request, "Wait for a locomotive on the rails, then refresh again."); return; }
            if (bogie.TrackDirectionSign == 0)
            { RejectRefresh(request, "The bogie's track orientation is not initialized; refresh again once ready."); return; }

            // Native bogie orientation converts the reverser sign to the track's coordinate order.
            // It does not choose a direction from velocity, vehicle facing or remembered motion.
            bool increasing = bogie.TrackDirectionSign * direction > 0;
            Log("Refresh #{0}: startTrack={1}, span={2:R}, bogieTrackSign={3}, increasing={4}.",
                request, bogie.track.name, bogie.traveller.Span, bogie.TrackDirectionSign, increasing);
            refreshStage = "walking selected track connections";
            double walkStart = clock.Elapsed.TotalMilliseconds;
            var route = RouteWalker.Walk(bogie.track, bogie.traveller.Span, increasing,
                Main.Settings.DistanceKm * 1000, TrackLength, NextTrack);
            double walkedAt = clock.Elapsed.TotalMilliseconds;
            refreshStage = "sampling track points";
            routePoints.Clear();
            foreach (var segment in route.Segments) Sample(segment);
            double sampledAt = clock.Elapsed.TotalMilliseconds;
            refreshStage = "discovering map controllers";
            DiscoverMaps();
            double mapsAt = clock.Elapsed.TotalMilliseconds;
            refreshStage = "drawing map ribbons";
            int visible = 0;
            foreach (var item in maps)
            {
                if (!item.Key) { item.Value.Hide(); continue; }
                bool active = item.Key.isActiveAndEnabled && Main.Settings.ShowOverlay;
                bool drawn = false;
                if (!active) item.Value.Hide();
                else drawn = item.Value.Draw(routePoints, Main.Settings.WidthMm * 0.001f, RouteColor());
                if (drawn) visible++;
                Log("Refresh #{0}: map={1}, scene={2}, active={3}, drawn={4}, vertices={5}.",
                    request, item.Key.name, item.Key.gameObject.scene.name, active, drawn, item.Value.VertexCount);
            }
            Status = string.Format("Snapshot: {0:0.0} km | Reverser {1} | {2} | {3}", route.Distance / 1000,
                direction > 0 ? "forward" : "reverse", route.StopReason,
                visible > 0 ? "Press the refresh key to update" : routePoints.Count < 2
                    ? "No drawable track in this direction" : "Open the world map and refresh again");
            Log("Refresh #{0} complete: distance={1:0.0} m, tracks={2}, samples={3}, drawnMaps={4}/{5}, stop={6}; total={7:0.0} ms (walk={8:0.0}, sample={9:0.0}, maps={10:0.0}, draw={11:0.0}).",
                request, route.Distance, route.Segments.Count, routePoints.Count, visible, maps.Count, route.StopReason,
                clock.Elapsed.TotalMilliseconds, walkedAt - walkStart, sampledAt - walkedAt, mapsAt - sampledAt, clock.Elapsed.TotalMilliseconds - mapsAt);
            try
            {
                double notesStart = clock.Elapsed.TotalMilliseconds;
                var addresses = StationAddresses(route);
                var snapshot = StationSnapshot.Build(route, track => addresses[track], Main.Settings.DistanceKm * 1000,
                    request, direction > 0 ? "forward" : "reverse");
                stationNotes.SetVisible(Main.Settings.ShowStationNotes);
                stationNotes.Publish(snapshot);
                Log("Refresh #{0}: station notes prepared in {1:0.0} ms; combined total={2:0.0} ms.",
                    request, clock.Elapsed.TotalMilliseconds - notesStart, clock.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                // A manual integration failure must not discard the world-map route.
                stationNotes.Clear();
                Main.Entry.Logger.Error("Station notes could not be updated; world-map route retained. " + ex);
            }
        }

        private static Dictionary<RailTrack, StationTrackAddress> StationAddresses(RouteWalker.Result<RailTrack> route)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assigned = new Dictionary<RailTrack, string>();
            foreach (var station in StationController.allStations)
            {
                if (!station || station.stationInfo == null) continue;
                string id = station.stationInfo.YardID;
                known.Add(id);
                if (station.AllStationTracks != null)
                    foreach (var track in station.AllStationTracks) if (track) assigned[track] = id;
            }
            var addresses = new Dictionary<RailTrack, StationTrackAddress>();
            foreach (var segment in route.Segments)
            {
                if (addresses.ContainsKey(segment.Track)) continue;
                var logic = segment.Track.LogicTrack();
                var id = logic != null ? logic.ID : null;
                string stationId = id != null && known.Contains(id.yardId ?? "") ? id.yardId : null;
                if (stationId == null) assigned.TryGetValue(segment.Track, out stationId);
                addresses[segment.Track] = stationId == null ? null : new StationTrackAddress {
                    StationId = stationId, TrackLabel = id != null ? id.TrackPartOnly : segment.Track.name };
            }
            return addresses;
        }

        private static double TrackLength(RailTrack track)
        {
            var points = track.GetKinkedPointSet();
            return points != null ? points.span : 0;
        }

        private static RouteWalker.Connection<RailTrack> NextTrack(RailTrack track, bool increasing)
        {
            // Native getters read the existing switch selection, including trailing approaches.
            // Branch.first explicitly describes the entry end on the NEXT track.
            var branch = increasing ? track.GetOutBranch() : track.GetInBranch();
            return branch != null && branch.track
                ? new RouteWalker.Connection<RailTrack>(branch.track, branch.first)
                : default(RouteWalker.Connection<RailTrack>);
        }

        private void Sample(RouteWalker.Segment<RailTrack> segment)
        {
            samplingTrack = segment.Track;
            samplingFrom = segment.From;
            samplingTo = segment.To;
            var pointSet = segment.Track.GetKinkedPointSet();
            if (pointSet == null || pointSet.points == null || pointSet.points.Length < 2) return;
            samplingLength = Math.Min(pointSet.span, pointSet.points[pointSet.points.Length - 1].span);
            double distance = Math.Abs(segment.To - segment.From);
            int count = Math.Max(1, (int)Math.Ceiling(distance / 15));
            for (int i = 0; i <= count; i++)
            {
                double span = SamplingMath.SampleSpan(segment.From, segment.To, i, count, samplingLength);
                samplingSpan = span;
                int index = SamplingMath.FindInterval(pointSet.points, span, PointSpan);
                var a = pointSet.points[index];
                var b = pointSet.points[index + 1];
                float t = b.span > a.span ? (float)((span - a.span) / (b.span - a.span)) : 0;
                // Point sets already use absolute world coordinates, unaffected by floating origin.
                Vector3 point = Vector3.Lerp((Vector3)a.position, (Vector3)b.position, t);
                if (routePoints.Count == 0 || (routePoints[routePoints.Count - 1] - point).sqrMagnitude > 0.01f)
                    routePoints.Add(point);
            }
        }

        private void DiscoverMaps()
        {
            var dead = new List<MapMarkersController>();
            foreach (var item in maps) if (!item.Key) { item.Value.Dispose(); dead.Add(item.Key); }
            foreach (var key in dead) maps.Remove(key);
            foreach (var map in Resources.FindObjectsOfTypeAll<MapMarkersController>())
            {
                if (!map.gameObject.scene.IsValid() || !map.gameObject.scene.isLoaded || maps.ContainsKey(map)) continue;
                maps.Add(map, new MapRibbon(map));
            }
        }

        private static Color RouteColor()
        {
            switch (Main.Settings.Color)
            {
                case 1: return new Color(0.2f, 1, 0.15f);
                case 2: return new Color(1, 0.45f, 0.05f);
                default: return new Color(0, 0.85f, 1);
            }
        }

        private void Hide() { foreach (var map in maps.Values) map.Hide(); }
        internal void Clear()
        {
            stationNotes.Clear();
            foreach (var map in maps.Values) map.Dispose();
            maps.Clear();
            routePoints.Clear();
        }
        private void OnDestroy() { Clear(); }
    }
}
