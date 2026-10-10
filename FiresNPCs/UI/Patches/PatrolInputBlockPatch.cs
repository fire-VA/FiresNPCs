using HarmonyLib;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// While the patrol recorder's name field is open, suppress the local player's input so typing the
    /// route name doesn't also drive movement/camera/attacks. Mirrors how vanilla menus gate input.
    /// </summary>
    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PatrolInputBlockPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!PatrolRecorder.IsNaming) return true;
            __result = false;
            return false;
        }
    }
}
