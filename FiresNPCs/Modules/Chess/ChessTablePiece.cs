using System;
using UnityEngine;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Loads the chess buildables from the vaitems_chessboard bundle and appends them to
    /// <see cref="FiresRPGmaker.piecePrefabs"/> so they ride the existing ZNetScene + hammer registration passes on
    /// both client and server: the playing table and standalone board (interact to play), plus each Lewis chessman
    /// scaled up as a decorative statue.
    /// </summary>
    public static class ChessTablePiece
    {
        public static GameObject Prefab { get; private set; }   // the table — kept for compatibility
        private static bool _loaded;

        private struct Def
        {
            public string Name, Label, Desc;
            public int Interact;   // 0 = none, 1 = chess, 2 = fanorona, 3 = poker, 4 = sit (vanilla Chair)
            public Def(string n, string l, string d, int i) { Name = n; Label = l; Desc = d; Interact = i; }
        }

        private static readonly Def[] Defs =
        {
            new Def("piece_fires_pokertable", "Poker Table", "A felt-topped saloon card table. Use it to play Hold'em.", 3),
            new Def("piece_fires_saloonchair", "Saloon Chair", "A spindle-backed saloon chair. Pull it up to the table and sit.", 4),
            new Def("piece_fires_bottle_1", "Decanter", "A pot-bellied glass decanter, half a night left in it.", 0),
            new Def("piece_fires_bottle_2", "Whiskey Bottle", "A squat stoneware bottle of whiskey, label and all.", 0),
            new Def("piece_fires_bottle_3", "Wine Bottle", "A slim green bottle for the top shelf.", 0),
            new Def("piece_fires_bottle_beer", "Beer Bottle", "A lone beer bottle, sweating on the felt.", 0),
            new Def("piece_fires_cork", "Cork", "A pulled cork — the bottle's long gone.", 0),
            new Def("piece_fires_shotglass", "Shot Glass", "A heavy-bottomed shot glass.", 0),
            new Def("piece_fires_carddeck", "Card Deck", "A squared-up deck, ready for the deal.", 0),
            new Def("piece_fires_cardspread", "Dealt Cards", "A dealt hand fanned on the table.", 0),
            new Def("piece_fires_chipstack", "Chip Stack", "A tidy stack of poker chips.", 0),
            new Def("piece_fires_chippile", "Chip Pot", "A gambler's pot — stacks and strays.", 0),
            new Def("piece_fires_chessboard", "Chess Board", "The Jarl's board, set for a new game. Use it to play.", 1),
            new Def("piece_fires_chessboard_empty", "Chess Board (Empty)", "A bare marble board — scale it up and set your own pieces.", 0),
            new Def("piece_fires_fanorona", "Fanorona Board", "The carved stone-duel board, stones at the ready. Use it to play.", 2),
            new Def("piece_fires_chess_king", "Lewis King", "The crowned king of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_queen", "Lewis Queen", "The pensive queen of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_bishop", "Lewis Bishop", "The mitred bishop of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_knight", "Lewis Knight", "The mounted knight of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_rook", "Lewis Warder", "The shield-biting warder of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_pawn", "Lewis Pawn", "A humble pawn of the Lewis chessmen, carved large.", 0),
            new Def("piece_fires_chess_king_red", "Lewis King (Red)", "The crowned king in the historical red stain.", 0),
            new Def("piece_fires_chess_queen_red", "Lewis Queen (Red)", "The pensive queen in the historical red stain.", 0),
            new Def("piece_fires_chess_bishop_red", "Lewis Bishop (Red)", "The mitred bishop in the historical red stain.", 0),
            new Def("piece_fires_chess_knight_red", "Lewis Knight (Red)", "The mounted knight in the historical red stain.", 0),
            new Def("piece_fires_chess_rook_red", "Lewis Warder (Red)", "The shield-biting warder in the historical red stain.", 0),
            new Def("piece_fires_chess_pawn_red", "Lewis Pawn (Red)", "A humble pawn in the historical red stain.", 0),
            new Def("piece_fires_chessboard2", "Chess Board (Viking)", "A hand-carved war set in weathered bone and cold iron. Use it to play.", 1),
            new Def("piece_fires_chessboard2_empty", "Chess Board (Viking, Empty)", "A cold slate board — scale it up and set your own pieces.", 0),
            new Def("piece_fires_chess2_king", "Viking King", "The carved warlord king, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_queen", "Viking Queen", "The carved queen, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_bishop", "Viking Bishop", "The carved bishop, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_knight", "Viking Knight", "The carved war-horse, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_rook", "Viking Tower", "The carved tower, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_pawn", "Viking Pawn", "A carved shieldman, cut large in weathered bone.", 0),
            new Def("piece_fires_chess2_king_dark", "Viking King (Iron)", "The carved warlord king in cold iron.", 0),
            new Def("piece_fires_chess2_queen_dark", "Viking Queen (Iron)", "The carved queen in cold iron.", 0),
            new Def("piece_fires_chess2_bishop_dark", "Viking Bishop (Iron)", "The carved bishop in cold iron.", 0),
            new Def("piece_fires_chess2_knight_dark", "Viking Knight (Iron)", "The carved war-horse in cold iron.", 0),
            new Def("piece_fires_chess2_rook_dark", "Viking Tower (Iron)", "The carved tower in cold iron.", 0),
            new Def("piece_fires_chess2_pawn_dark", "Viking Pawn (Iron)", "A carved shieldman in cold iron.", 0),
        };

        public static void Load(AssetBundle bundle)
        {
            if (_loaded) return;
            if (bundle == null)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: chess bundle missing — chess pieces unavailable.");
                return;
            }
            _loaded = true;

            int ok = 0;
            foreach (var def in Defs)
                if (Configure(bundle, def)) ok++;
            Debug.Log($"{FiresRPGmaker.PluginName}: chess pieces loaded ({ok}/{Defs.Length}) + queued for hammer registration.");
        }

        private static bool Configure(AssetBundle bundle, Def def)
        {
            try
            {
                var prefab = bundle.LoadAsset<GameObject>($"assets/custom/vaitems/pieces/{def.Name}.prefab")
                             ?? bundle.LoadAsset<GameObject>(def.Name);
                if (prefab == null)
                {
                    Debug.LogWarning($"{FiresRPGmaker.PluginName}: {def.Name} not in the chess bundle (old bundle?).");
                    return false;
                }

                UnityEngine.Object.DontDestroyOnLoad(prefab);   // fake-null protection — the static list outlives worlds

                var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;
                nview.m_syncInitialScale = true;   // Structure Tweaks / Infinity Hammer scaling persists — giant boards welcome

                var piece = prefab.GetComponent<Piece>() ?? prefab.AddComponent<Piece>();
                piece.m_name = def.Label;
                piece.m_description = def.Desc;
                piece.m_category = Piece.PieceCategory.Furniture;
                piece.m_enabled = true;
                piece.m_canBeRemoved = true;
                piece.m_allowedInDungeons = true;
                piece.m_placeEffect = new EffectList();          // bundle-baked EffectList refs are the PlacePiece trap
                piece.m_resources = new Piece.Requirement[0];

                bool livingBoard = def.Name == "piece_fires_chessboard" || def.Name == "piece_fires_chessboard2";
                if (def.Interact == 1 && prefab.GetComponent<ChessBoardInteractable>() == null)
                    prefab.AddComponent<ChessBoardInteractable>();
                if (livingBoard && prefab.GetComponent<ChessBoardVisual>() == null)
                    prefab.AddComponent<ChessBoardVisual>();   // the living board — mirrors the UI game in the world
                if (livingBoard && prefab.GetComponent<Modules.TableGames.LongRangeInteract>() == null)
                    prefab.AddComponent<Modules.TableGames.LongRangeInteract>().Range = 60f;   // arena play from afar
                if (def.Interact == 2 && prefab.GetComponent<FanoronaBoardInteractable>() == null)
                    prefab.AddComponent<FanoronaBoardInteractable>();
                if (def.Interact == 3 && prefab.GetComponent<PokerTableInteractable>() == null)
                    prefab.AddComponent<PokerTableInteractable>();
                if (def.Interact == 4 && prefab.GetComponent<Chair>() == null)
                {
                    // Vanilla sit, configured from piece_chair: plain component, safe on the dedi. The builder
                    // bakes the "attachpoint" child at the seat pan.
                    var chair = prefab.AddComponent<Chair>();
                    chair.m_name = def.Label;
                    chair.m_useDistance = 1.5f;
                    chair.m_detachOffset = new Vector3(0f, 0.5f, 0f);
                    chair.m_attachAnimation = "attach_chair";
                    var ap = prefab.transform.Find("attachpoint");
                    chair.m_attachPoint = ap != null ? ap : prefab.transform;
                    if (ap == null)
                        Debug.LogWarning($"{FiresRPGmaker.PluginName}: {def.Name} has no attachpoint child — sitting at the piece origin.");
                }

                if (!FiresRPGmaker.piecePrefabs.Contains(prefab))
                    FiresRPGmaker.piecePrefabs.Add(prefab);

                if (def.Name == "piece_fires_chesstable") Prefab = prefab;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{FiresRPGmaker.PluginName}: chess piece '{def.Name}' load failed: {ex}");
                return false;
            }
        }
    }

    /// <summary>[E] on the placed table/board opens the chess UI, bound to THIS board so the physical pieces mirror
    /// the game and a game in progress resumes from the board's ZDO.</summary>
    public sealed class ChessBoardInteractable : MonoBehaviour, Interactable, Hoverable
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            ChessMpFlow.OpenBoard(GetComponent<ZNetView>());
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
            => Localization.instance.Localize("Chess\n[<color=yellow><b>$KEY_Use</b></color>] Play");

        public string GetHoverName() => "Chess";
        // Valheim 1.0 added GetHoverOffset to the Hoverable interface; it is ADDED to
        // Player.m_maxInteractDistance, so 0 preserves the previous interact range exactly.
        public float GetHoverOffset() => 0f;
    }

    /// <summary>[E] on the placed Fanorona board opens the stone duel.</summary>
    public sealed class FanoronaBoardInteractable : MonoBehaviour, Interactable, Hoverable
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;
            global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.Open(3);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
            => Localization.instance.Localize("Fanorona\n[<color=yellow><b>$KEY_Use</b></color>] Play");

        public string GetHoverName() => "Fanorona";
        // Valheim 1.0 added GetHoverOffset to the Hoverable interface; it is ADDED to
        // Player.m_maxInteractDistance, so 0 preserves the previous interact range exactly.
        public float GetHoverOffset() => 0f;
    }

    /// <summary>[E] on the placed saloon table: Solo vs the house AI, or queue for an online tournament. A player
    /// already seated at a live online table jumps straight back to it.</summary>
    public sealed class PokerTableInteractable : MonoBehaviour, Interactable, Hoverable
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (!(user is Player player) || player != Player.m_localPlayer) return false;

            if (Modules.Poker.PokerNetClient.ActiveTable != null)
            {
                Modules.TableGames.GameModePopup.Show("Hold'em", "The gambler's table.", new[]
                {
                    ("Return to Your Table", "You're seated at a live online table.", (Action)(() => Modules.Poker.PokerNetClient.OpenTable())),
                    ("Solo vs Computer", "Play the house AI with theater chips.", (Action)(() => UI.Screens.PlayerPanels.Minigames.PokerTableGame.Open(1000, 10))),
                });
                return true;
            }

            if (global::FiresCore.Queue.FiresQueueSystem.LocalInQueue(Modules.Poker.PokerLobby.QueueId))
            {
                Modules.TableGames.GameModePopup.Show("Hold'em", "You're queued for a tournament — the table deals when everyone readies up.", new[]
                {
                    ("Open the Lobby", "See who's seated and ready up.", (Action)(() => global::FiresCore.Queue.FiresQueueLobby.Show(Modules.Poker.PokerLobby.QueueId))),
                    ("Leave the Queue", "Give up your spot at the table.", (Action)(() => Modules.Poker.PokerNetClient.LeaveQueue())),
                });
                return true;
            }

            var nv = GetComponent<ZNetView>();
            string cashCur = Modules.Poker.PokerCashTables.CurrencyCfg != null ? Modules.Poker.PokerCashTables.CurrencyCfg.Value : "Coins";
            int cashBuy = Modules.Poker.PokerCashTables.BuyInCfg != null ? Modules.Poker.PokerCashTables.BuyInCfg.Value : 500;
            Modules.TableGames.GameModePopup.Show("Hold'em", "The gambler's table.", new[]
            {
                ("Solo vs Computer", "Play the house AI with theater chips.", (Action)(() => UI.Screens.PlayerPanels.Minigames.PokerTableGame.Open(1000, 10))),
                ("Open Table (" + cashCur + ")", "Buy in " + cashBuy + " from your bank — real coin; players take the bots' seats.", (Action)(() => Modules.Poker.PokerCashTables.RequestJoin(nv))),
                ("Tournament (online)", "Queue with other players — the table deals when everyone readies up.", (Action)(() =>
                {
                    Modules.Poker.PokerLobby.EnsureRegistered();
                    global::FiresCore.Queue.FiresQueueLobby.Show(Modules.Poker.PokerLobby.QueueId);
                })),
            });
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
            => Localization.instance.Localize("Poker Table\n[<color=yellow><b>$KEY_Use</b></color>] Play Hold'em");

        public string GetHoverName() => "Poker Table";
        // Valheim 1.0 added GetHoverOffset to the Hoverable interface; it is ADDED to
        // Player.m_maxInteractDistance, so 0 preserves the previous interact range exactly.
        public float GetHoverOffset() => 0f;
    }
}
