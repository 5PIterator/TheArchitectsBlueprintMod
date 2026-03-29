using System;
using ArchitectsStencilMod.Config;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core
{
    /// <summary>
    /// A tile from Projection.SourceAreaTiles / TargetAreaTiles for layer operations.
    /// World = where to modify (Bounds.Location + Relative for place; SourceArea.Location + Relative for cut).
    /// </summary>
    internal readonly struct PasteTile(Point relative, Point world, Tile source, Tile target)
    {
        public readonly Point Relative = relative;
        public readonly Point World = world;
        public readonly Tile Source = source;
        public readonly Tile Target = target;

        public int X => World.X;
        public int Y => World.Y;
    }

    /// <summary>
    /// Arguments for a layer's Place and Consume execution. Place validates item availability; Consume consumes after successful Place.
    /// </summary>
    internal readonly struct PasteContext(int playerIndex, IConsumableSlots slots, PastingConfig config)
    {
        public readonly Player Player = Main.player[playerIndex];
        public readonly bool CheatSweep = config.CheatSweep;
        public readonly bool ForceTiles = config.ForceTiles;
        public readonly bool NeedsItems = config.NeedsItems;
        public readonly int PasteStepSize = Math.Max(1, config.PasteStepSize);
        public readonly int PasteDelayMs = Math.Max(0, config.PasteDelayMs);
        public readonly IConsumableSlots Slots = slots;
    }
}
