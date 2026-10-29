using System;
using System.Collections.Generic;
using System.Globalization;
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
            private string lastDiagnostic;

            internal ManualNote(PageBook book, string[] labels, List<string> stationIds)
            {
                this.book = book; this.labels = labels; this.stationIds = stationIds;
                book.PageFlipped += OnPageFlipped;
                book.PageBookGenerated += OnGenerated;
            }

            internal void SetSnapshot(StationSnapshot value)
            { snapshot = value; pageText.Clear(); RefreshText(); }
            internal void SetVisible(bool value) { if (visible == value) return; visible = value; RefreshText(); }
            private void OnPageFlipped(int page)
            {
                Main.Entry.Logger.Log("Station manual page flip: book=" + (book ? book.name : "destroyed") + ", page=" + page + ".");
                RefreshText();
            }
            private void OnGenerated() { RefreshText(); }

            private void RefreshText()
            {
                try
                {
                    if (!book || !visible || snapshot == null || !book.PagesGenerated)
                    { if (host) host.SetActive(false); LogState("waiting/hidden", null); return; }
                    if (book.PageNum != labels.Length)
                    {
                        if (host) host.SetActive(false);
                        if (!mismatchLogged) Main.Entry.Logger.Error("Station manual page count differs from its world definitions; notes hidden to avoid labelling the wrong station.");
                        mismatchLogged = true;
                        LogState("page count mismatch", null);
                        return;
                    }
                    int page = book.currentPage;
                    if (page < 0 || page >= labels.Length) { if (host) host.SetActive(false); LogState("invalid page index", null); return; }
                    string stationId = StationSnapshot.MatchPage(labels[page], stationIds);
                    if (stationId == null) { if (host) host.SetActive(false); LogState("page has no station match", null); return; } // Cover and legend stay clear.
                    if (!host) CreateFooter();
                    PositionFooter();
                    string content;
                    if (!pageText.TryGetValue(page, out content))
                    {
                        content = snapshot.FooterFor(stationId);
                        pageText.Add(page, content);
                    }
                    text.text = content;
                    FitFooterText();
                    host.SetActive(true);
                    LogState("shown", stationId);
                }
                catch (Exception ex)
                {
                    if (host) host.SetActive(false);
                    visible = false;
                    Main.Entry.Logger.Error("Station manual note disabled after a UI error: " + ex);
                }
            }

            private void CreateFooter()
            {
                host = new GameObject("DVRouteOverlay Station Footer", typeof(RectTransform), typeof(Canvas));
                var canvas = host.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 10;
                host.transform.SetParent(book.transform, false);
                // The schematic's printed text runs along book -Z, with its top toward +X.
                // Match that UV orientation instead of the book transform's +X direction.
                host.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.right);
                host.transform.localScale = Vector3.one * 0.00025f;
                var rect = (RectTransform)host.transform;
                rect.pivot = new Vector2(1, 0);
                var label = new GameObject("Route Footer Text", typeof(RectTransform));
                label.transform.SetParent(host.transform, false);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
                text = label.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 28;
                text.resizeTextForBestFit = false;
                text.color = new Color(0.08f, 0.12f, 0.14f, 1);
                text.alignment = TextAnchor.MiddleRight;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.supportRichText = false;
                text.raycastTarget = false;
            }

            private void PositionFooter()
            {
                if (book.pages.Count == 0) throw new InvalidOperationException("The station manual has no physical pages.");
                var page = book.pages[Mathf.Clamp(book.currentPage, 0, book.pages.Count - 1)];
                if (!page || !page.renderer) throw new InvalidOperationException("The current station page has no renderer.");
                // Page mesh coordinates and pivots are not the physical page edges. Transform all
                // eight local-bounds corners into book space, including the model's own rotation/scale.
                var bounds = page.renderer.localBounds;
                var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = book.transform.InverseTransformPoint(page.renderer.transform.TransformPoint(corner));
                    min = Vector3.Min(min, point); max = Vector3.Max(max, point);
                }
                // Printed-page right is book -Z, and printed-page up is book +X.
                // Position above the bookmark strip in the page's bottom-right margin.
                // Reset to book space before sizing, then attach to the current leaf so
                // the footer follows the book's animated pivot and page-stack offsets.
                host.transform.SetParent(book.transform, false);
                host.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.right);
                host.transform.localScale = Vector3.one * 0.00025f;
                var rect = (RectTransform)host.transform;
                float inset = 0.007f;
                float lineHeight = 0.014f;
                rect.sizeDelta = new Vector2(Mathf.Max(0.05f, max.z - min.z - 2 * inset) / 0.00025f,
                    lineHeight / 0.00025f);
                host.transform.localPosition = new Vector3(min.x + (max.x - min.x) * 0.10f,
                    max.y + 0.002f, min.z + inset);
                host.transform.SetParent(page.transform, true);
                host.layer = page.renderer.gameObject.layer;
                text.gameObject.layer = host.layer;
            }

            private void FitFooterText()
            {
                text.fontSize = 28;
                float available = ((RectTransform)host.transform).rect.width;
                float preferred = text.preferredWidth;
                if (preferred > available)
                    text.fontSize = Mathf.Max(12, Mathf.FloorToInt(28 * available / preferred));
            }

            private void LogState(string reason, string stationId)
            {
                string label = book && book.currentPage >= 0 && book.currentPage < labels.Length ? labels[book.currentPage] : "none";
                string diagnostic = string.Format(CultureInfo.InvariantCulture,
                    "Station note: state={0}, snapshot={1}, book={2}, active={3}, generated={4}, pages={5}/{6}, page={7}, label={8}, station={9}, enabled={10}, host={11}, font={12}",
                    reason, snapshot != null ? snapshot.Revision : 0, book ? book.name : "none",
                    book && book.gameObject.activeInHierarchy, book && book.PagesGenerated,
                    book && book.pageTextures != null ? book.pageTextures.Length : 0, labels.Length,
                    book ? book.currentPage : -1, label, stationId ?? "none", visible,
                    host && host.activeInHierarchy, text && text.font ? text.font.name : "none");
                if (diagnostic == lastDiagnostic) return;
                lastDiagnostic = diagnostic;
                Main.Entry.Logger.Log(diagnostic + ".");
                if (!host || reason != "shown") return;
                var camera = PlayerManager.ActiveCamera;
                var rect = (RectTransform)host.transform;
                var center = rect.TransformPoint(rect.rect.center);
                var viewport = camera ? camera.WorldToViewportPoint(center) : Vector3.zero;
                Main.Entry.Logger.Log(string.Format(CultureInfo.InvariantCulture,
                    "Station note layout: local={0}, world={1}, scale={2}, layer={3}, camera={4}, layerInCamera={5}, centerViewport={6}, facingCamera={7}.",
                    host.transform.localPosition.ToString("F4"), center.ToString("F4"), host.transform.lossyScale.ToString("F5"), host.layer,
                    camera ? camera.name : "none", camera && (camera.cullingMask & (1 << host.layer)) != 0,
                    viewport.ToString("F4"), camera && Vector3.Dot(-host.transform.forward, camera.transform.position - center) > 0));
            }

            public void Dispose()
            {
                if (book) { book.PageFlipped -= OnPageFlipped; book.PageBookGenerated -= OnGenerated; }
                if (host) { host.SetActive(false); UnityEngine.Object.Destroy(host); }
            }
        }
    }
}
