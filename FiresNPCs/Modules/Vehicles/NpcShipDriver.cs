using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.Modules.Vehicles
{
    /// <summary>
    /// 0.0.547 (Fire 10-09: an NPC drives a REAL ship along its patrol route; design Tools\RPG_NPC_VEHICLES_SOUNDS_DESIGN.md B).
    /// Vanilla physics do all the sailing and buoyancy. The driver only works the controls a helmsman would: the speed steps
    /// (Ship.Forward/Backward/Stop RPCs) and the rudder (the "Rudder" RPC), toward the next route point. It runs only where
    /// the ship's ZNetView is owner. A ship is "driven" while its ZDO holds the NPC route key; vanilla stops any ship with no
    /// Player aboard, so the two patches below count a driven ship as crewed.
    /// </summary>
    public class NpcShipDriver : MonoBehaviour
    {
        public const string RouteKey = "fires_npc_ship_route";
        private const float ArriveRadius = 8f, LogEvery = 5f;

        internal static readonly HashSet<List<Player>> DrivenCrewLists = new HashSet<List<Player>>();
        internal static readonly HashSet<Ship> DrivenShips = new HashSet<Ship>();
        private static readonly FieldInfo F_players = AccessTools.Field(typeof(Ship), "m_players");
        private static readonly FieldInfo F_speed = AccessTools.Field(typeof(Ship), "m_speed");

        private Ship _ship;
        private ZNetView _nview;
        private List<Player> _crew;
        private string _routeName;
        private List<Vector3> _points;
        private int _wp;
        private float _nextLog, _nextSpeedStep;
        private float _turnSign;   // 0.0.553: locked turn direction while coming about (|err| > 150)
        // 0.0.555: learned rudder sense per speed class (+ = positive rudder turns clockwise); sail starts at +1 (proven 0.0.551).
        private float _senseSail = 3f, _senseOars = 0f, _lastYaw, _lastRudder;
        private bool _hasYaw;

        public static NpcShipDriver Bind(Ship ship, string routeName)
        {
            if (ship == null) return null;
            var nv = ship.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return null;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(RouteKey, routeName ?? "");
            var d = ship.GetComponent<NpcShipDriver>() ?? ship.gameObject.AddComponent<NpcShipDriver>();
            d.Load();
            return d;
        }

        private void Awake()
        {
            _ship = GetComponent<Ship>();
            _nview = GetComponent<ZNetView>();
            _crew = F_players?.GetValue(_ship) as List<Player>;
            Load();
        }

        private void Load()
        {
            _routeName = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(RouteKey, "") : "";
            var route = string.IsNullOrEmpty(_routeName) ? null : PatrolRouteManager.GetRoute(_routeName);
            _points = route?.Points != null ? new List<Vector3>(route.Points) : null;
            bool driven = _points != null && _points.Count >= 2;
            if (_crew != null) { if (driven) DrivenCrewLists.Add(_crew); else DrivenCrewLists.Remove(_crew); }
            if (driven) DrivenShips.Add(_ship); else DrivenShips.Remove(_ship);
            if (driven) _wp = Nearest(transform.position);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcVehicle] ship {name} {(_nview != null && _nview.IsValid() ? _nview.GetZDO().m_uid.ToString() : "?")}: route '{_routeName}' {(driven ? $"{_points.Count} point(s), start wp {_wp + 1}" : "none (not driven)")}");
        }

        private int Nearest(Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < _points.Count; i++) { float d = Flat(_points[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void OnDestroy() { if (_crew != null) DrivenCrewLists.Remove(_crew); DrivenShips.Remove(_ship); }

        private void FixedUpdate()
        {
            if (_ship == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (_points == null || _points.Count < 2) return;

            Vector3 pos = transform.position, target = _points[_wp];
            Vector3 to = Flat(target - pos);
            float dist = to.magnitude;
            if (dist < ArriveRadius)
            {
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcVehicle] ship {_nview.GetZDO().m_uid} reached wp {_wp + 1}/{_points.Count}");
                _wp = (_wp + 1) % _points.Count;
                return;
            }

            // Rudder: the signed heading error to the waypoint, -1..1. 0.0.551 (ship drive round, 0.0.547: steered AWAY in 5/5 samples):
            // vanilla turns toward a positive rudder for a positive SignedAngle, so the rudder follows the error sign. (was: negative rudder to
            // starboard, so steer against the error's sign.
            float err = Vector3.SignedAngle(Flat(transform.forward), to, Vector3.up);
            // 0.0.553 ([visual] leg 2: err 143-157 deg for 13 samples, never came about): past 150 deg lock the turn direction so it
            // cannot dither across +-180, and hold full rudder until it is lined up again (below 60 deg the lock is released).
            // 0.0.555 ([visual] 12:30: locked rudder -1 carried the ship from err -153 through 180 to +72, i.e. at oars the rudder
            // turned it the other way than under sail). The driver LEARNS the rudder's effect: per speed class (oars vs sail) it sums
            // sign(yaw rate) x sign(rudder) while the rudder is hard over and the ship is turning, and steers with that sense.
            // _turnSign is now the wanted TURN direction (+1 = clockwise, toward a positive err); the lock releases under 60 deg
            // or when the error's sign no longer matches it.
            var speedNow = (Ship.Speed)F_speed.GetValue(_ship);
            bool oars = speedNow == Ship.Speed.Slow || speedNow == Ship.Speed.Back;
            float yaw = transform.eulerAngles.y;
            if (_hasYaw && Time.fixedDeltaTime > 0f && Mathf.Abs(_lastRudder) > 0.5f)
            {
                float rate = Mathf.DeltaAngle(_lastYaw, yaw) / Time.fixedDeltaTime;
                if (Mathf.Abs(rate) > 2f)
                {
                    float vote = Mathf.Sign(rate) * Mathf.Sign(_lastRudder);
                    if (oars) _senseOars = Mathf.Clamp(_senseOars + vote, -50f, 50f); else _senseSail = Mathf.Clamp(_senseSail + vote, -50f, 50f);
                }
            }
            _lastYaw = yaw; _hasYaw = true;
            float sense = (oars ? _senseOars : _senseSail) < 0f ? -1f : 1f;

            if (Mathf.Abs(err) > 150f) { if (_turnSign == 0f) _turnSign = err >= 0f ? 1f : -1f; }
            else if (Mathf.Abs(err) < 60f || (_turnSign != 0f && Mathf.Sign(err) != _turnSign)) _turnSign = 0f;
            float rudder = sense * (_turnSign != 0f ? _turnSign : Mathf.Clamp(err / 45f, -1f, 1f));
            _lastRudder = rudder;
            _nview.Invoke("Rudder", rudder);

            // Speed: Full when far and roughly on heading, Half when turning hard, Slow when close.
            // 0.0.553: a big heading change is made at Slow (oars), the tightest turn; full sail only when lined up and far.
            // 0.0.554 (Fire: "use the sails to go faster"): FULL sail on straight legs (|err| < 20, more than 40 m out), HALF for
            // moderate corrections or the last 40 m, oars (Slow) for tight turns / coming about (|err| > 60) and the last 20 m.
            float aerr = Mathf.Abs(err);
            Ship.Speed want = aerr > 60f || dist < 20f ? Ship.Speed.Slow : aerr < 20f && dist > 40f ? Ship.Speed.Full : Ship.Speed.Half;
            var have = (Ship.Speed)F_speed.GetValue(_ship);
            if (have != want && Time.time >= _nextSpeedStep)
            {
                _nextSpeedStep = Time.time + 0.6f;
                if (Rank(want) > Rank(have)) _ship.Forward(); else _ship.Backward();
            }

            if (Time.time >= _nextLog)
            {
                _nextLog = Time.time + LogEvery;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcVehicle] ship {_nview.GetZDO().m_uid} at ({pos.x:F0},{pos.z:F0}) -> wp {_wp + 1}/{_points.Count} ({target.x:F0},{target.z:F0}) dist {dist:F0} m, heading err {err:F0}, sail {Sail(have)}->{Sail(want)}, rudder {rudder:F2}{(_turnSign != 0f ? " (coming about, locked)" : "")}, rudder sense sail {(_senseSail < 0f ? -1 : 1)} ({_senseSail:F0}) oars {(_senseOars < 0f ? -1 : 1)} ({_senseOars:F0}), owner {_nview.GetZDO().GetOwner()}");
            }
        }

        private static string Sail(Ship.Speed s) => s == Ship.Speed.Full ? "FULL sail" : s == Ship.Speed.Half ? "half sail" : s == Ship.Speed.Slow ? "oars" : s == Ship.Speed.Back ? "back" : "stop";

        private static int Rank(Ship.Speed s) => s == Ship.Speed.Back ? 0 : s == Ship.Speed.Stop ? 1 : s == Ship.Speed.Slow ? 2 : s == Ship.Speed.Half ? 3 : 4;

        // Ships loaded from a save re-attach their driver from the ZDO key.
        [HarmonyPatch(typeof(Ship), "Awake")]
        private static class ShipAwake
        {
            private static void Postfix(Ship __instance)
            {
                var nv = __instance.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) return;
                if (!string.IsNullOrEmpty(nv.GetZDO().GetString(RouteKey, "")) && __instance.GetComponent<NpcShipDriver>() == null)
                    __instance.gameObject.AddComponent<NpcShipDriver>();
            }
        }

        // A driven ship counts as having a controlling helmsman.
        [HarmonyPatch(typeof(Ship), "HaveControllingPlayer")]
        private static class HaveControllingPlayer
        {
            private static void Postfix(Ship __instance, ref bool __result) { if (!__result && DrivenShips.Contains(__instance)) __result = true; }
        }

        // CustomFixedUpdate zeroes speed and rudder when m_players.Count == 0. For a driven ship that count reads as 1.
        [HarmonyPatch(typeof(Ship), "CustomFixedUpdate")]
        private static class CrewCount
        {
            private static readonly MethodInfo M_count = AccessTools.PropertyGetter(typeof(List<Player>), "Count");
            private static readonly MethodInfo M_eff = AccessTools.Method(typeof(CrewCount), nameof(Effective));
            private static int Effective(List<Player> list) => list.Count == 0 && DrivenCrewLists.Contains(list) ? 1 : list.Count;
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                foreach (var ci in code)
                {
                    if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand as MethodInfo == M_count)
                        yield return new CodeInstruction(OpCodes.Call, M_eff);
                    else yield return ci;
                }
            }
        }
    }
}
