using System;
using System.Collections.Generic;
using Terraria.ModLoader.IO;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Draws one payload entity overlay using preview context and origin-cell frames.</summary>
    internal delegate bool TryDrawEntityPreview(in PreviewEntityDrawArgs args);

    /// <summary>Named TileEntity preview handler, typically wrapping a shared draw strategy.</summary>
    internal readonly struct EntityPreviewHandler
    {
        public TryDrawEntityPreview? TryDraw { get; init; }
    }

    /// <summary>Inputs for drawing a payload entity at its captured origin cell.</summary>
    internal readonly struct PreviewEntityDrawArgs
    {
        public PreviewDrawContext Ctx { get; init; }
        public PayloadEntity Entity { get; init; }
        public PayloadCell OriginCell { get; init; }
        public int RelX { get; init; }
        public int RelY { get; init; }
    }

    /// <summary>Resolves payload TileEntity overlays from FullName, tag shape, or a generic Draw fallback.</summary>
    internal static class EntityPreviewResolver
    {
        private static readonly Dictionary<string, EntityPreviewHandler> ByName = new(StringComparer.Ordinal)
        {
            [PayloadEntityNames.ItemFrame] = new() { TryDraw = EntityPreviewStrategies.TryDrawItemOverlay },
            [PayloadEntityNames.WeaponsRack] = new() { TryDraw = EntityPreviewStrategies.TryDrawItemOverlay },
            [PayloadEntityNames.FoodPlatter] = new() { TryDraw = EntityPreviewStrategies.TryDrawItemOverlay },
            [PayloadEntityNames.DisplayDoll] = new() { TryDraw = EntityPreviewStrategies.TryDrawPlayerDoll },
            [PayloadEntityNames.HatRack] = new() { TryDraw = EntityPreviewStrategies.TryDrawPlayerDoll },
        };

        /// <summary>Draws an origin marker, then any matching content overlay for the payload entity.</summary>
        public static bool TryDraw(in PreviewEntityDrawArgs args)
        {
            // --- Always mark the origin cell; chests/signs stop there, display TEs continue to content.

            string fullName = args.Entity.FullName;
            if (string.IsNullOrEmpty(fullName))
                return false;

            bool drew = EntityPreviewStrategies.TryDrawOriginMarker(in args);

            // Chests and signs are inventory/text sidecars with no content overlay.
            if (fullName == PayloadEntityNames.Chest || fullName == PayloadEntityNames.Sign)
                return drew;

            if (ByName.TryGetValue(fullName, out EntityPreviewHandler handler) && handler.TryDraw is not null)
                return handler.TryDraw(in args) || drew;

            TagCompound? data = args.Entity.Data;
            if (data is { Count: > 0 })
            {
                if (data.ContainsKey("item") && EntityPreviewStrategies.TryDrawItemOverlay(in args))
                    return true;

                if (data.ContainsKey("items") && EntityPreviewStrategies.TryDrawPlayerDoll(in args))
                    return true;
            }

            return EntityPreviewStrategies.TryDrawGenericTileEntity(in args) || drew;
        }
    }
}
