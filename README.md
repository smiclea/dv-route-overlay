# Route Overlay for Derail Valley

A small Unity Mod Manager mod that highlights the track ahead on the game's world map. It reads the switches as they are currently set and follows that single connected route. It never selects a destination, searches alternate paths, changes switches, rotates turntables, or controls the train.

## Use

1. Start or restart Derail Valley with Unity Mod Manager installed.
2. Enter a locomotive, select forward or reverse on its reverser, and open the handheld world map. Press **F8** to draw a cyan route snapshot.
3. After moving, reversing, or changing a switch, press **F8** again when you want an updated route. Holding the key does not repeat the refresh.
4. Step out to throw a switch: the last-used locomotive remains the route's origin. Refresh with the map open afterward.

There is no automatic route calculation at startup or on a timer. The line stays as the last snapshot, including its starting point, until you refresh. Changing locomotives or unloading the current one clears that snapshot. Newly created maps are discovered only when you refresh; open the world map and refresh again if the line is missing.

The route starts at the locomotive's leading bogie, according to the selected direction. It ends at disconnected track, a repeated track in the same direction, or the look-ahead limit. It can also stop if track geometry is unavailable. Turntables are followed only where the game exposes a connected track; they are never moved by this mod. A trailing approach uses the game's native connection rule, which leads back to the junction's common track regardless of its selected outgoing branch. The line is a route preview, not a signal or clearance indicator.

## Settings

Press **Ctrl+F10** during play to reopen Unity Mod Manager, then open **Route Overlay** and its settings panel. Use **Refresh route key** to change the F8 binding (modifier keys are supported), or click **Refresh route now**. The hotkey is ignored while the UMM window is open so editing a binding does not also refresh the route. On Steam Deck, map F8 to a spare button through Steam Input. Settings are saved through UMM to this mod's `Settings.xml`.

| Setting | Default | Behavior |
| --- | --- | --- |
| Show route on world map | On | Show/hide the overlay |
| Refresh route key | F8 | Manually calculate and draw the route once per press; configurable in UMM |
| Direction source | Reverser only | Forward/reverse come from the actual reverser control at refresh time; neutral has no direction |
| Look ahead | 15 km | Adjustable from 1–60 km |
| Line width | 0.8 mm | Adjustable from 0.3–2 mm in map-local units |
| Color | Cyan | Cyan, green, or orange |

The visibility toggle works immediately. Reverser, look-ahead, width and color changes apply when you next press the hotkey or click **Refresh route now**. Old saved interval and direction-override settings are ignored.

The only source of route direction is `SimController.controlsOverrider.Reverser.Value`, read once per manual refresh. The game's common control uses `0` for reverse, `0.5` for neutral, and `1` for forward; analog steam cutoff positions on either side of neutral select the corresponding direction. Neutral clears the line and displays a message asking you to select forward or reverse. A missing or invalid reverser never falls back to vehicle motion, facing, a remembered direction, or a manual override. There is no speed/direction observation running between refreshes. The game's native bogie track-orientation sign maps the reverser direction into the rail segment's coordinate order.

Track traversal, track sampling, map discovery and mesh rebuilding happen only after a manual refresh request. Every refresh walks a single connected route and samples it about every 15 metres (roughly 1,000 samples at 15 km), then rebuilds the line for active maps. Point intervals are found with binary search instead of rescanning the track's points for every sample. Sample spans are clamped to valid geometry bounds, including exact endpoint handling to prevent floating-point overshoots. A refresh can still cause a brief hitch when requested, but no recurring route calculations run in the background. Shorten the look-ahead distance to reduce work per refresh.

## Build and install

Requires a .NET 8 SDK, the installed game assemblies, and Unity Mod Manager. No NuGet libraries or other mods are required. The DLL targets .NET Framework 4.7.2 for the game's Unity/Mono runtime. Proprietary game DLLs are referenced locally and are not bundled.

```bash
bash scripts/build.sh
bash scripts/install.sh
```

Pass a different game directory as the first argument to either script, or set `DV_GAME_PATH`. Set `DOTNET` to a .NET SDK executable if it is not on PATH. The build script also recognizes the temporary SDK at `/tmp/dv-dotnet/dotnet` used during development; that temporary SDK may disappear after reboot. Invariant globalization is enabled for the build tools so they work on this machine without ICU.

The install script copies only this mod's files into `Mods/DVRouteOverlay`, preserving settings on updates. Existing versions are backed up under the project's `dist/backups/`. Restart the game after installation or updates. To uninstall, close the game and remove `Mods/DVRouteOverlay`.

## Validation

The project is compiled against the locally installed game and Unity Mod Manager assemblies. The standalone checks cover route traversal, floating-point endpoint regressions, bounded forward/reverse sampling, point-interval lookup, and forward/reverse/neutral/analog/invalid reverser values.

The initial version was confirmed working in-game by the user. Version 0.1.1 caused recurring stutters, so version 0.2.0 removed periodic refreshes. Logs from version 0.2.0 confirmed `ArgumentOutOfRangeException` in point-set sampling, which caused the route to be cleared after an F8 press. Version 0.2.1 fixes that sampling boundary, uses only the reverser for direction, and retains an existing snapshot if later sampling fails. These changes still need an in-game check: keep the locomotive stationary, refresh with the reverser forward and reverse, confirm the route flips, then try neutral. Open the map, change a switch, and confirm the snapshot stays unchanged until F8 is pressed again. Check alignment after an origin shift and after loading another save.

Each manual refresh writes a numbered diagnostic to `DerailValley_Data/Managed/UnityModManager/Log.txt`, prefixed with `DVRouteOverlay`. It records the locomotive ID, raw reverser value and control port, start track/span, bogie-to-track orientation, distance, track/sample counts, map activity and vertex counts, stop reason, and timing for traversal, sampling, map discovery and drawing. Failures include the stage, exact track/sample bounds, and full exception. No per-frame or per-sample log spam is emitted. If a line still fails to appear, reproduce it once and share this log before launching another game session, which may replace it.

## Implementation

No Harmony patches and no CommsRadioAPI dependency. The mod uses native track branch getters, samples the game's absolute-coordinate point sets, and draws its own mesh under each loaded map marker controller. The game's private `GetMapPosition` method is bound once per map for projection; a future game update changing that method may require a mod update. Disabling or unloading the mod removes its meshes and materials.

Inspired by [WallyCZ/DVRouteManager](https://github.com/WallyCZ/DVRouteManager), particularly its use of the world-map marker projection. This is an independent, display-only implementation; the reference mod's pathfinder and control systems are not included.
