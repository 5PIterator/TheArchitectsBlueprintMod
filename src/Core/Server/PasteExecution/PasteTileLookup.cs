using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using ArchitectsStencilMod.Core.TileMapping;

namespace ArchitectsStencilMod.Core
{

    internal enum PasteSubject
    {
        Source,
        Target
    }

    /// <summary>
    /// One relative index in a paste: <see cref="InteractionEntry"/>, source payload tile, source-side and target-side <see cref="Tile"/> views, and coordinates. See <see cref="Projection"/>.
    /// <see cref="SubjectType"/> selects which face <see cref="Layer.Identify"/> uses (source intent vs world to clear).
    /// </summary>
    internal readonly struct PasteTile(
        Tile payload,
        InteractionEntry interaction,
        Tile source,
        Tile target,
        Point relative,
        Point world,
        PasteSubject subjectType = PasteSubject.Source)
    {
        /// <summary>Frozen blueprint at <see cref="Relative"/>; all placement and post-place consume use this, not live map.</summary>
        public readonly Tile Payload = payload;
        /// <summary>What is required to place <see cref="Target"/> (items, wiring, etc.) for the active layer.</summary>
        public readonly InteractionEntry Interaction = interaction;
        /// <summary>Match/clear “intent” vs <see cref="Target"/> (empty after cut on source clears). Placement uses <see cref="Payload"/>.</summary>
        public readonly Tile Source = source;
        /// <summary>Target tile: live world tile in the target area at <see cref="World"/> (<see cref="Projection.TryGetTargetTile"/>).</summary>
        public readonly Tile Target = target;
        /// <summary>Offset in the payload grid / selection (same as <see cref="Projection.SourceTiles"/> / <see cref="Projection.TargetTiles"/> relatives).</summary>
        public readonly Point Relative = relative;
        /// <summary>World grid coordinates of the target tile (<see cref="Target"/>).</summary>
        public readonly Point World = world;
        /// <summary>Which tile view <see cref="Layer.Identify"/> should classify (placement vs clear planning).</summary>
        public readonly PasteSubject SubjectType = subjectType;
        /// <summary>Shorthand for <see cref="Relative"/>.X (not world; use <see cref="World"/> for <see cref="Main.tile"/>).</summary>
        public int X => Relative.X;
        /// <summary>Shorthand for <see cref="Relative"/>.Y (not world; use <see cref="World"/> for <see cref="Main.tile"/>).</summary>
        public int Y => Relative.Y;

        /// <summary>Resolved subject tile for identification: payload-side vs destination world.</summary>
        public Tile Subject => SubjectType == PasteSubject.Source ? Source : Target;

        /// <summary>
        /// Tile classified by <see cref="Layer.Identify"/>: live <see cref="Target"/> when planning clears,
        /// frozen <see cref="Payload"/> when planning places (never the mutable <see cref="Source"/> “intent” slot used in <see cref="Layer.Match"/>).
        /// </summary>
        public Tile IdentifyFace => SubjectType == PasteSubject.Target ? Target : Payload;

        /// <summary>Returns a copy with a different <see cref="SubjectType"/> (lookup entries default to <see cref="PasteSubject.Source"/>).</summary>
        public PasteTile WithSubject(PasteSubject subject) =>
            new(Payload, Interaction, Source, Target, Relative, World, subject);

        /// <summary>
        /// Neighbor/read helper: world <see cref="Target"/> when <paramref name="subject"/> is <see cref="PasteSubject.Target"/>,
        /// else blueprint <see cref="Payload"/> for placement-style classification.
        /// </summary>
        public static Tile Face(PasteSubject subject, in PasteTile pasteTile) =>
            subject == PasteSubject.Target ? pasteTile.Target : pasteTile.Payload;
    }

    /// <summary>
    /// Resolves <see cref="PasteTile"/> by <see cref="PasteTile.Relative"/> or <see cref="PasteTile.World"/>.
    /// Indexes are built once in the constructor (O(n)); lookups are O(1) average.
    /// </summary>
    internal sealed class PasteTileLookup
    {
        private readonly IReadOnlyList<PasteTile> _pasteTiles;
        private readonly Dictionary<Point, PasteTile> _byRelative;
        private readonly Dictionary<Point, PasteTile> _byWorld;

        public PasteTileLookup(IReadOnlyList<PasteTile> pasteTiles)
        {
            _pasteTiles = pasteTiles ?? throw new ArgumentNullException(nameof(pasteTiles));
            int n = pasteTiles.Count;
            _byRelative = new Dictionary<Point, PasteTile>(n);
            _byWorld = new Dictionary<Point, PasteTile>(n);

            for (int i = 0; i < n; i++)
            {
                PasteTile t = pasteTiles[i];
                if (!_byRelative.TryAdd(t.Relative, t))
                    throw new InvalidOperationException($"Duplicate paste Relative ({t.Relative.X},{t.Relative.Y}); each cell must appear once.");
                if (!_byWorld.TryAdd(t.World, t))
                    throw new InvalidOperationException($"Duplicate paste World ({t.World.X},{t.World.Y}); each target cell must appear once.");
            }
        }

        public IReadOnlyList<PasteTile> PasteTiles => _pasteTiles;

        public int Count => _pasteTiles.Count;

        /// <summary>True if <paramref name="relative"/> is part of this paste.</summary>
        public bool IsInPasteScope(Point relative) => _byRelative.ContainsKey(relative);

        public bool TryGetPasteTile(Point relative, out PasteTile tile) => _byRelative.TryGetValue(relative, out tile);

        public bool TryGetPasteTileByWorld(Point world, out PasteTile tile) => _byWorld.TryGetValue(world, out tile);

        /// <summary>Gets a paste tile by relative coordinate.</summary>
        /// <exception cref="KeyNotFoundException">No tile at <paramref name="relative"/>.</exception>
        public PasteTile GetPasteTile(Point relative) =>
            TryGetPasteTile(relative, out PasteTile tile)
                ? tile
                : throw new KeyNotFoundException($"Paste tile not found for relative coordinate: ({relative.X},{relative.Y})");

        /// <summary>Gets a paste tile by target world position.</summary>
        /// <exception cref="KeyNotFoundException">No tile at <paramref name="world"/>.</exception>
        public PasteTile GetPasteTileByWorld(Point world) =>
            TryGetPasteTileByWorld(world, out PasteTile tile)
                ? tile
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
        /// For each entry <c>delta</c> in <paramref name="offsetMap"/>, resolves <c><paramref name="relative"/> + delta</c>.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Any computed relative is not in the paste.</exception>
        public IReadOnlyList<PasteTile> GetPasteTileByOffsetMap(Point relative, IReadOnlyList<Point> offsetMap)
        {
            if (offsetMap is null) throw new ArgumentNullException(nameof(offsetMap));
            if (offsetMap.Count == 0) return Array.Empty<PasteTile>();

            var result = new PasteTile[offsetMap.Count];
            for (int i = 0; i < offsetMap.Count; i++)
            {
                Point d = offsetMap[i];
                Point key = new(relative.X + d.X, relative.Y + d.Y);
                if (!_byRelative.TryGetValue(key, out result[i]))
                    throw new KeyNotFoundException(
                        $"Paste tile not found for relative ({key.X},{key.Y}) = anchor ({relative.X},{relative.Y}) + offset ({d.X},{d.Y}) (index {i}).");
            }

            return result;
        }
    }
}
