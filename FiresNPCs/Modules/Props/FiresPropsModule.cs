using System;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Props
{
    // Loads the firesnpcs_props bundle. FiresMailbox_* -> buildable Furniture pieces whose interact opens the mail UI
    // (MailboxPiece attached here by name); FiresSignBoard_* -> ZNetScene-registered plain prefabs (NPC model-swap
    // targets, NOT on the hammer). Mirrors the ChessTablePiece.Load pattern: append mailboxes to piecePrefabs so they
    // ride the existing ZNetScene + hammer registration passes.
    public static class FiresPropsModule
    {
        private static bool _loaded;

        public static void Load(AssetBundle bundle)
        {
            if (_loaded) return;
            if (bundle == null)
            {
                Debug.LogWarning($"{FiresRPGmaker.PluginName}: firesnpcs_props bundle missing — mailboxes/boards unavailable.");
                return;
            }
            _loaded = true;

            int mailboxes = 0, boards = 0;
            foreach (var prefab in bundle.LoadAllAssets<GameObject>())
            {
                if (prefab == null) continue;
                prefab.SetActive(true);
                UnityEngine.Object.DontDestroyOnLoad(prefab);   // static lists outlive worlds; guards Unity fake-null

                if (prefab.name.StartsWith("FiresMailbox_", StringComparison.Ordinal))
                {
                    var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
                    nview.m_persistent = true;
                    nview.m_syncInitialScale = true;

                    var piece = prefab.GetComponent<Piece>() ?? prefab.AddComponent<Piece>();
                    piece.m_name = "Mailbox";
                    piece.m_description = "Read and send mail.";
                    piece.m_category = Piece.PieceCategory.Furniture;
                    piece.m_enabled = true;
                    piece.m_canBeRemoved = true;
                    piece.m_allowedInDungeons = true;
                    piece.m_placeEffect = new EffectList();       // bundle-baked EffectList refs are the PlacePiece NRE trap
                    piece.m_resources = new Piece.Requirement[0]; // free build

                    if (prefab.GetComponent<MailboxPiece>() == null) prefab.AddComponent<MailboxPiece>();
                    if (!FiresRPGmaker.piecePrefabs.Contains(prefab)) FiresRPGmaker.piecePrefabs.Add(prefab);
                    mailboxes++;
                }
                else if (prefab.name.StartsWith("FiresSignBoard_", StringComparison.Ordinal))
                {
                    var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
                    nview.m_persistent = true;
                    if (!VAPieceManager.NetworkOnlyPrefabs.Contains(prefab)) VAPieceManager.NetworkOnlyPrefabs.Add(prefab);
                    boards++;
                }
            }
            Debug.Log($"{FiresRPGmaker.PluginName}: props loaded — {mailboxes} mailbox piece(s) + {boards} board prefab(s).");
        }
    }
}
