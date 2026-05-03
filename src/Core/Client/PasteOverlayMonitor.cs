using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using ArchitectsStencilMod.UI;
using ArchitectsStencilMod.UIKit;
using ArchitectsStencilMod.Core;

namespace ArchitectsStencilMod.Core.Client
{
    /// <summary>
    /// Client-only: registers paste progress overlays from local <see cref="PastingSystem"/> (single player / host)
    /// or from server-sent snapshots (multiplayer clients).
    /// </summary>
    public sealed class PasteOverlayMonitor : ModSystem
    {
        private readonly List<PasteOverlaySnapshot> _localScratch = [];
        private readonly List<PasteOverlaySnapshot> _remoteFromServer = [];
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
            InterfaceSystem.ClearLayer(InterfaceLayer.PasteProgress);

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
                    RegisterSnapshots(_localScratch);
                    break;
                case NetmodeID.MultiplayerClient:
                    RegisterSnapshots(_remoteFromServer);
                    break;
                case NetmodeID.Server:
                    return;
            }
        }

        private static void RegisterSnapshots(List<PasteOverlaySnapshot> snapshots)
        {
            UIView layer = InterfaceSystem.GetLayer(InterfaceLayer.PasteProgress);
            for (int i = 0; i < snapshots.Count; i++)
            {
                PasteOverlaySnapshot s = snapshots[i];

                Color placeHue = ArchitectVisualPalette.PlacePhaseColor;
                Color clearHue = ArchitectVisualPalette.ClearPhaseColor;
                Color baseHue = s.IsPlacePhase ? placeHue : clearHue;

                if (s.Pending.Length > 0)
                    layer.AddChild(new UITileFill(s.Pending, baseHue * 0.35f));
                if (s.Clear.Length > 0)
                    layer.AddChild(new UITileFill(s.Clear, clearHue * 0.6f));
                if (s.Place.Length > 0)
                    layer.AddChild(new UITileFill(s.Place, placeHue * 0.6f));
                if (s.Done.Length > 0)
                    layer.AddChild(new UITileFill(s.Done, baseHue * 0.1f));
            }
        }

        private void ClearAllPasteFamilies()
        {
            InterfaceSystem.ClearLayer(InterfaceLayer.PasteProgress);
        }
    }
}
