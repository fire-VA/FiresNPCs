using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    // 0.0.498 (Fire 10-05): two territory flags that change the body.
    //
    // Gravity=<g>, g in -10..10. 1 = vanilla, 0 = weightless, 0.5 = half, 3 = three times, negative = pulls UP.
    //   Vanilla gravity is the Rigidbody's own (useGravity + Physics.gravity); vanilla switches it OFF to swim, climb, fly or sit,
    //   and Core's swim / climb / crawl modules do the same. So territory gravity is one extra acceleration
    //   (Physics.gravity * (g - 1)) added in the owner's fixed step ONLY while vanilla's gravity is on: swimming and climbing
    //   keep their own logic, untouched. Players only (the local player, on its owner): creatures' AI and pathing assume
    //   vanilla gravity, so they keep it.
    //   Falls: below vanilla gravity (g < 1) there is no fall damage (a weightless drift must never kill on landing). At g >= 1
    //   vanilla's height-based fall damage applies as usual.
    //   Ceiling: at g <= 0 nothing pulls you back down, so above HoverCeiling metres over the ground the upward speed is
    //   stopped and the pull is cancelled: you hover there and can steer out of the territory.
    //   Leaving the territory, deleting the flag or changing it applies the next physics step.
    //
    // Scale=<s>, s in 0.25..4 (a quarter to four times the normal size; small enough to fit under furniture, big enough to see
    //   over a longhouse, before the camera and doorways stop making sense). The whole body scales: the visual and the colliders
    //   (the transform), so the camera's eye point follows. Deliberately NOT scaled: walk/run speed, jump force, reach, carry
    //   weight, the camera's distance, and mounts / chairs / beds (the scale doesn't change while attached).
    //   Networked: the owner decides its size and writes it to its ZDO ("fires_terr_scale"); every peer, the owner included,
    //   eases the transform toward that value, so everyone sees the same size.
    //   Growing (leaving a shrink territory, or a bigger value) only happens when there is room: a capsule overlap test at the
    //   target size every 0.25 s; while blocked the player stays at the current size (one Debug line names the blocker).
    [HarmonyPatch]
    internal static partial class TerritoryRuntime
    {
        public const float GravityMin = -10f, GravityMax = 10f, ScaleMin = 0.25f, ScaleMax = 4f;
        private const float HoverCeiling = 25f;
        private static readonly int s_scaleKey = "fires_terr_scale".GetStableHashCode();

        private static float _gravityNow = 1f;
        private static string _gravityTerritory = "";

        // 0.0.503 (lead, 10-06 frame-hog report): the local player's territory, looked up again only after moving more than
        // LookupMove metres or after LookupSeconds (so a flag edited live still lands within a quarter second). The gravity
        // step asked every physics step (10 per frame at low fps) and the scale step every frame.
        private const float LookupMove = 1f, LookupSeconds = 0.25f;
        private static Vector3 _lookupPos = new Vector3(float.MaxValue, 0f, 0f);
        private static float _lookupAt = -1f;
        private static Territory _lookupTerr;

        private static Territory LocalTerritory(Vector3 pos)
        {
            float now = Time.time;
            if (now - _lookupAt < LookupSeconds && (pos - _lookupPos).sqrMagnitude < LookupMove * LookupMove) return _lookupTerr;
            _lookupTerr = GetTerritoryInternal(pos);
            _lookupPos = pos; _lookupAt = now;
            return _lookupTerr;
        }

        internal static float GravityOf(Territory t) =>
            t != null && t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.Gravity) ? Mathf.Clamp(t.Gravity, GravityMin, GravityMax) : 1f;
        internal static float ScaleOf(Territory t) =>
            t != null && t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.Scale) ? Mathf.Clamp(t.Scale, ScaleMin, ScaleMax) : 1f;

        // ── gravity: the owner's own player, after vanilla's motion step ─────────────────────────────────────────────────
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        [HarmonyPostfix]
        private static void Character_CustomFixedUpdate_Gravity(Character __instance, float dt)
        {
            try
            {
                if (__instance == null || !__instance.IsPlayer() || __instance != Player.m_localPlayer) return;
                var nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
                var terr = LocalTerritory(__instance.transform.position);
                float g = GravityOf(terr);
                if (!Mathf.Approximately(g, _gravityNow))
                {
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{(terr != null ? terr.Name : _gravityTerritory)}' gravity {g:0.##} (was {_gravityNow:0.##})");
                    _gravityNow = g; _gravityTerritory = terr?.Name ?? "";
                }
                var body = __instance.m_body;
                TrackJump(__instance, body, g);   // 0.0.500: the measured proof, also at g = 1 for the baseline
                if (Mathf.Approximately(g, 1f)) return;
                if (body == null || !body.useGravity || body.isKinematic) return;   // swimming, climbing, flying, sitting: their own logic
                if (__instance.IsAttached() || __instance.IsDebugFlying() || __instance.InLiquidSwimDepth()) return;

                if (g < 1f) __instance.m_maxAirAltitude = __instance.transform.position.y;   // no fall damage below vanilla gravity

                Vector3 extra = Physics.gravity * (g - 1f);
                if (g <= 0f)
                {
                    Vector3 p = __instance.transform.position;
                    float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : p.y;
                    if (p.y - ground >= HoverCeiling)
                    {
                        var v = body.linearVelocity;
                        if (v.y > 0f) body.linearVelocity = new Vector3(v.x, 0f, v.z);
                        extra = -Physics.gravity;   // cancel vanilla's pull too: no net vertical force, a hover
                    }
                }
                // 0.0.500 (Fire: "we're kinda pinned to the ground and just fall slower"): the difference from vanilla gravity is
                // applied straight to the velocity each owner step (dt * extra), the same physics as an acceleration force but
                // in the body's velocity at once, so nothing that rebuilds the step's velocity can drop it on the way up.
                body.linearVelocity += extra * dt;
            }
            catch (Exception e) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] gravity: " + e.Message); }
        }

        // ── 0.0.500: every jump of the local player, measured: takeoff speed, apex above takeoff, airtime ──────────────────
        // One Debug line on landing: "[Territory] jump at gravity g: apex A m (expected E m = v0^2 / (2 * |gravity| * g); vanilla
        // V m), airtime T s". At g = 0.5 the apex should be twice the g = 1 one for the same takeoff speed.
        private static bool _airborne, _wasGrounded = true;
        private static float _takeoffY, _peakY, _takeoffV, _takeoffTime, _jumpG;

        private static void TrackJump(Character me, Rigidbody body, float g)
        {
            if (body == null) return;
            bool grounded = me.IsOnGround();
            float y = me.transform.position.y, vy = body.linearVelocity.y;
            if (!_airborne)
            {
                if (_wasGrounded && !grounded && vy > 2f && !me.InLiquidSwimDepth() && !me.IsAttached())
                {
                    _airborne = true; _takeoffY = y; _peakY = y; _takeoffV = vy; _takeoffTime = Time.time; _jumpG = g;
                }
            }
            else
            {
                if (y > _peakY) _peakY = y;
                if (grounded || me.InLiquidSwimDepth() || me.IsAttached() || Time.time - _takeoffTime > 30f)
                {
                    _airborne = false;
                    float grav = Mathf.Abs(Physics.gravity.y);
                    float vanilla = _takeoffV * _takeoffV / (2f * grav);
                    float expected = _jumpG > 0.01f ? vanilla / _jumpG : float.PositiveInfinity;
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] jump at gravity {_jumpG:0.##}: takeoff {_takeoffV:0.0} m/s, apex {_peakY - _takeoffY:0.00} m "
                        + $"(expected {(float.IsInfinity(expected) ? "the hover ceiling" : expected.ToString("0.00") + " m")}; vanilla {vanilla:0.00} m), airtime {Time.time - _takeoffTime:0.00} s");
                }
            }
            _wasGrounded = grounded;
        }

        // ── scale: the owner decides (with room to grow), every peer eases to the ZDO value ─────────────────────────────
        private static float _scaleDecided = 1f, _nextRoomCheck;
        private static string _blockedBy;

        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        private static void Player_Update_Scale(Player __instance)
        {
            try
            {
                if (__instance == null) return;
                var nview = __instance.m_nview;
                if (nview == null || !nview.IsValid()) return;
                var zdo = nview.GetZDO();
                if (zdo == null) return;

                if (nview.IsOwner() && __instance == Player.m_localPlayer) { DecideScale(__instance, zdo); DecideFirstPerson(__instance); }

                float target = Mathf.Clamp(zdo.GetFloat(s_scaleKey, 1f), ScaleMin, ScaleMax);
                float now = __instance.transform.localScale.y;
                if (Mathf.Abs(now - target) < 0.001f)
                {
                    if (now != target) __instance.transform.localScale = Vector3.one * target;
                    return;
                }
                float step = Mathf.Max(0.75f, Mathf.Abs(target - now) * 2f) * Time.deltaTime;   // ~0.5 s for most changes
                __instance.transform.localScale = Vector3.one * Mathf.MoveTowards(now, target, step);
            }
            catch (Exception e) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] scale: " + e.Message); }
        }

        private static void DecideScale(Player me, ZDO zdo)
        {
            if (me.IsAttached()) return;   // chairs, beds, mounts, ladders: the size doesn't change while attached
            var terr = LocalTerritory(me.transform.position);
            float want = ScaleOf(terr);
            if (Mathf.Approximately(want, _scaleDecided)) { _blockedBy = null; return; }

            if (want > _scaleDecided)   // growing: only with room
            {
                if (Time.time < _nextRoomCheck) return;
                _nextRoomCheck = Time.time + 0.25f;
                var blocker = RoomBlocker(me, want);
                if (blocker != null)
                {
                    if (_blockedBy != blocker.name)
                    {
                        _blockedBy = blocker.name;
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] waiting for room to grow to {want:0.##} (blocked by {blocker.name})");
                    }
                    return;
                }
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{(terr != null ? terr.Name : "outside")}' scale {want:0.##} (was {_scaleDecided:0.##})"
                + (_blockedBy != null ? " - room to grow now" : ""));
            _blockedBy = null;
            _scaleDecided = want;
            zdo.Set(s_scaleKey, want);
        }

        // ── 0.0.505 FirstPerson (Fire 10-06: forced, Valcast wins, no admin bypass) ─────────────────────────────────────
        // The local player inside a FirstPerson territory asks Core's CrawlCamera for first person under one owner tag;
        // leaving releases it and Core eases back to the player's own camera. Core decides priority (its existing
        // stand-downs: Valcast camera modes / possession, the First Person POV mod) and forces it (zoom ignored).
        // Core's Request is reached by reflection so this build runs on a Core without it (then it only logs once).
        private const string FirstPersonOwner = "FiresRPGmaker.Territory";
        private static bool _fpRequested, _fpApiChecked;
        private static System.Reflection.MethodInfo _fpRequest;

        private static void DecideFirstPerson(Player me)
        {
            var terr = LocalTerritory(me.transform.position);
            bool want = terr != null && terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.FirstPerson) && !me.IsDead();
            if (want == _fpRequested) return;
            if (!_fpApiChecked)
            {
                _fpApiChecked = true;
                var t = Type.GetType("FiresCore.Movement.CrawlCamera, FiresUnifiedCore");
                _fpRequest = t?.GetMethod("Request", new[] { typeof(string), typeof(bool), typeof(string) });
                if (_fpRequest == null) global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] FirstPerson: this Core has no CrawlCamera.Request(string,bool,string) yet; the flag waits for it");
            }
            _fpRequested = want;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{(terr != null ? terr.Name : "outside")}' first person {(want ? "ON" : "off")}");
            try { _fpRequest?.Invoke(null, new object[] { FirstPersonOwner, want, want ? $"territory '{terr.Name}'" : null }); }
            catch (Exception e) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] FirstPerson request failed: " + e.Message); }
        }

        // Core keeps a request across death/respawn, so the local player's going away (logout, world exit) releases it.
        [HarmonyPatch(typeof(Player), "OnDestroy")]
        [HarmonyPrefix]
        private static void Player_OnDestroy_ReleaseFirstPerson(Player __instance)
        {
            if (!_fpRequested || __instance != Player.m_localPlayer) return;
            _fpRequested = false;
            try { _fpRequest?.Invoke(null, new object[] { FirstPersonOwner, false, null }); } catch { }
        }

        private static int _solidMask;
        private static Collider RoomBlocker(Player me, float scale)
        {
            var cap = me.GetComponent<CapsuleCollider>();
            if (cap == null) return null;
            if (_solidMask == 0) _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 basePos = me.transform.position;
            float radius = cap.radius * scale, height = Mathf.Max(cap.height * scale, radius * 2f);
            // feet stay where they are: the capsule grows upward from the ground point, lifted slightly off the floor
            Vector3 bottom = basePos + Vector3.up * (radius + 0.05f);
            Vector3 top = basePos + Vector3.up * (height - radius);
            var hits = Physics.OverlapCapsule(bottom, top, radius * 0.95f, _solidMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h == null || h.transform.IsChildOf(me.transform)) continue;
                return h;
            }
            return null;
        }
    }
}
