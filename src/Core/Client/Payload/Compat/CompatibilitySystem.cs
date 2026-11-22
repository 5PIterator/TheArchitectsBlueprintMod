using System;
using System.Collections.Generic;
using Terraria.DataStructures;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core.Compat.Adapters;

namespace TheArchitectsBlueprintMod.Core.Compat
{
    /// <summary>Bootstraps the payload compatibility registry with the built-in Terraria adapter.</summary>
    public sealed class CompatibilitySystem : ModSystem
    {
        /// <summary>Registers the Terraria adapter when the mod loads.</summary>
        public override void Load()
        {
            // --- Terraria claims Terraria: FullNames only; unclaimed mods get tiles without entity data.
            CompatibilityRegistry.Register(new TerrariaCopyPasteAdapter());
        }
    }

    /// <summary>Lifecycle phase for payload mod-compatibility adapters.</summary>
    internal enum CompatibilityPhase
    {
        Copy,
        Sanitize,
        Paste,
        Preview,
    }

    /// <summary>Mutable per-call state shared with payload compatibility adapters.</summary>
    internal sealed class CompatibilityContext
    {
        /// <summary>Creates a context seeded with the active session mod list.</summary>
        public CompatibilityContext(CompatibilityPhase phase, IReadOnlyList<string> modsLoaded)
        {
            // --- Store phase and a non-null mods snapshot for adapter availability checks.
            Phase = phase;
            ModsLoaded = modsLoaded ?? Array.Empty<string>();
        }

        public CompatibilityPhase Phase { get; set; }

        public IReadOnlyList<string> ModsLoaded { get; }

        public string? FullName { get; set; }

        public int WorldX { get; set; }

        public int WorldY { get; set; }

        public int RelativeX { get; set; }

        public int RelativeY { get; set; }

        public TileEntity? LiveTileEntity { get; set; }

        /// <summary>Rebinds entity identity fields for the next copy/sanitize/paste call.</summary>
        public void BindEntity(string fullName, int worldX = 0, int worldY = 0, int relativeX = 0, int relativeY = 0, TileEntity? live = null)
        {
            // --- Overwrite per-entity fields while keeping the shared mods list.
            FullName = fullName;
            WorldX = worldX;
            WorldY = worldY;
            RelativeX = relativeX;
            RelativeY = relativeY;
            LiveTileEntity = live;
        }
    }

}
