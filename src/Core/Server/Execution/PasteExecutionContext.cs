using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Server-safe paste runtime state built from Payload and placement metadata (no OverlaySystem).
    /// </summary>
    internal sealed class PasteExecutionContext
    {
        private List<Rectangle> _inventories = [];

        /// <summary>Optional live client ghost for Lock/Unlock on SP / Host &amp; Play local execute.</summary>
        private readonly Projection? _clientProjection;

        private PasteExecutionContext(
            int owner,
            int correlationId,
            Payload payload,
            Rectangle targetArea,
            Rectangle sourceArea,
            bool cutSource,
            bool[,] included,
            Projection? clientProjection)
        {
            Owner = owner;
            CorrelationId = correlationId;
            Payload = payload;
            TargetArea = targetArea;
            SourceArea = sourceArea;
            CutSource = cutSource && !sourceArea.IsEmpty && payload.IsDefined;
            Included = included;
            _clientProjection = clientProjection;
            RefreshInventories();
        }

        /// <summary>Player index that owns this paste.</summary>
        public int Owner { get; }

        /// <summary>Request / projection correlation id used for ACK and cancel.</summary>
        public int CorrelationId { get; set; }

        /// <summary>Prepared payload grid for this paste.</summary>
        public Payload Payload { get; }

        /// <summary>World rectangle where the payload is placed.</summary>
        public Rectangle TargetArea { get; }

        /// <summary>Alias for <see cref="TargetArea"/> (legacy Projection naming).</summary>
        public Rectangle Area => TargetArea;

        /// <summary>World rectangle of the capture / cut source.</summary>
        public Rectangle SourceArea { get; }

        /// <summary>When true, planning includes a source-area clear pass.</summary>
        public bool CutSource { get; }

        /// <summary>Per-cell inclusion mask sized to the payload grid.</summary>
        public bool[,] Included { get; }

        /// <summary>Nearby chest footprints refreshed from world state.</summary>
        public IReadOnlyList<Rectangle> Inventories => _inventories;

        /// <summary>True when target bounds and payload are defined.</summary>
        public bool IsDefined => !TargetArea.IsEmpty && Payload.IsDefined;

        /// <summary>True when the payload grid is allocated.</summary>
        public bool HasPayload => Payload.IsDefined;

        /// <summary>Payload width and height.</summary>
        public Point PayloadSize => Payload.Size;

        /// <summary>Inclusion grid width.</summary>
        public int GridWidth => Included.GetLength(0);

        /// <summary>Inclusion grid height.</summary>
        public int GridHeight => Included.GetLength(1);

        /// <summary>Live client projection when this context was built from one; otherwise null.</summary>
        public Projection? ClientProjection => _clientProjection;

        /// <summary>
        /// Builds execution state from a live client projection for local paste.
        /// </summary>
        public static PasteExecutionContext FromProjection(Projection projection)
        {
            // --- Snapshot payload, areas, and inclusion; keep the projection only for Lock/Unlock.

            ArgumentNullException.ThrowIfNull(projection);
            if (!projection.IsDefined || !projection.HasPayload)
                throw new InvalidOperationException("Projection is not ready for paste execution.");

            Payload payload = Payload.FromClone(projection.Payload);
            int w = payload.Width;
            int h = payload.Height;
            var included = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Point rel = new(x, y);
                    included[x, y] = projection.TryGetTileByRelative(rel, out Blueprint.BlueprintCell? cell)
                        && cell is not null
                        && cell.Included;
                }
            }

            return new PasteExecutionContext(
                owner: projection.Owner,
                correlationId: projection.Id,
                payload: payload,
                targetArea: new Rectangle(projection.Area.X, projection.Area.Y, w, h),
                sourceArea: projection.SourceArea,
                cutSource: projection.CutSource,
                included: included,
                clientProjection: projection);
        }

        /// <summary>
        /// Builds execution state from a parsed payload and placement metadata (network paste path).
        /// </summary>
        public static PasteExecutionContext FromParsedRequest(
            int owner,
            int correlationId,
            Payload payload,
            Point targetOrigin,
            Rectangle sourceArea,
            bool cutSource,
            IReadOnlyList<Point> excludedRelatives,
            IReadOnlyList<string>? metadataModsLoaded)
        {
            // --- Prepare payload, size the target rect, then apply excluded relatives as inclusion holes.

            ArgumentNullException.ThrowIfNull(payload);
            if (!payload.IsDefined)
                throw new InvalidOperationException("Paste payload is not defined.");

            Payload prepared = PayloadCompatibility.Prepare(
                payload,
                PayloadPrepareMode.SkipIncompatible,
                metadataModsLoaded,
                out _);

            if (!prepared.IsDefined)
                throw new InvalidOperationException("Prepared paste payload is empty.");

            int w = prepared.Width;
            int h = prepared.Height;
            var included = new bool[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    included[x, y] = true;
            }

            if (excludedRelatives is not null)
            {
                for (int i = 0; i < excludedRelatives.Count; i++)
                {
                    Point rel = excludedRelatives[i];
                    if ((uint)rel.X < (uint)w && (uint)rel.Y < (uint)h)
                        included[rel.X, rel.Y] = false;
                }
            }

            var targetArea = new Rectangle(targetOrigin.X, targetOrigin.Y, w, h);
            return new PasteExecutionContext(
                owner: owner,
                correlationId: correlationId,
                payload: Payload.FromClone(prepared),
                targetArea: targetArea,
                sourceArea: sourceArea,
                cutSource: cutSource,
                included: included,
                clientProjection: null);
        }

        /// <summary>Maps a payload-relative cell to world tile coordinates.</summary>
        public Point RelativeToWorld(Point relative) =>
            new(TargetArea.X + relative.X, TargetArea.Y + relative.Y);

        /// <summary>Returns whether the payload-relative cell is included in this paste.</summary>
        public bool IsIncluded(Point relative)
        {
            if ((uint)relative.X >= (uint)GridWidth || (uint)relative.Y >= (uint)GridHeight)
                return false;
            return Included[relative.X, relative.Y];
        }

        /// <summary>Counts included cells in the inclusion mask.</summary>
        public int CountIncludedVolume()
        {
            // --- Sum true entries in the inclusion grid.

            int count = 0;
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    if (Included[x, y])
                        count++;
                }
            }

            return count;
        }

        /// <summary>Reads live <c>Main.tile</c> under the target area at a relative cell.</summary>
        public bool TryGetTargetTile(Point relative, out Tile targetTile)
        {
            if ((uint)relative.X >= (uint)GridWidth || (uint)relative.Y >= (uint)GridHeight)
            {
                targetTile = default;
                return false;
            }

            Point world = RelativeToWorld(relative);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                targetTile = default;
                return false;
            }

            targetTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>Reads live <c>Main.tile</c> under the source area at a relative cell.</summary>
        public bool TryGetSourceTile(Point relative, out Tile sourceTile)
        {
            if (SourceArea.IsEmpty
                || (uint)relative.X >= (uint)SourceArea.Width
                || (uint)relative.Y >= (uint)SourceArea.Height)
            {
                sourceTile = default;
                return false;
            }

            Point world = new(SourceArea.X + relative.X, SourceArea.Y + relative.Y);
            if (!WorldGen.InWorld(world.X, world.Y))
            {
                sourceTile = default;
                return false;
            }

            sourceTile = Main.tile[world.X, world.Y];
            return true;
        }

        /// <summary>Rebuilds nearby chest footprints from authoritative world chest state.</summary>
        public void RefreshInventories()
        {
            // --- Replace cached footprints with a fresh reach-ring scan.

            _inventories = PasteInventoryReach.CollectChestFootprints(TargetArea);
        }

        /// <summary>Locks the live client projection when this context owns one.</summary>
        public void NotifyClientLocked()
        {
            // --- Network shells have no ghost; local paste locks the UI projection.

            _clientProjection?.Lock();
        }

        /// <summary>Unlocks the live client projection and clears its correlation id.</summary>
        public void NotifyClientUnlocked()
        {
            // --- Clear correlation on the ghost, then unlock for further edits.

            if (_clientProjection is null)
                return;

            _clientProjection.Id = 0;
            _clientProjection.Unlock();
        }

        /// <summary>True when <paramref name="projection"/> is the live ghost this context was built from.</summary>
        public bool IsBoundTo(Projection? projection) =>
            projection is not null && ReferenceEquals(_clientProjection, projection);
    }
}
