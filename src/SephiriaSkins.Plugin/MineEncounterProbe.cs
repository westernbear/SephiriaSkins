using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace SephiriaSkins.Plugin;

// Opt-in fixture only: deploy with native player input and let the native
// proximity module detonate. Never call DestroySelf or manufacture damage.
internal static class MineEncounterProbe
{
    private static Vector3? FindDeployment(PlayerAvatar player)
    {
        var enemies = CombatManager.Instance.AllCreatures.Where(u => u && u != player && !u.IsDead &&
            u.faction != "Dummy" && u.gameObject.activeInHierarchy && !u.canBeTarget.IsFalse() &&
            CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), u.faction)).ToArray();
        var safe = new List<Vector3>();
        foreach (var map in UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None).Where(m => m && m.gameObject.activeInHierarchy))
        {
            if ((long)map.cellBounds.size.x * map.cellBounds.size.y > 200000) continue;
            bool Ground(Vector3 position)
            {
                var tile = map.GetTile(map.WorldToCell(position));
                return tile && TileDatabase.FindGroundTile(tile)?.type == GroundTileEntity.Type.Ground &&
                    !Physics2D.OverlapCircle(position, .6f, CombatManager.PathfindingObstacleLayerMask | CombatManager.PitLayerMask);
            }
            foreach (var cell in map.cellBounds.allPositionsWithin)
            {
                var point = map.GetCellCenterWorld(cell);
                if (Vector3.Distance(point, player.transform.position) > 20 || !Ground(point) ||
                    enemies.Any(u => Vector2.Distance(point, u.transform.position) < 8)) continue;
                // Native aim assistance can redirect the mine towards another
                // enemy even after a leftward input. The observed v3 trajectory
                // travelled right. Verify ground in every possible direction,
                // rather than treating the requested aim as the actual motion.
                if (Enumerable.Range(-12, 25).Any(x => Enumerable.Range(-12, 25).Any(y =>
                {
                    if (x*x + y*y > 144) return false;
                    return !Ground(point + new Vector3(x*.5f, y*.5f, 0));
                }))) continue;
                safe.Add(point);
            }
        }
        return safe.OrderBy(p => Vector3.Distance(p, player.transform.position)).Select(p => (Vector3?)p).FirstOrDefault();
    }
    private static bool Mine(NewWeaponFireData? data) => data is NewWeaponFireData_Bullet bullet && bullet.bulletPrefab &&
        bullet.bulletPrefab.GetComponentInChildren<BulletMoveModule_CrossbowMine>(true);

    public static IEnumerator Inspect(Plugin plugin, Action<string, object> record)
    {
        var player = Ownership.Local!;
        var weapon = DiagnosticWeapons.All().FirstOrDefault(w => DiagnosticWeapons.SkipReason(w) == null &&
            w.mainWeaponPrefab.GetComponent<WeaponSimple>().basicComboAttacks.Any(Mine));
        if (!weapon)
        {
            record("live mine encounter", new { available = false, reason = "No active native basic-attack proximity mine prefab found." });
            yield break;
        }
        var controller = player.GetComponent<WeaponControllerSimple>();
        var priorWeapon = controller.currentWeapon.entityId;
        var priorMaxMp = player.maxMp;
        var priorGameOver = player.dieIsGameOver;
        var priorPosition = player.transform.position;
        var target = CombatManager.Instance.AllCreatures.FirstOrDefault(u => u && u != player && !u.IsDead && u.faction != "Dummy" &&
            u.gameObject.activeSelf && !u.canBeTarget.IsFalse() && u.TopdownActor.YPos <= 5 &&
            Vector3.Distance(u.transform.position, player.transform.position) < 30 &&
            CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), u.faction));
        if (!target)
        {
            record("live mine encounter", new { available = false, weapon = weapon.id, reason = "No eligible living native room enemy remains." });
            yield break;
        }
        var deployment = FindDeployment(player);
        if (!deployment.HasValue)
        {
            record("live mine encounter", new { available = false, weapon = weapon.id,
                reason = "No nearby native ground corridor with clear pit/obstacle checks and enemy separation; deployment was not attempted." });
            yield break;
        }
        var targetPosition = target.transform.position;
        Bullet? deployed = null;
        TopdownRigidbody? mineRigid = null;
        var groundedObserved = false;
        var trajectory = new List<object>();
        var fires = 0; var hits = 0; var rawDamage = new List<float>();
        void Grounded() { groundedObserved = true; }
        void Fire(ProjectileBase projectile)
        {
            if (projectile is Bullet bullet && bullet.GetComponentInChildren<BulletMoveModule_CrossbowMine>(true))
            {
                deployed = bullet; fires++;
                mineRigid = bullet.GetComponent<TopdownRigidbody>();
                if (mineRigid) mineRigid!.OnGroundedAuthority += Grounded;
            }
        }
        void Hit(UnitAvatar victim, DamageInstance damage)
        {
            if (victim == target && damage.damageResult > 0) { hits++; rawDamage.Add(damage.damage); }
        }
        PlayProbe.NativeFireObserved += Fire;
        player.OnAttackUnit += Hit;
        try
        {
            player.CancelCurrentAction(); player.DespawnAllBullet();
            player.dieIsGameOver = NestedBoolean.False;
            controller.EquipWeapon(false, weapon.id);
            player.NetworkmaxMp = Math.Max(priorMaxMp, 1000); player.Networkmp = player.MaxMp;
            yield return new WaitForSecondsRealtime(.5f);
            var actualMine = Mine(controller.currentWeapon.GetBasicAttack(0, ""));
            if (!actualMine)
            {
                record("live mine encounter", new { available = false, weapon = weapon.id,
                    reason = "Native equipped basic attack selects a different conditional fire variant." });
                yield break;
            }
            // Fire away from this actual AI enemy, then move it only after
            // the mine has landed. Both moves use existing network APIs.
            player.ReqSetPosition(deployment.Value, true);
            yield return new WaitForSecondsRealtime(.2f);
            player.ForceAimToPosition((Vector2)player.transform.position - Vector2.right * 5);
            player.AttackButtonDown(Vector2.left);
            var deadline = Time.realtimeSinceStartup + 3;
            while (!deployed && Time.realtimeSinceStartup < deadline) yield return null;
            player.AttackButtonUp();
            var rigid = deployed ? deployed!.GetComponent<TopdownRigidbody>() : null;
            deadline = Time.realtimeSinceStartup + 5;
            var nextSample = 0f;
            while (deployed && deployed!.gameObject.activeInHierarchy && rigid && !rigid!.IsGrounded && Time.realtimeSinceStartup < deadline)
            {
                if (Time.realtimeSinceStartup >= nextSample)
                {
                    trajectory.Add(new { position = deployed.transform.position.ToString(), rigid.YPosition, rigid.IsGrounded,
                        rigid.IsPitFalling, rigid.GravityVelocity });
                    nextSample = Time.realtimeSinceStartup + .1f;
                }
                yield return null;
            }
            var landed = deployed && deployed!.gameObject.activeInHierarchy && rigid && rigid!.IsGrounded;
            var moved = false;
            if (landed && target && !target!.IsDead)
            {
                target.ReqSetPosition(deployed!.transform.position, true); moved = true;
                yield return new WaitForSecondsRealtime(1.2f);
            }
            var detonated = moved && (!deployed || !deployed!.gameObject.activeInHierarchy);
            RuntimeProbe.Capture(plugin.Diagnostics.File("play-mine-encounter.png"), Debug.Log);
            record("live mine encounter", new { available = true, weapon = weapon.id, target = target ? target!.name : "destroyed native enemy",
                fires, landed = (bool)landed, targetMoved = moved, detonated, hits, rawDamage = rawDamage.ToArray(),
                groundedObserved, trajectory = trajectory.ToArray(),
                finalMinePosition = deployed ? deployed!.transform.position.ToString() : null,
                finalPitFalling = rigid ? rigid!.IsPitFalling : (bool?)null,
                verifiedGroundCorridor = deployment.Value.ToString(),
                reason = !landed ? "Native mine did not remain active and grounded within the bounded deployment window; detonation is not verified." :
                    !moved ? "Native target ended before proximity movement; detonation is not verified." :
                    !detonated || hits == 0 ? "Native proximity/damage was not observed within the bounded window." : "Native proximity detonation and actual enemy hit observed.",
                localThemed = player.TopdownActor.bodyRenderer.sprite && player.TopdownActor.bodyRenderer.sprite.name.StartsWith(plugin.Diagnostics.PackId + ":"),
                scope = "one native mine deployment and proximity detonation against an actual AI enemy; not an A/B damage comparison or homing-mine coverage" });
        }
        finally
        {
            PlayProbe.NativeFireObserved -= Fire; player.OnAttackUnit -= Hit;
            if (mineRigid) mineRigid!.OnGroundedAuthority -= Grounded;
            player.AttackButtonUp(); player.CancelCurrentAction(); player.DespawnAllBullet();
            if (target && !target!.IsDead) target.ReqSetPosition(targetPosition, true);
            if (player.IsDead) player.Revive(player.MaxHp);
            player.dieIsGameOver = priorGameOver;
            player.NetworkmaxMp = priorMaxMp; player.Networkmp = Math.Min(player.mp, player.MaxMp);
            controller.EquipWeapon(false, priorWeapon); player.ReqSetPosition(priorPosition, true);
        }
    }
}
