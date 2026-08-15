using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using Terraria.DataStructures;
using TheArchitectsBlueprintMod.Utils;
namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Detached tile cell for a projection: boxed <see cref="DetachedTileSnapshot"/> (tModLoader ECS tile slices).
    /// Default / <see cref="IsEmpty"/> matches former <c>PayloadFacets?</c> null (air).
    /// </summary>
    public readonly struct PayloadCell
    {
        public class TileData
        {
            public TileTypeData TileType;
            public WallTypeData WallType;
            public Terraria.LiquidData LiquidData;
            public TileWallBrightnessInvisibilityData TWBIData;
            public TileWallWireStateData TWWStateData;
            /// <summary>Authoritative placement style captured at cell build (drop item placeStyle / TileObjectData).</summary>
            public int TileStyle;
            /// <summary>Placement alternate captured at cell build.</summary>
            public int TileAlternate;
        };
        public readonly struct WallData(TileData? data)
        {
            public ushort Type => data?.WallType.Type ?? 0;
            public int FrameNumber => data?.TWWStateData.WallFrameNumber ?? 0;
            public int FrameX => data?.TWWStateData.WallFrameX ?? 0;
            public int FrameY => data?.TWWStateData.WallFrameY ?? 0;
        };
        public readonly struct BlockData(TileData? data)
        {
            public ushort Type => data?.TileType.Type ?? 0;
            public int FrameNumber => data?.TWWStateData.TileFrameNumber ?? 0;
            public short FrameX => data?.TWWStateData.TileFrameX ?? 0;
            public short FrameY => data?.TWWStateData.TileFrameY ?? 0;
        };
        public readonly struct LiquidData(TileData? data)
        {
            public int Type => data?.LiquidData.LiquidType ?? 0;
            public byte Amount => data?.LiquidData.Amount ?? 0;
            public bool SkipLiquid => data?.LiquidData.SkipLiquid ?? false;
            public bool CheckingLiquid => data?.LiquidData.CheckingLiquid ?? false;
        };
        public readonly struct PaintData(TileData? data)
        {
            public byte TileColor => data?.TWWStateData.TileColor ?? 0;
            public byte WallColor => data?.TWWStateData.WallColor ?? 0;
            public bool TileInvisible => data?.TWBIData.IsTileInvisible ?? false;
            public bool WallInvisible => data?.TWBIData.IsWallInvisible ?? false;
            public bool TileFullbright => data?.TWBIData.IsTileFullbright ?? false;
            public bool WallFullbright => data?.TWBIData.IsWallFullbright ?? false;
        };
        public readonly struct WireData(TileData? data)
        {
            public int Data => data?.TWWStateData.WireData ?? 0;
            public bool HasActuator => data?.TWWStateData.HasActuator ?? false;
            public bool RedWire => data?.TWWStateData.RedWire ?? false;
            public bool BlueWire => data?.TWWStateData.BlueWire ?? false;
            public bool GreenWire => data?.TWWStateData.GreenWire ?? false;
            public bool YellowWire => data?.TWWStateData.YellowWire ?? false;

        };
        public readonly struct DetailData(TileData? data)
        {
            public bool HasTile => data?.TWWStateData.HasTile ?? false;
            public bool IsActuated => data?.TWWStateData.IsActuated ?? false;
            public bool IsHalfBlock => data?.TWWStateData.IsHalfBlock ?? false;
            public int Style => data?.TileStyle ?? 0;
            public int Alternate => data?.TileAlternate ?? 0;
            public SlopeType Slope => data?.TWWStateData.Slope ?? SlopeType.Solid;
            public BlockType BlockType => data?.TWWStateData.BlockType ?? BlockType.Solid;
            public int NonFrameBits => data?.TWWStateData.NonFrameBits ?? 0;
        };

        private readonly TileData? _data;
        internal TileData? Data => _data;
        public WallData Wall => new(_data);
        public BlockData Block => new(_data);
        public LiquidData Liquid => new(_data);
        public PaintData Paint => new(_data);
        public WireData Wire => new(_data);
        public DetailData Detail => new(_data);

        public bool IsEmpty => _data is null;

        /// <summary>True when sparse <c>payload</c> / network encodings would emit a non-empty <c>c</c> body.</summary>
        internal bool HasSparseContent => PayloadTagIO.CellHasBodyContent(this);
        internal PayloadCell(TileData data) => _data = data;

        /// <summary>Creates a non-empty payload cell from detached tile data.</summary>
        internal static PayloadCell FromData(TileData data)
        {
            // --- Preserve the already-detached snapshot object as a payload cell.

            ArgumentNullException.ThrowIfNull(data);
            return new PayloadCell(data);
        }

        /// <summary>
        /// Creates a new <see cref="PayloadCell"/> from a <see cref="Tile"/>.
        /// </summary>
        public static PayloadCell FromTile(Tile tile)
        {
            // --- Snapshot tile slices and resolve placement style without world coordinates.

            return FromTile(tile, x: -1, y: -1);
        }

        /// <summary>
        /// Creates a new <see cref="PayloadCell"/> from a live world tile at the given coordinates.
        /// </summary>
        public static PayloadCell FromTile(Tile tile, int x, int y)
        {
            // --- Snapshot tile slices, then resolve style via world-aware drop placeStyle when possible.

            var data = new TileData
            {
                TileType = tile.Get<TileTypeData>(),
                WallType = tile.Get<WallTypeData>(),
                LiquidData = tile.Get<Terraria.LiquidData>(),
                TWBIData = tile.Get<TileWallBrightnessInvisibilityData>(),
                TWWStateData = tile.Get<TileWallWireStateData>(),
            };
            data.TWWStateData.TileFrameNumber = 0;
            data.TWWStateData.WallFrameNumber = 0;

            // Resolve authoritative placeStyle (avoids treating stateful FrameX as style).
            // Tree wood/palm styles are not stored on the trunk tile; TreeLayer derives them from
            // payload support (or world APIs) at resolve time — never from neighboring world reads here.
            int style = 0, alternate = 0;
            if (tile.HasTile && data.TileType.Type > 0 && data.TileType.Type < TileLoader.TileCount)
                TileBreakDropResolver.ResolvePlacementVariant(tile, x, y, out style, out alternate);

            data.TileStyle = style;
            data.TileAlternate = alternate;
            PayloadCell cell = new(data);
            return cell.HasSparseContent ? cell : default;
        }

        /// <summary>
        /// Creates a new <see cref="PayloadCell"/> from a JSON string (schema-2 tile snapshot).
        /// </summary>
        public static PayloadCell FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON is null or empty", nameof(json));

            var data = JsonConvert.DeserializeObject<TileData>(json)
                ?? throw new JsonException("Invalid tile snapshot JSON");
            return new PayloadCell(data);
        }

        /// <summary>
        /// Creates a Tile data structure from a PayloadCell. Empty cells materialize as a fresh air tile (<see cref="Tile()"/>).
        /// !Important: The returned Tile is a temporary tile, and will be overridden by the next call to ToTile().
        /// For world placement, prefer <see cref="CopyToWorldTile"/> instead of <see cref="Tile.CopyFrom"/>.
        /// </summary>
        /// <param name="cell"></param>
        /// <returns>A Tile data structure from a PayloadCell.</returns>
        public Tile ToTile()
        {
            if (IsEmpty)
                return new Tile();
            Tile t = new();
            CopyTileDataOnto(t);
            return t;
        }

        /// <summary>
        /// Writes this cell's detached snapshot onto a live world tile at <paramref name="x"/>, <paramref name="y"/>.
        /// Empty cells clear the destination to vacant (block and wall). Avoids the shared <see cref="ToTile"/> scratch buffer.
        /// </summary>
        public void CopyToWorldTile(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return;

            Tile dest = Main.tile[x, y];
            CopyTileDataOnto(dest);
        }

        private void CopyTileDataOnto(Tile dest)
        {
            if (IsEmpty)
            {
                dest.Clear(TileDataType.All);
                return;
            }

            dest.Get<TileTypeData>() = _data.TileType;
            dest.Get<WallTypeData>() = _data.WallType;
            dest.Get<Terraria.LiquidData>() = _data.LiquidData;
            dest.Get<TileWallBrightnessInvisibilityData>() = _data.TWBIData;
            TileWallWireStateData state = _data.TWWStateData;
            state.TileFrameNumber = 0;
            state.WallFrameNumber = 0;
            dest.Get<TileWallWireStateData>() = state;
        }

        /// <summary>
        /// Converts the <see cref="PayloadCell"/> to a JSON string.
        /// </summary>
        public string ToJson()
        {
            if (_data is not TileData data)
                throw new InvalidOperationException("PayloadCell is empty");
            return JsonConvert.SerializeObject(new
            {
                data.TileType,
                data.WallType,
                data.LiquidData,
                data.TWBIData,
                data.TWWStateData,
                data.TileStyle,
                data.TileAlternate,
            });
        }

        public bool HasTile() => !IsEmpty && Detail.HasTile;

        /// <summary>Returns the placement style stored on this payload cell at capture/import time.</summary>
        public int ResolvePlacementStyle()
        {
            // --- Use the cached style field; do not re-derive from sprite frames.

            return _data?.TileStyle ?? 0;
        }

        /// <summary>Copies cached style/alternate from <see cref="FromTile"/> / <see cref="FromJson"/>.</summary>
        public void GetTileInfo(ref int style, ref int alternate)
        {
            if (_data is null)
            {
                style = 0;
                alternate = 0;
                return;
            }

            alternate = _data.TileAlternate;
            style = _data.TileStyle;
        }
    }

    /// <summary>
    /// Immutable sparse list of detached tile snapshots (<see cref="PayloadCell"/>) with document bounds.
    /// New state is always produced via <see cref="Empty"/>, <see cref="FromWorld"/>, <see cref="FromSparse"/>, or <see cref="TryFromTagDocument"/>.
    /// </summary>
    public sealed class Payload
    {
        /// <summary>One non-air cell placement in a sparse or network decode step.</summary>
        public readonly struct Entry(Point relative, PayloadCell cell)
        {
            public Point Relative { get; } = relative;
            public PayloadCell Cell { get; } = cell;
        }

        /// <summary>Zero-sized payload (no grid).</summary>
        public static Payload Empty { get; } = new(0, 0, Array.Empty<Entry>());

        private readonly int _width;
        private readonly int _height;
        private readonly Entry[] _entries;
        private Dictionary<Point, PayloadCell>? _cellIndex;

        private Payload(int width, int height, Entry[] entries)
        {
            _width = width;
            _height = height;
            _entries = entries;
        }

        /// <summary>Non-empty cells with relative coordinates.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Non-empty cell bodies only (no coordinates).</summary>
        public IEnumerable<PayloadCell> Cells
        {
            get
            {
                for (int i = 0; i < _entries.Length; i++)
                    yield return _entries[i].Cell;
            }
        }

        public Point Size => IsDefined ? new Point(_width, _height) : Point.Zero;
        public bool IsDefined => _width > 0 && _height > 0;

        public int Width => _width;
        public int Height => _height;

        /// <summary>Captures live world tiles under <paramref name="worldBounds"/>.</summary>
        public static Payload FromWorld(Rectangle worldBounds)
        {
            if (worldBounds.IsEmpty)
                return Empty;

            int w = worldBounds.Width;
            int h = worldBounds.Height;
            var entries = new List<Entry>();
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int wx = worldBounds.X + x;
                    int wy = worldBounds.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                        continue;

                    PayloadCell cell = PayloadCell.FromTile(Framing.GetTileSafely(wx, wy), wx, wy);
                    if (cell.HasSparseContent)
                        entries.Add(new Entry(new Point(x, y), cell));
                }
            }

            return new Payload(w, h, entries.ToArray());
        }

        /// <summary>Builds a sparse payload from relative placements within <paramref name="width"/>×<paramref name="height"/> bounds.</summary>
        public static Payload FromSparse(IReadOnlyList<Entry> entries, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return Empty;

            if (entries is null || entries.Count == 0)
                return new Payload(width, height, Array.Empty<Entry>());

            var filtered = new List<Entry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                Point rel = e.Relative;
                if ((uint)rel.X >= (uint)width || (uint)rel.Y >= (uint)height)
                    continue;
                if (!e.Cell.HasSparseContent)
                    continue;
                filtered.Add(e);
            }

            return new Payload(width, height, filtered.ToArray());
        }

        /// <summary>
        /// Decodes a sparse payload: width, height, count, then (x, y, cellTagBytes) tuples.
        /// </summary>
        public static Payload FromNetwork(BinaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int count = reader.ReadInt32();
            if (width <= 0 || height <= 0 || count < 0)
                return Empty;

            var entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int byteLen = reader.ReadInt32();
                if (byteLen < 0 || byteLen > 1024 * 1024)
                    continue;

                byte[] cellBytes = reader.ReadBytes(byteLen);
                if (!PayloadTagIO.TryCellFromBytes(cellBytes, out PayloadCell cell) || !cell.HasSparseContent)
                    continue;

                entries.Add(new Entry(new Point(x, y), cell));
            }

            return FromSparse(entries, width, height);
        }

        /// <summary>Loads a payload from a schema 3–4 TagCompound document root.</summary>
        public static bool TryFromTagDocument(TagCompound root, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            return PayloadTagIO.TryReadDocument(root, out payload, out info, out error);
        }

        /// <summary>Independent copy of <paramref name="other"/>.</summary>
        public static Payload FromClone(Payload other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (!other.IsDefined)
                return Empty;

            var copy = new Entry[other._entries.Length];
            Array.Copy(other._entries, copy, other._entries.Length);
            return new Payload(other._width, other._height, copy);
        }

        /// <summary>Snapshot for one world <see cref="Tile"/> (air omitted).</summary>
        public static PayloadCell CellFromTile(Tile tile) => PayloadCell.FromTile(tile);

        /// <summary>
        /// Encodes a payload: width, height, count, then (x, y, cellTagBytes) tuples for non-air cells.
        /// </summary>
        public void ToNetwork(BinaryWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write(_width);
            writer.Write(_height);
            if (!IsDefined)
            {
                writer.Write(0);
                return;
            }

            writer.Write(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry entry = _entries[i];
                Point rel = entry.Relative;
                byte[] cellBytes = PayloadTagIO.CellTagToBytes(PayloadTagIO.CellBodyToTag(entry.Cell));
                writer.Write(rel.X);
                writer.Write(rel.Y);
                writer.Write(cellBytes.Length);
                writer.Write(cellBytes);
            }
        }

        /// <summary>
        /// Returns a cell at <paramref name="relative"/> when inside payload bounds.
        /// Sparse omissions materialize as <see cref="PayloadCell.IsEmpty"/> (air).
        /// </summary>
        public bool TryGetCell(Point relative, out PayloadCell cell)
        {
            if (!IsDefined || (uint)relative.X >= (uint)_width || (uint)relative.Y >= (uint)_height)
            {
                cell = default;
                return false;
            }

            EnsureCellIndex();
            if (_cellIndex!.TryGetValue(relative, out cell))
                return true;

            // --- In-bounds sparse miss: implicit air (default PayloadCell).

            cell = default;
            return true;
        }

        private void EnsureCellIndex()
        {
            if (_cellIndex is not null)
                return;

            _cellIndex = new Dictionary<Point, PayloadCell>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry entry = _entries[i];
                _cellIndex[entry.Relative] = entry.Cell;
            }
        }
    }
}
