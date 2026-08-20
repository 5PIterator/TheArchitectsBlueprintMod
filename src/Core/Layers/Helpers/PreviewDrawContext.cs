using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Shared state passed to each <see cref="Layer"/> while drawing a payload region.</summary>
    internal readonly struct PreviewDrawContext
    {
        public const int TilePixelSize = 16;

        public required SpriteBatch SpriteBatch { get; init; }
        public required Vector2 Position { get; init; }
        public required float Scale { get; init; }
        public required Color Tint { get; init; }
        public IReadOnlyDictionary<Point, PayloadCell>? CellLookup { get; init; }
        public IReadOnlyList<PayloadEntity>? Entities { get; init; }
        public int MinX { get; init; }
        public int MaxX { get; init; }
        public int MinY { get; init; }
        public int MaxY { get; init; }
        public Func<Point, bool>? IncludeCell { get; init; }
        public int? WorldOriginX { get; init; }
        public int? WorldOriginY { get; init; }

        /// <summary>Maps a payload-local pixel offset onto the current preview sprite batch.</summary>
        public Vector2 ToScreen(Vector2 localPixels) =>
            (Position + localPixels) * Scale;

        /// <summary>Returns whether a relative cell sits in the visible range and inclusion filter.</summary>
        public bool IsCellVisible(int relX, int relY)
        {
            if (relX < MinX || relX >= MaxX || relY < MinY || relY >= MaxY)
                return false;
            if (IncludeCell is not null && !IncludeCell(new Point(relX, relY)))
                return false;
            return true;
        }

        /// <summary>Returns whether a relative neighbor cell carries the same wire color.</summary>
        public bool NeighborHasWire(int relX, int relY, int offsetX, int offsetY, Func<PayloadCell.WireData, bool> hasWire)
        {
            if (CellLookup is null)
                return false;

            if (!CellLookup.TryGetValue(new Point(relX + offsetX, relY + offsetY), out PayloadCell neighbor))
                return false;

            return hasWire(neighbor.Wire);
        }
    }
}
