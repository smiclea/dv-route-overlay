using System;
using System.Collections.Generic;
using DV.Booklets.Rendered;
using DV.RenderTextureSystem.BookletRender;
using UnityEngine;
using UnityEngine.UI;

namespace DVRouteOverlay
{
    // Discovery and indexing run only after F8. Page events read the cached snapshot.
    internal sealed class StationManualNotes
    {
        private readonly Dictionary<PageBook, ManualNote> notes = new Dictionary<PageBook, ManualNote>();
        private bool visible = true;

        internal void Publish(StationSnapshot snapshot)
        {
            var prefab = LevelInfo.GetWorldSpecificPrefab(LevelInfo.WorldSpecificPrefabs.SchematicMapRender);
            var render = prefab ? prefab.GetComponent<StaticPagesRender>() : null;
            if (!render || render.staticPages == null) throw new InvalidOperationException("Station manual page definitions are unavailable.");
            var labels = new string[render.staticPages.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = render.staticPages[i] ? render.staticPages[i].name : "";
            var stationIds = new List<string>();
            foreach (var station in StationController.allStations)
                if (station && station.stationInfo != null) stationIds.Add(station.stationInfo.YardID);
            var dead = new List<PageBook>();
            foreach (var item in notes) if (!item.Key) { item.Value.Dispose(); dead.Add(item.Key); }
            foreach (var key in dead) notes.Remove(key);
            foreach (var book in Resources.FindObjectsOfTypeAll<PageBook>())
            {
                if (!book.gameObject.scene.IsValid() || !book.gameObject.scene.isLoaded || notes.ContainsKey(book)) continue;
                var source = book.GetComponent<RuntimeRenderedStaticTextureBooklet>();
                if (!source || !source.worldSpecific || source.worldSpecificPrefab != LevelInfo.WorldSpecificPrefabs.SchematicMapRender) continue;
                notes.Add(book, new ManualNote(book, labels, stationIds));
            }
            foreach (var note in notes.Values) { note.SetSnapshot(snapshot); note.SetVisible(visible); }
            Main.Entry.Logger.Log("Station notes: " + snapshot.Visits.Count + " station(s) reached; " + notes.Count + " manual(s), " + labels.Length + " page definitions.");
            foreach (var visit in snapshot.Visits)
                Main.Entry.Logger.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Station {0}: first track at {1:0.0} m; tracks={2}.", visit.Key, visit.Value.EntryDistance, string.Join(" > ", visit.Value.Tracks)));
        }

        internal void SetVisible(bool value)
        {
            visible = value;
            foreach (var note in notes.Values) note.SetVisible(value);
        }

        internal void Clear()
        {
            foreach (var note in notes.Values) note.Dispose();
            notes.Clear();
        }

        private sealed class ManualNote : IDisposable
        {
            private readonly PageBook book;
            private readonly string[] labels;
            private readonly List<string> stationIds;
            private readonly Dictionary<int, string> pageText = new Dictionary<int, string>();
            private StationSnapshot snapshot;
            private GameObject host;
            private Text text;
            private bool visible = true;
            private bool mismatchLogged;

            internal ManualNote(PageBook book, string[] labels, List<string> stationIds)
            {
                this.book = book; this.labels = labels; this.stationIds = stationIds;
                book.PageFlipped += OnPageFlipped;
                book.PageBookGenerated += OnGenerated;
            }

            internal void SetSnapshot(StationSnapshot value)
            { snapshot = value; pageText.Clear(); RefreshText(); }
            internal void SetVisible(bool value) { visible = value; RefreshText(); }
            private void OnPageFlipped(int _) { RefreshText(); }
            private void OnGenerated() { RefreshText(); }

            private void RefreshText()
            {
                try
                {
                    if (!book || !visible || snapshot == null || !book.PagesGenerated)
                    { if (host) host.SetActive(false); return; }
                    if (book.PageNum != labels.Length)
                    {
                        if (host) host.SetActive(false);
                        if (!mismatchLogged) Main.Entry.Logger.Error("Station manual page count differs from its world definitions; notes hidden to avoid labelling the wrong station.");
                        mismatchLogged = true;
                        return;
                    }
                    int page = book.currentPage;
                    if (page < 0 || page >= labels.Length) { if (host) host.SetActive(false); return; }
                    string stationId = StationSnapshot.MatchPage(labels[page], stationIds);
                    if (stationId == null) { if (host) host.SetActive(false); return; } // Cover and legend stay clear.
                    if (!host) CreateSlip();
                    string content;
                    if (!pageText.TryGetValue(page, out content))
                    {
                        content = snapshot.NoteFor(stationId, StationSnapshot.NormalizePage(labels[page]));
                        pageText.Add(page, content);
                    }
                    text.text = content;
                    host.SetActive(true);
                }
                catch (Exception ex)
                {
                    if (host) host.SetActive(false);
                    visible = false;
                    Main.Entry.Logger.Error("Station manual note disabled after a UI error: " + ex);
                }
            }

            private void CreateSlip()
            {
                // The manual's A3 page template is scaled to approximately 0.232 x 0.300 m.
                // Attach a separate slip beside its right edge, keeping the diagram unobscured.
                var page = book.pages.Count > 0 ? book.pages[0] : null;
                Vector3 scale = page ? page.transform.localScale : new Vector3(0.78f, 0.8f, 0.715f);
                float width = 0.297f * Mathf.Abs(scale.x);
                float height = 0.420f * Mathf.Abs(scale.z);
                int layer = page && page.renderer ? page.renderer.gameObject.layer : book.gameObject.layer;
                host = new GameObject("DVRouteOverlay Station Note", typeof(RectTransform), typeof(Canvas));
                host.layer = layer;
                host.transform.SetParent(book.transform, false);
                host.transform.localPosition = new Vector3(width + 0.008f, 0.008f, height * 0.88f);
                host.transform.localRotation = Quaternion.Euler(90, 0, 0);
                host.transform.localScale = Vector3.one * 0.00025f;
                var rect = (RectTransform)host.transform;
                rect.pivot = new Vector2(0, 1);
                rect.sizeDelta = new Vector2(800, 600);
                var canvas = host.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 10;
                var background = host.AddComponent<Image>();
                background.color = new Color(0.97f, 0.95f, 0.86f, 1);
                background.raycastTarget = false;
                var label = new GameObject("Route Note Text", typeof(RectTransform));
                label.layer = layer;
                label.transform.SetParent(host.transform, false);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(28, 24); labelRect.offsetMax = new Vector2(-28, -24);
                text = label.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 30;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 22; text.resizeTextMaxSize = 30;
                text.color = new Color(0.08f, 0.12f, 0.14f, 1);
                text.alignment = TextAnchor.UpperLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.supportRichText = false;
                text.raycastTarget = false;
            }

            public void Dispose()
            {
                if (book) { book.PageFlipped -= OnPageFlipped; book.PageBookGenerated -= OnGenerated; }
                if (host) { host.SetActive(false); UnityEngine.Object.Destroy(host); }
            }
        }
    }
}
