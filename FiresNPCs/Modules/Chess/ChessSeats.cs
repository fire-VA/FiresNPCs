using System;
using System.Globalization;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Board-bound multiplayer seats, stored on the chess board piece's ZDO. A seat is a "<id>|<name>|<worldTime>"
    /// string refreshed by a heartbeat while its player is at the board (UI open or parked on the dock chip);
    /// a stale heartbeat frees the seat, so crashes and log-offs never wedge a board. World time comes from
    /// <see cref="ZNet.GetTimeSeconds"/> — shared by every client, no wall-clock skew.
    /// </summary>
    public static class ChessSeats
    {
        public const string KeyW = "fires_chess_seatW";
        public const string KeyB = "fires_chess_seatB";
        public const string KeyLast = "fires_chess_last";   // "<seq>|<from>|<to>|<promo>" of the latest move
        public const double StaleSeconds = 15.0;

        public struct Seat { public long Id; public string Name; public double Time; public bool Valid; }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
        public static long MyId => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
        private static string MyName => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";

        public static Seat Read(ZNetView nv, bool white)
        {
            var seat = new Seat();
            if (nv == null || !nv.IsValid()) return seat;
            string raw = nv.GetZDO().GetString(white ? KeyW : KeyB, "");
            if (string.IsNullOrEmpty(raw)) return seat;
            var parts = raw.Split('|');
            if (parts.Length < 3) return seat;
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seat.Id)) return seat;
            seat.Name = parts[1];
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seat.Time)) return seat;
            seat.Valid = true;
            return seat;
        }

        public static bool IsLive(Seat s) => s.Valid && s.Id != 0 && Now - s.Time < StaleSeconds;
        public static bool IsMine(Seat s) => s.Valid && s.Id != 0 && s.Id == MyId;

        /// <summary>Claim a seat if it is free, stale, or already mine.</summary>
        public static bool TryClaim(ZNetView nv, bool white)
        {
            if (nv == null || !nv.IsValid() || MyId == 0) return false;
            var s = Read(nv, white);
            if (IsLive(s) && !IsMine(s)) return false;
            Write(nv, white);
            return true;
        }

        /// <summary>Refresh my heartbeat on a seat I hold.</summary>
        public static void Heartbeat(ZNetView nv, bool white)
        {
            if (nv == null || !nv.IsValid()) return;
            if (!IsMine(Read(nv, white))) return;
            Write(nv, white);
        }

        public static void Release(ZNetView nv, bool white)
        {
            if (nv == null || !nv.IsValid()) return;
            if (!IsMine(Read(nv, white))) return;
            try { nv.ClaimOwnership(); nv.GetZDO().Set(white ? KeyW : KeyB, ""); } catch { }
        }

        private static void Write(ZNetView nv, bool white)
        {
            try
            {
                nv.ClaimOwnership();
                nv.GetZDO().Set(white ? KeyW : KeyB,
                    MyId.ToString(CultureInfo.InvariantCulture) + "|" + MyName + "|" + Now.ToString("F1", CultureInfo.InvariantCulture));
            }
            catch { }
        }
    }

    /// <summary>
    /// The interact flow for a chess board: mode popup (Solo vs Computer / Multiplayer), seat claims, rejoin, and the
    /// full-board spectate gate. Multiplayer rides the living-board ZDO — the first claimant resets the position, the
    /// second joins into it, and every move relays through <c>fires_chess</c> + <c>fires_chess_last</c>.
    /// </summary>
    public static class ChessMpFlow
    {
        public static void OpenBoard(ZNetView nv)
        {
            if (nv == null || !nv.IsValid()) return;

            var w = ChessSeats.Read(nv, true);
            var b = ChessSeats.Read(nv, false);
            bool wLive = ChessSeats.IsLive(w), bLive = ChessSeats.IsLive(b);

            // Already seated → straight back into the game, in whichever view was used last.
            if (wLive && ChessSeats.IsMine(w)) { UI.Screens.PlayerPanels.Minigames.ChessTableGame.OpenMpAuto(nv, ChessColor.White); return; }
            if (bLive && ChessSeats.IsMine(b)) { UI.Screens.PlayerPanels.Minigames.ChessTableGame.OpenMpAuto(nv, ChessColor.Black); return; }

            if (wLive && bLive)
            {
                Msg(w.Name + " and " + b.Name + " hold this board — spectate!");
                return;
            }

            string mpLabel = wLive ? "Join " + w.Name : bLive ? "Join " + b.Name : "Multiplayer";
            string mpSub = wLive || bLive
                ? "Take the open seat and play them."
                : "Claim a seat — a second player joins at this board.";

            Modules.TableGames.GameModePopup.Show("Chess", "The Jarl's board awaits.", new[]
            {
                ("Solo vs Computer", "Play Skald in the window.", (Action)(() => StartSolo(nv, threeD: false))),
                ("Solo — sit at the board", "Play Skald on the real pieces: hint stones, the lot.", (Action)(() => StartSolo(nv, threeD: true))),
                (mpLabel, mpSub, (Action)(() => JoinMp(nv, threeD: false))),
                (mpLabel + " — at the board", "The same seat, played on the real pieces.", (Action)(() => JoinMp(nv, threeD: true))),
            });
        }

        private static void StartSolo(ZNetView nv, bool threeD)
        {
            var w = ChessSeats.Read(nv, true); var b = ChessSeats.Read(nv, false);
            if (ChessSeats.IsLive(w) || ChessSeats.IsLive(b)) { Msg("An online game holds this board."); return; }
            if (threeD) UI.Screens.PlayerPanels.Minigames.ChessTableGame.Open3D(0, nv);
            else UI.Screens.PlayerPanels.Minigames.ChessTableGame.Open(0, nv);
        }

        private static void JoinMp(ZNetView nv, bool threeD)
        {
            var w = ChessSeats.Read(nv, true);
            var b = ChessSeats.Read(nv, false);
            bool wLive = ChessSeats.IsLive(w), bLive = ChessSeats.IsLive(b);

            if (!wLive)
            {
                if (!ChessSeats.TryClaim(nv, true)) { Msg("Seat taken — try again."); return; }
                if (!bLive)
                {
                    // Fresh online game — reset the board for both players.
                    try
                    {
                        nv.ClaimOwnership();
                        nv.GetZDO().Set(ChessBoardVisual.ZdoKey, ChessBoard.Serialize(new ChessBoard()));
                        nv.GetZDO().Set(ChessSeats.KeyLast, "");
                    }
                    catch { }
                }
                OpenSeat(nv, ChessColor.White, threeD);
            }
            else if (!bLive)
            {
                if (!ChessSeats.TryClaim(nv, false)) { Msg("Seat taken — try again."); return; }
                OpenSeat(nv, ChessColor.Black, threeD);
            }
            else Msg("Both seats are taken — spectate!");
        }

        private static void OpenSeat(ZNetView nv, ChessColor color, bool threeD)
        {
            if (threeD) UI.Screens.PlayerPanels.Minigames.ChessTableGame.OpenMp3D(nv, color);
            else UI.Screens.PlayerPanels.Minigames.ChessTableGame.OpenMp(nv, color);
        }

        private static void Msg(string text)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, text);
        }
    }
}
