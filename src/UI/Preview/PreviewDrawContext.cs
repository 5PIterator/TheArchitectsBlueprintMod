using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Shared state passed to each <see cref="PreviewLayer"/> while drawing a payload region.</summary>
    internal readonly struct PreviewDrawContext
    {
        public const int TilePixelSize = 16;

        public required SpriteBatch SpriteBatch { get; init; }
        public required Vector2 Position { get; init; }
        public required float Scale { get; init; }
        public required Color Tint { get; init; }
        public IReadOnlyDictionary<Point, PayloadCell>? CellLookup { get; init; }
        public int? WorldOriginX { get; init; }
        public int? WorldOriginY { get; init; }

        /// <summary>Returns whether a relative neighbor cell carries the same wire color.</summary>
        public bool NeighborHasWire(int relX, int relY, int offsetX, int offsetY, Func<PayloadCell.WireData, bool> hasWire)
        {
            // --- Look up the offset cell in the sparse payload map and test its wire flags.

            if (CellLookup is null)
                return false;

            if (!CellLookup.TryGetValue(new Point(relX + offsetX, relY + offsetY), out PayloadCell neighbor))
                return false;

            return hasWire(neighbor.Wire);
        }
    }
}
