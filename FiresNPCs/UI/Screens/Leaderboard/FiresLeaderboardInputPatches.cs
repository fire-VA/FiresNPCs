using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.UI.Leaderboard
{
    // Ports VikingLands.Core's leaderboard input-capture (LeaderboardPatches input block). While the window is
    // open (FiresLeaderboardGui.IsInputBlockingActive) the cursor is shown, the camera stops rotating, and
    // player movement / placement / scroll / map input is swallowed — i.e. the window is MODAL, exactly like TDV.
    // Every type patched here (PlayerController, GameCamera, ZInput, Minimap, Player placement) is client-only, so
    // this class is listed in FiresRPGmaker's DedicatedServerSkipPatchTypes set (Ascend.cs) — on a headless
    // server its references would native-crash Mono's IL rewriter and it is gameplay-irrelevant there anyway.
    [HarmonyPatch]
    internal static class FiresLeaderboardInputPatches
    {
        // The core of the modal lock: refuse the camera's mouse capture and force the cursor visible every frame
        // the window is open, so the camera freezes and the pointer can click the UI.
        [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
        [HarmonyPrefix]
        private static bool GameCameraUpdateMouseCapturePrefix()
        {
            if (!FiresLeaderboardGui.IsInputBlockingActive) return true;
            try
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            catch { }
            return false;
        }

        // Block player look/move input while open.
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        [HarmonyPrefix]
        private static bool PlayerControllerTakeInputPrefix() => !FiresLeaderboardGui.IsInputBlockingActive;

        // Don't let clicks-through place/aim pieces while the board is up.
        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        [HarmonyPrefix]
        private static bool PlayerTryPlacePiecePrefix() => !FiresLeaderboardGui.IsInputBlockingActive;

        [HarmonyPatch(typeof(Player), "PlacePiece")]
        [HarmonyPrefix]
        private static bool PlayerPlacePiecePrefix() => !FiresLeaderboardGui.IsInputBlockingActive;

        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        [HarmonyPrefix]
        private static bool PlayerUpdatePlacementPrefix() => !FiresLeaderboardGui.IsInputBlockingActive;

        // Swallow the scroll wheel so paging/clicking the list never zooms the camera.
        [HarmonyPatch(typeof(ZInput), "GetMouseScrollWheel")]
        [HarmonyPostfix]
        private static void ZInputGetMouseScrollWheelPostfix(ref float __result)
        {
            if (FiresLeaderboardGui.IsInputBlockingActive) __result = 0f;
        }

        // Don't let the map toggle while the board is up.
        [HarmonyPatch(typeof(Minimap), "SetMapMode")]
        [HarmonyPrefix]
        private static bool MinimapSetMapModePrefix() => !FiresLeaderboardGui.IsInputBlockingActive;
    }
}
