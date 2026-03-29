using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace ArchitectsStencilMod.Core
{
    internal delegate bool TryGetLookupTile(Point relative, out Tile tile);

    internal enum PasteIdentifyChannel
    {
        Source,
        Target,
        CutSource,
    }

    internal sealed class PasteTileDatabase
    {
        private readonly Point _worldOrigin;
        private readonly Point _cutWorldOrigin;
        private readonly int _width;
        private readonly int _height;

        private readonly TryGetLookupTile _sourceLookup;
        private readonly TryGetLookupTile _targetLookup;
        private readonly TryGetLookupTile _cutLookup;

        private readonly Dictionary<Point, int> _idByRelative;
        private readonly Dictionary<int, Point> _relativeById;
        private readonly Dictionary<Point, Point> _relativeByWorld;
        private readonly List<Blueprint.BoundedTile> _selectedTiles;

        public PasteTileDatabase(
            Point worldOrigin,
            Point cutWorldOrigin,
            Point size,
            IReadOnlyList<Blueprint.BoundedTile> selectedTiles,
            TryGetLookupTile sourceLookup,
            TryGetLookupTile targetLookup,
            TryGetLookupTile cutLookup)
        {
            ArgumentNullException.ThrowIfNull(selectedTiles);
            ArgumentNullException.ThrowIfNull(sourceLookup);
            ArgumentNullException.ThrowIfNull(targetLookup);
            ArgumentNullException.ThrowIfNull(cutLookup);

            _worldOrigin = worldOrigin;
            _cutWorldOrigin = cutWorldOrigin;
            _width = Math.Max(0, size.X);
            _height = Math.Max(0, size.Y);
            _sourceLookup = sourceLookup;
            _targetLookup = targetLookup;
            _cutLookup = cutLookup;

            _idByRelative = new Dictionary<Point, int>(selectedTiles.Count);
            _relativeById = new Dictionary<int, Point>(selectedTiles.Count);
            _relativeByWorld = new Dictionary<Point, Point>(selectedTiles.Count);
            _selectedTiles = new List<Blueprint.BoundedTile>(selectedTiles.Count);

            for (int i = 0; i < selectedTiles.Count; i++)
            {
                Point relative = selectedTiles[i].Relative;
                if (!IsInBounds(relative) || _idByRelative.ContainsKey(relative))
                    continue;

                int id = RelativeToId(relative);
                Point world = RelativeToWorld(relative);

                _idByRelative[relative] = id;
                _relativeById[id] = relative;
                _relativeByWorld[world] = relative;
                _selectedTiles.Add(new Blueprint.BoundedTile(relative, selected: true));
            }
        }

        public IReadOnlyList<Blueprint.BoundedTile> SelectedTiles => _selectedTiles;

        public PasteLookupQuery CreateQuery(PasteIdentifyChannel identifyChannel) =>
            new(this, identifyChannel);

        public bool IsInBounds(Point relative) =>
            (uint)relative.X < (uint)_width && (uint)relative.Y < (uint)_height;

        public bool IsSelected(Point relative) => _idByRelative.ContainsKey(relative);

        public bool IsInPasteScope(Point relative) => IsSelected(relative);

        public Point RelativeToWorld(Point relative) =>
            new(_worldOrigin.X + relative.X, _worldOrigin.Y + relative.Y);

        public Point RelativeToCutWorld(Point relative) =>
            new(_cutWorldOrigin.X + relative.X, _cutWorldOrigin.Y + relative.Y);

        public bool TryWorldToRelative(Point world, out Point relative)
        {
            relative = new Point(world.X - _worldOrigin.X, world.Y - _worldOrigin.Y);
            return IsInBounds(relative);
        }

        public bool TrySelectedWorldToRelative(Point world, out Point relative) =>
            _relativeByWorld.TryGetValue(world, out relative);

        public bool TryGetId(Point relative, out int id) =>
            _idByRelative.TryGetValue(relative, out id);

        public bool TryGetRelative(int id, out Point relative) =>
            _relativeById.TryGetValue(id, out relative);

        public bool TryGetSource(Point relative, out Tile tile)
        {
            if (!IsSelected(relative))
            {
                tile = default;
                return false;
            }

            return _sourceLookup(relative, out tile);
        }

        public bool TryGetTarget(Point relative, out Tile tile)
        {
            if (!IsSelected(relative))
            {
                tile = default;
                return false;
            }

            return _targetLookup(relative, out tile);
        }

        public bool TryGetIdentify(Point relative, PasteIdentifyChannel channel, out Tile tile)
        {
            if (!IsSelected(relative))
            {
                tile = default;
                return false;
            }

            return channel switch
            {
                PasteIdentifyChannel.Source => _sourceLookup(relative, out tile),
                PasteIdentifyChannel.Target => _targetLookup(relative, out tile),
                PasteIdentifyChannel.CutSource => _cutLookup(relative, out tile),
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown identify channel."),
            };
        }

        public bool TryCreatePasteTile(Point relative, PasteIdentifyChannel channel, out PasteTile pasteTile)
        {
            if (!IsSelected(relative))
            {
                pasteTile = default;
                return false;
            }

            if (channel == PasteIdentifyChannel.CutSource)
            {
                if (!_cutLookup(relative, out Tile cutTargetTile))
                {
                    pasteTile = default;
                    return false;
                }

                pasteTile = new PasteTile(relative, RelativeToCutWorld(relative), default, cutTargetTile);
                return true;
            }

            if (!TryGetSource(relative, out Tile sourceTile) || !TryGetTarget(relative, out Tile targetTile))
            {
                pasteTile = default;
                return false;
            }

            pasteTile = new PasteTile(relative, RelativeToWorld(relative), sourceTile, targetTile);
            return true;
        }

        private int RelativeToId(Point relative)
        {
            checked
            {
                return (relative.X + 1) + (relative.Y * _width);
            }
        }
    }

    internal readonly struct PasteLookupQuery(PasteTileDatabase database, PasteIdentifyChannel identifyChannel)
    {
        public IReadOnlyList<Blueprint.BoundedTile> SelectedTiles => database.SelectedTiles;

        public bool IsInPasteScope(Point relative) => database.IsInPasteScope(relative);

        public bool TryGetSource(Point relative, out Tile tile) =>
            database.TryGetSource(relative, out tile);

        public bool TryGetTarget(Point relative, out Tile tile) =>
            database.TryGetTarget(relative, out tile);

        public bool TryGetIdentify(Point relative, out Tile tile) =>
            database.TryGetIdentify(relative, identifyChannel, out tile);

        public bool TryCreatePasteTile(Point relative, out PasteTile pasteTile) =>
            database.TryCreatePasteTile(relative, identifyChannel, out pasteTile);

        public Point RelativeToWorld(Point relative) =>
            database.RelativeToWorld(relative);

        public bool TryWorldToRelative(Point world, out Point relative) =>
            database.TryWorldToRelative(world, out relative);

        public bool TrySelectedWorldToRelative(Point world, out Point relative) =>
            database.TrySelectedWorldToRelative(world, out relative);

        public bool TryGetId(Point relative, out int id) =>
            database.TryGetId(relative, out id);

        public bool TryGetRelative(int id, out Point relative) =>
            database.TryGetRelative(id, out relative);
    }
}
