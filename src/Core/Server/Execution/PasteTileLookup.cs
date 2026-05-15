using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core
{

    internal enum PasteSubject
    {
        Source,
        Target
    }

    /// <summary>
    /// One relative index in a paste: <see cref="InteractionEntry"/>, source payload tile, source-side and target-side <see cref="Tile"/> views, and coordinates. See <see cref="Projection"/>.
    /// <see cref="GetTileForIdentify"/> selects which face <see cref="Layer.Identify"/> uses (source intent vs world to clear).
    /// </summary>
    internal readonly struct PasteTile(
        PayloadCell payloadCell,
        InteractionEntry interaction,
        Tile source,
        Tile target,
        Point relative,
        Point world)
    {
        /// <summary>Payload cell from the projection: not a real tile, just a data structure.</summary>
        public readonly PayloadCell PayloadCell = payloadCell;
        /// <summary>What is required to place <see cref="Target"/> (items, wiring, etc.) for the active layer.</summary>
        public readonly InteractionEntry Interaction = interaction;
        /// <summary>Match/clear “intent” vs <see cref="Target"/> (empty after cut on source clears). Prefer <see cref="PayloadCell"/> for blueprint fields; materialize via <see cref="PasteTile.PayloadTile"/> only when an API needs <see cref="Tile"/>.</summary>
        public readonly Tile Source = source;
        /// <summary>Target tile: live world tile in the target area at <see cref="World"/> (<see cref="Projection.TryGetTargetTile"/>).</summary>
        public readonly Tile Target = target;
        /// <summary>Offset in the payload grid / selection (same as <see cref="Projection.SourceTiles"/> / <see cref="Projection.TargetTiles"/> relatives).</summary>
        public readonly Point Relative = relative;
        /// <summary>World grid coordinates of the target tile (<see cref="Target"/>).</summary>
        public readonly Point World = world;
        /// <summary>Materializes <see cref="PayloadCell"/> as a scratch <see cref="Tile"/> (allocates/overwrites tModLoader’s <see cref="PayloadCell.ToTile"/> buffer). Prefer <see cref="PayloadCell"/> at call sites.</summary>
        public readonly Tile PayloadTile => PayloadCell.ToTile();
        /// <summary>Shorthand for <see cref="Relative"/>.X (not world; use <see cref="World"/> for <see cref="Main.tile"/>).</summary>
        public int X => Relative.X;
        /// <summary>Shorthand for <see cref="Relative"/>.Y (not world; use <see cref="World"/> for <see cref="Main.tile"/>).</summary>
        public int Y => Relative.Y;

        /// <summary>Source (blueprint) or target (world) <see cref="Tile"/> for <see cref="Layer.Identify"/> (placement vs clear planning is chosen by the caller, not stored on the struct).</summary>
        public static Tile GetTileForIdentify(in PasteTile tile, PasteSubject mode) =>
            mode == PasteSubject.Source ? tile.Source : tile.Target;
    }

    /// <summary>
    /// Resolves <see cref="PasteTile"/> by <see cref="PasteTile.Relative"/> or <see cref="PasteTile.World"/>.
    /// Indexes are built once in the constructor (O(n)); lookups are O(1) average.
    /// </summary>
    internal sealed class PasteTileLookup
    {
        private readonly PasteTile[,] _pasteTiles;
        private readonly Dictionary<Point, PasteTile> _byRelative;
        private readonly Dictionary<Point, PasteTile> _byWorld;

        public PasteTileLookup(PasteTile[,] pasteTiles)
        {
            _pasteTiles = pasteTiles ?? throw new ArgumentNullException(nameof(pasteTiles));
            int n = pasteTiles.Length;
            _byRelative = new Dictionary<Point, PasteTile>(n);
            _byWorld = new Dictionary<Point, PasteTile>(n);

            foreach (var pasteTile in pasteTiles)
            {
                if (!_byRelative.TryAdd(pasteTile.Relative, pasteTile))
                    throw new InvalidOperationException($"Duplicate paste Relative ({pasteTile.Relative.X},{pasteTile.Relative.Y}); each cell must appear once.");
                if (!_byWorld.TryAdd(pasteTile.World, pasteTile))
                    throw new InvalidOperationException($"Duplicate paste World ({pasteTile.World.X},{pasteTile.World.Y}); each target cell must appear once.");
            }
        }

        public PasteTile[,] PasteTiles => _pasteTiles;

        public int Count => _pasteTiles.Length;

        /// <summary>True if <paramref name="relative"/> is part of this paste.</summary>
        public bool IsInPasteScope(Point relative) => _byRelative.ContainsKey(relative);

        public bool TryGetPasteTile(Point relative, out PasteTile pasteTile) => _byRelative.TryGetValue(relative, out pasteTile);

        public bool TryGetPasteTileByWorld(Point world, out PasteTile pasteTile) => _byWorld.TryGetValue(world, out pasteTile);

        /// <summary>Gets a paste tile by relative coordinate.</summary>
        /// <exception cref="KeyNotFoundException">No tile at <paramref name="relative"/>.</exception>
        public PasteTile GetPasteTile(Point relative) =>
            TryGetPasteTile(relative, out PasteTile pasteTile)
                ? pasteTile
                : throw new KeyNotFoundException($"Paste tile not found for relative coordinate: ({relative.X},{relative.Y})");

        /// <summary>Gets a paste tile by target world position.</summary>
        /// <exception cref="KeyNotFoundException">No tile at <paramref name="world"/>.</exception>
        public PasteTile GetPasteTileByWorld(Point world) =>
            TryGetPasteTileByWorld(world, out PasteTile pasteTile)
                ? pasteTile
                : throw new KeyNotFoundException($"Paste tile not found for world coordinate: ({world.X},{world.Y})");

        /// <summary>Resolves each relative in order. Fails on the first missing key.</summary>
        /// <exception cref="KeyNotFoundException">Any relative is not in the paste.</exception>
        public IReadOnlyList<PasteTile> GetPasteTiles(IReadOnlyList<Point> relatives)
        {
            if (relatives is null) throw new ArgumentNullException(nameof(relatives));
            if (relatives.Count == 0) return Array.Empty<PasteTile>();

            var result = new PasteTile[relatives.Count];
            for (int i = 0; i < relatives.Count; i++)
            {
                Point rel = relatives[i];
                if (!_byRelative.TryGetValue(rel, out result[i]))
                    throw new KeyNotFoundException($"Paste tile not found for relative coordinate: ({rel.X},{rel.Y}) (index {i}).");
            }

            return result;
        }

        /// <summary>Resolves each world position in order. Fails on the first missing key.</summary>
        /// <exception cref="KeyNotFoundException">Any world position is not in the paste.</exception>
        public IReadOnlyList<PasteTile> GetPasteTilesByWorld(IReadOnlyList<Point> worlds)
        {
            if (worlds is null) throw new ArgumentNullException(nameof(worlds));
            if (worlds.Count == 0) return Array.Empty<PasteTile>();

            var result = new PasteTile[worlds.Count];
            for (int i = 0; i < worlds.Count; i++)
            {
                Point w = worlds[i];
                if (!_byWorld.TryGetValue(w, out result[i]))
                    throw new KeyNotFoundException($"Paste tile not found for world coordinate: ({w.X},{w.Y}) (index {i}).");
            }

            return result;
        }

        /// <summary>
        /// For each entry <c>delta</c> in <paramref name="offsetMap"/>, resolves <c><paramref name="anchorRelative"/> + delta</c>.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Any computed relative is not in the paste.</exception>
        public IReadOnlyList<PasteTile> GetPasteTileByOffsetMap(Point anchorRelative, IReadOnlyList<Point> offsetMap)
        {
            if (offsetMap is null) throw new ArgumentNullException(nameof(offsetMap));
            if (offsetMap.Count == 0) return Array.Empty<PasteTile>();

            var result = new PasteTile[offsetMap.Count];
            for (int i = 0; i < offsetMap.Count; i++)
            {
                Point offsetDelta = offsetMap[i];
                Point neighborRelative = new(anchorRelative.X + offsetDelta.X, anchorRelative.Y + offsetDelta.Y);
                if (!_byRelative.TryGetValue(neighborRelative, out result[i]))
                    throw new KeyNotFoundException(
                        $"Paste tile not found for relative ({neighborRelative.X},{neighborRelative.Y}) = anchor ({anchorRelative.X},{anchorRelative.Y}) + offset ({offsetDelta.X},{offsetDelta.Y}) (index {i}).");
            }

            return result;
        }
    }
}
