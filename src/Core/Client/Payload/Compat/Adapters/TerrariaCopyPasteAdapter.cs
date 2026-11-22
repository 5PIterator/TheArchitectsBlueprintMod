using System;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace TheArchitectsBlueprintMod.Core.Compat.Adapters
{
    /// <summary>Built-in adapter for Chest, Sign, DisplayDoll, and other Terraria tile entities.</summary>
    internal sealed class TerrariaCopyPasteAdapter : ICompatibilityAdapter
    {
        private const string FullNamePrefix = "Terraria:";

        public string Id => "Terraria";

        public int Priority => 0;

        /// <summary>Always available as the Terraria entity workflow.</summary>
        public bool IsAvailable(CompatibilityContext ctx) => true;

        /// <summary>Claims only Terraria FullNames; unclaimed mods get tiles with no entity sidecar.</summary>
        public bool Handles(CompatibilityContext ctx) =>
            !string.IsNullOrEmpty(ctx.FullName)
            && ctx.FullName.StartsWith(FullNamePrefix, StringComparison.Ordinal);

        /// <summary>Copies via DisplayDoll custom path or generic SaveData.</summary>
        public bool TryCopy(CompatibilityContext ctx, TileEntity te, out TagCompound? data)
        {
            // --- Dispatch by FullName into PayloadEntityHandlers vanilla copy helpers.
            data = PayloadEntityHandlers.CopyVanillaTileEntity(te, ctx.FullName ?? string.Empty);
            return true;
        }

        /// <summary>Sanitizes Chest/Sign/DisplayDoll identity tags or generic TE data.</summary>
        public bool TrySanitize(CompatibilityContext ctx, TagCompound data, out TagCompound? cleaned)
        {
            // --- Dispatch by FullName into PayloadEntityHandlers vanilla sanitize helpers.
            cleaned = PayloadEntityHandlers.SanitizeVanilla(ctx.FullName ?? string.Empty, data);
            return true;
        }

        /// <summary>Applies Chest/Sign/DisplayDoll or generic LoadData at the host origin.</summary>
        public bool TryPaste(CompatibilityContext ctx, int worldX, int worldY, TagCompound data, out bool applied)
        {
            // --- Always handle; applied reflects whether the vanilla path wrote data.
            applied = PayloadEntityHandlers.ApplyVanilla(ctx.FullName ?? string.Empty, worldX, worldY, data);
            return true;
        }
    }
}
