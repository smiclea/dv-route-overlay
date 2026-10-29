using System;
using UnityEngine;
using UnityModManagerNet;

namespace DVRouteOverlay
{
    public sealed class Settings : UnityModManager.ModSettings
    {
        public bool ShowOverlay = true;
        public float DistanceKm = 15;
        public float WidthMm = 0.8f;
        public KeyBinding RefreshKey = new KeyBinding { keyCode = KeyCode.F8 };
        public int Color = 0;
        public override void Save(UnityModManager.ModEntry entry) { Save(this, entry); }

        internal void Validate()
        {
            DistanceKm = float.IsNaN(DistanceKm) || float.IsInfinity(DistanceKm) ? 15 : Mathf.Clamp(DistanceKm, 1, 60);
            WidthMm = float.IsNaN(WidthMm) || float.IsInfinity(WidthMm) ? 0.8f : Mathf.Clamp(WidthMm, 0.3f, 2);
            if (RefreshKey == null) RefreshKey = new KeyBinding { keyCode = KeyCode.F8 };
            Color = Mathf.Clamp(Color, 0, 2);
        }
    }

    public static class Main
    {
        internal static Settings Settings;
        internal static UnityModManager.ModEntry Entry;
        private static OverlayController controller;
        private static bool enabled;
        private static readonly string[] Colors = { "Cyan", "Green", "Orange" };

        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry;
            Settings = UnityModManager.ModSettings.Load<Settings>(entry);
            Settings.Validate();
            entry.OnToggle = Toggle;
            entry.OnGUI = DrawSettings;
            entry.OnSaveGUI = e => Settings.Save(e);
            entry.OnUnload = e => { Stop(); return true; };
            entry.Logger.Log("Loaded. Manual route refresh only; no periodic route calculations, switch or driving control.");
            return true;
        }

        private static bool Toggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            if (value && !controller)
            {
                var host = new GameObject("DVRouteOverlay");
                UnityEngine.Object.DontDestroyOnLoad(host);
                controller = host.AddComponent<OverlayController>();
            }
            else if (!value) Stop();
            return true;
        }

        private static void Stop()
        {
            enabled = false;
            if (controller)
            {
                controller.Clear();
                UnityEngine.Object.Destroy(controller.gameObject);
            }
            controller = null;
        }

        private static void DrawSettings(UnityModManager.ModEntry entry)
        {
            Settings.ShowOverlay = GUILayout.Toggle(Settings.ShowOverlay, "Show route on world map");
            GUILayout.Label("Follows your current / last-used locomotive through the currently selected switches.");
            GUILayout.Label("Direction comes only from the reverser. Select forward or reverse before refreshing; neutral has no route direction.");
            GUILayout.Label("Refresh manually after moving, reversing, or changing a switch. The line stays as a snapshot.");
            UnityModManager.UI.DrawKeybindingSmart(Settings.RefreshKey, "Refresh route key");
            if (enabled && controller && GUILayout.Button("Refresh route now")) controller.RequestRefresh();
            GUILayout.Label("Look ahead: " + Settings.DistanceKm.ToString("0") + " km");
            Settings.DistanceKm = Mathf.Round(GUILayout.HorizontalSlider(Settings.DistanceKm, 1, 60));
            GUILayout.Label("Line width: " + Settings.WidthMm.ToString("0.0") + " mm on the map");
            Settings.WidthMm = GUILayout.HorizontalSlider(Settings.WidthMm, 0.3f, 2);
            Settings.Color = GUILayout.SelectionGrid(Settings.Color, Colors, 3);
            GUILayout.Label(enabled && controller ? controller.Status : "Mod disabled");
            GUILayout.Label("The line stops at disconnected track, a loop, or the look-ahead limit.");
            GUILayout.Label("Reverser, look-ahead, width and color changes apply when you next refresh the route.");
        }
    }
}
