using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsBlueprintMod.UI;
using ArchitectsBlueprintMod.Core;

namespace ArchitectsBlueprintMod.Core.Client
{
    /// <summary>
    /// Client-only: registers paste progress overlays from local <see cref="PastingSystem"/> (single player / host)
    /// or from server-sent snapshots (multiplayer clients).
    /// </summary>
    public sealed class PasteOverlayMonitor : ModSystem
    {
        private const string LocalFamilyPrefix = "pasteLocal";
        private const string RemoteFamilyPrefix = "pasteRemote";

        private readonly List<PasteOverlaySnapshot> _localScratch = [];
        private readonly List<PasteOverlaySnapshot> _remoteFromServer = [];
        private readonly HashSet<int> _lastLocalOverlayIds = [];
        private readonly HashSet<int> _lastRemoteOverlayIds = [];

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            Refresh();
        }

        public override void OnWorldUnload()
        {
            ClearAllPasteFamilies();
            _remoteFromServer.Clear();
        }

        /// <summary>Applies snapshots received from the server (multiplayer client).</summary>
        internal void ApplyRemoteSnapshots(ReadOnlySpan<PasteOverlaySnapshot> snapshots)
        {
            _remoteFromServer.Clear();
            for (int i = 0; i < snapshots.Length; i++)
                _remoteFromServer.Add(snapshots[i]);
        }

        private void Refresh()
        {
            UnregisterTrackedFamilies(_lastLocalOverlayIds, LocalFamilyPrefix);
            UnregisterTrackedFamilies(_lastRemoteOverlayIds, RemoteFamilyPrefix);

            switch (Main.netMode)
            {
                case NetmodeID.SinglePlayer:
                    // Single player or listen-server host: read active operations for the local player.
                    _localScratch.Clear();
                    var pasting = ModContent.GetInstance<PastingSystem>();
                    foreach (var op in pasting.ActiveOperations)
                    {
                        if (op.Projection.Owner != Main.myPlayer)
                            continue;
                        if (op.TryBuildOverlaySnapshot(out PasteOverlaySnapshot snap))
                            _localScratch.Add(snap);
                    }
                    RegisterSnapshots(_localScratch, LocalFamilyPrefix, _lastLocalOverlayIds);
                    break;
                case NetmodeID.MultiplayerClient:
                    RegisterSnapshots(_remoteFromServer, RemoteFamilyPrefix, _lastRemoteOverlayIds);
                    break;
                case NetmodeID.Server:
                    return;
            }
        }

        private static void UnregisterTrackedFamilies(HashSet<int> ids, string prefix)
        {
            foreach (int id in ids)
                OverlaySystem.UnregisterFamily(MakeFamily(prefix, id));
            ids.Clear();
        }

        private static void RegisterSnapshots(List<PasteOverlaySnapshot> snapshots, string prefix, HashSet<int> trackIds)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                PasteOverlaySnapshot s = snapshots[i];
                string family = MakeFamily(prefix, s.OverlayId);
                trackIds.Add(s.OverlayId);

                Color placeHue = ArchitectVisualPalette.PlacePhaseColor;
                Color clearHue = ArchitectVisualPalette.ClearPhaseColor;
                Color baseHue = s.IsPlacePhase ? placeHue : clearHue;

                if (s.Pending.Length > 0)
                    OverlaySystem.RegTileFill("pending", s.Pending, baseHue * 0.35f, family);
                if (s.Clear.Length > 0)
                    OverlaySystem.RegTileFill("clear", s.Clear, clearHue * 0.6f, family);
                if (s.Place.Length > 0)
                    OverlaySystem.RegTileFill("place", s.Place, placeHue * 0.6f, family);
                if (s.Done.Length > 0)
                    OverlaySystem.RegTileFill("done", s.Done, baseHue * 0.1f, family);
            }
        }

        private static string MakeFamily(string prefix, int overlayId) => $"{prefix}:{overlayId}";

        private void ClearAllPasteFamilies()
        {
            UnregisterTrackedFamilies(_lastLocalOverlayIds, LocalFamilyPrefix);
            UnregisterTrackedFamilies(_lastRemoteOverlayIds, RemoteFamilyPrefix);
        }
    }
}
