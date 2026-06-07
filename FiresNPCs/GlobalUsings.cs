// Central type aliases redirecting FiresNPCs to the canonical FiresCore (FUC) implementations
// as local forks are removed during the Phase-1 de-dup. Each alias replaces a deleted local copy,
// so existing call sites resolve to the shared type with no per-file edits.
global using NetworkObjectHelper = FiresCore.Net.NetworkObjectHelper;
global using SafeRoutedRpc = FiresCore.Net.SafeRoutedRpc;
global using AdminSyncing = FiresCore.Sync.AdminSyncing;
global using ManifestDiffSync = FiresCore.Sync.ManifestDiffSync;
global using NpcType = FiresCore.Npc.NpcType;
global using SavedNpcData = FiresCore.Npc.SavedNpcData;
global using FiresCore.Async;
global using FiresCore.UI;
