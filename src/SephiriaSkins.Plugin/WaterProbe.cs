using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;

// Locate an actual loaded native water tile and adjacent solid shore. Never
// manufacture water, alter its stencil, or teleport into an unverified pit.
internal static class WaterProbe
{
    public static IEnumerator Inspect(Plugin plugin, string output, Action<object> record)
    {
        var player = Ownership.Local!;
        var candidates = new List<(Tilemap Map, Vector3Int Water, Vector3Int Shore, Vector3 Point, float Distance)>();
        var maps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        var oversized = new List<string>(); var waterTiles = 0; var physicalWaterShoreChecks = 0;
        foreach (var map in maps.Where(m => m && m.gameObject.activeInHierarchy))
        {
            var size = map.cellBounds.size;
            if ((long)size.x * size.y * size.z > 200000) { oversized.Add(RuntimeCatalog.PathOf(map.transform)); continue; }
            foreach (var cell in map.cellBounds.allPositionsWithin)
            {
                var tile = map.GetTile(cell);
                var entity = tile ? TileDatabase.FindGroundTile(tile) : null;
                // Hand-authored town lakes can use a PitWater collider rather
                // than a database Water tile. Check the game's own pit layer
                // below a known solid ground tile, without changing geometry.
                if (entity && entity.type == GroundTileEntity.Type.Ground)
                {
                    var shorePoint = map.GetCellCenterWorld(cell);
                    var waterPoint = shorePoint - Vector3.up;
                    var nativePit = Physics2D.OverlapPoint(waterPoint, CombatManager.PitLayerMask);
                    if (nativePit && nativePit.GetComponent<PitWater>() && !Physics2D.OverlapCircle(shorePoint, .3f, CombatManager.PitLayerMask))
                    {
                        physicalWaterShoreChecks++;
                        candidates.Add((map, cell + Vector3Int.down, cell, shorePoint, Vector3.Distance(player.transform.position, shorePoint)));
                    }
                }
                if (!entity || entity.type != GroundTileEntity.Type.Water) continue;
                waterTiles++;
                // Reflection is drawn below the feet, so prefer solid ground
                // above this water rather than changing the native renderer.
                foreach (var offset in new[] { Vector3Int.up, Vector3Int.right, Vector3Int.left, Vector3Int.down })
                {
                    var shore = cell + offset;
                    var shoreTile = map.GetTile(shore);
                    var shoreEntity = shoreTile ? TileDatabase.FindGroundTile(shoreTile) : null;
                    if (!shoreEntity || shoreEntity.type != GroundTileEntity.Type.Ground) continue;
                    var point = map.GetCellCenterWorld(shore);
                    var waterPoint = map.GetCellCenterWorld(cell);
                    if (point.y <= waterPoint.y) continue;
                    candidates.Add((map, cell, shore, point, Vector3.Distance(player.transform.position, point)));
                }
            }
        }
        record(new { waterSurvey = true, waterTiles, physicalWaterShoreChecks, shores = candidates.Count, oversizedMaps = oversized.ToArray(),
            available = candidates.Count > 0, reason = candidates.Count > 0 ? "loaded native water tile or PitWater collider with verified solid shore" : "no loaded native water with a verified solid shore was located by this survey; reflection renderer checks remain separate" });
        if (candidates.Count == 0) yield break;
        var selected = candidates.OrderBy(c => c.Distance).First();
        var start = player.transform.position;
        var priorPixel = plugin.PixelArt;
        player.CancelCurrentAction(); player.DespawnAllBullet();
        try
        {
            player.ReqSetPosition(selected.Point, true);
            yield return new WaitForSecondsRealtime(1);
            foreach (var pixel in new[] { true, false })
            foreach (var direction in new[] { Vector2.up, Vector2.down })
            {
                plugin.SetPixelArt(pixel);
                InputSystem.QueueDeltaStateEvent(Gamepad.current.rightStick, direction);
                player.ForceAimToPosition((Vector2)player.transform.position + direction * 3);
                yield return new WaitForSecondsRealtime(.35f);
                var nativeTheme = plugin.SuspendDiagnosticTheme();
                var nativeRenderers = VisualProbe.Snapshot();
                RuntimeProbe.Capture(Path.Combine(output, $"water-native-{pixel}-{direction.y}.png"), Debug.Log);
                plugin.ResumeDiagnosticTheme(nativeTheme);
                yield return null;
                var themedRenderers = VisualProbe.Snapshot();
                RuntimeProbe.Capture(Path.Combine(output, $"water-theme-{pixel}-{direction.y}.png"), Debug.Log);
                record(new { waterCapture = true, pixel, facing = direction == Vector2.up ? "back" : "front",
                    map = RuntimeCatalog.PathOf(selected.Map.transform), waterCell = selected.Water.ToString(), shoreCell = selected.Shore.ToString(),
                    waterPoint = selected.Map.GetCellCenterWorld(selected.Water).ToString(), requestedPosition = selected.Point.ToString(), actualPosition = player.transform.position.ToString(),
                    alive = !player.IsDead, player.CanMove, nativeRenderers, themedRenderers,
                    scope = "native water and solid shore camera capture; pixel visibility is reviewed in the saved PNG" });
            }
        }
        finally
        {
            plugin.SetPixelArt(priorPixel);
            InputSystem.QueueDeltaStateEvent(Gamepad.current.rightStick, Vector2.right);
            player.CancelCurrentAction(); player.ReqSetPosition(start, true);
        }
    }
}
