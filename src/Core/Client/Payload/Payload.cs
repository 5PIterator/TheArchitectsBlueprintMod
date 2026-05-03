using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace ArchitectsStencilMod.Core
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
            /// <summary><see cref="TileObjectData.GetTileInfo(Tile, ref int, ref int)"/> style, captured at cell build.</summary>
            public int TileStyle;
            /// <summary><see cref="TileObjectData.GetTileInfo(Tile, ref int, ref int)"/> alternate, captured at cell build.</summary>
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
            var data = new TileData
            {
                TileType = tile.Get<TileTypeData>(),
                WallType = tile.Get<WallTypeData>(),
                LiquidData = tile.Get<Terraria.LiquidData>(),
                TWBIData = tile.Get<TileWallBrightnessInvisibilityData>(),
                TWWStateData = tile.Get<TileWallWireStateData>(),
            };
            int style = 0, alternate = 0;
            if (tile.HasTile && data.TileType.Type > 0 && data.TileType.Type < TileLoader.TileCount)
                TileObjectData.GetTileInfo(tile, ref style, ref alternate);
            data.TileStyle = style;
            data.TileAlternate = alternate;
            return new PayloadCell(data);
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
        /// Creates a Tile data structure from a PayloadCell.
        /// !Important: The returned Tile is a temporary tile, and will be overriddden by the next call to ToTile().
        /// </summary>
        /// <param name="cell"></param>
        /// <returns>A Tile data structure from a PayloadCell.</returns>
        public Tile ToTile()
        {
            if (IsEmpty)
                throw new InvalidOperationException("PayloadCell is empty");
            Tile t = new();
            t.Get<TileTypeData>() = _data.TileType;
            t.Get<WallTypeData>() = _data.WallType;
            t.Get<Terraria.LiquidData>() = _data.LiquidData;
            t.Get<TileWallBrightnessInvisibilityData>() = _data.TWBIData;
            t.Get<TileWallWireStateData>() = _data.TWWStateData;
            return t;
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

        /// <summary>Copies cached style/alternate from <see cref="FromTile"/> / <see cref="FromJson"/>.</summary>
        public void GetTileInfo(ref int style, ref int alternate)
        {
            if (_data is null)
            {
                style = 0;
                alternate = 0;
                return;
            }

            style = _data.TileStyle;
            alternate = _data.TileAlternate;
        }
    }

    /// <summary>
    /// Immutable grid of detached tile snapshots (<see cref="PayloadCell"/>), stored as a 2D array <c>[x, y]</c>.
    /// New state is always produced via <see cref="Empty"/>, <see cref="FromWorld"/>, <see cref="FromSparse"/>, or <see cref="FromDocument"/>.
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
        public static Payload Empty { get; } = new(0, 0, new PayloadCell[0, 0]);

        private readonly PayloadCell[,] _cells;
        private readonly int _width;
        private readonly int _height;

        private Payload(int width, int height, PayloadCell[,] cells)
        {
            _width = width;
            _height = height;
            _cells = cells;
        }

        /// <summary>
        /// All cells in C# multidimensional order: first index (width) slowest, second (height) fastest — same as <c>foreach</c> on the backing 2D array.
        /// </summary>
        public IEnumerable<PayloadCell> Cells => EnumerateCells();

        public Point Size => IsDefined ? new Point(_width, _height) : Point.Zero;
        public bool IsDefined => _width > 0 && _height > 0;

        public int Width => _width;
        public int Height => _height;

        private IEnumerable<PayloadCell> EnumerateCells()
        {
            if (!IsDefined)
                yield break;
            foreach (PayloadCell cell in _cells)
                yield return cell;
        }

        /// <summary>Captures live world tiles under <paramref name="worldBounds"/>.</summary>
        public static Payload FromWorld(Rectangle worldBounds)
        {
            if (worldBounds.IsEmpty)
                return Empty;

            int w = worldBounds.Width;
            int h = worldBounds.Height;
            var cells = new PayloadCell[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int wx = worldBounds.X + x;
                    int wy = worldBounds.Y + y;
                    if (!WorldGen.InWorld(wx, wy))
                        continue;

                    cells[x, y] = PayloadCell.FromTile(Framing.GetTileSafely(wx, wy));
                }
            }

            return new Payload(w, h, cells);
        }

        /// <summary>
        /// Merges sparse entries into a dense <paramref name="width"/>×<paramref name="height"/> grid (unset cells stay air).
        /// </summary>
        public static Payload FromSparse(IReadOnlyList<Entry> entries, int width, int height)
        {
            if (entries is null || width <= 0 || height <= 0)
                return Empty;

            var cells = new PayloadCell[width, height];
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                Point rel = e.Relative;
                if ((uint)rel.X >= (uint)width || (uint)rel.Y >= (uint)height)
                    continue;
                cells[rel.X, rel.Y] = e.Cell;
            }

            return new Payload(width, height, cells);
        }

        /// <summary>Loads a payload from a raw JSON 2D tile-data array.</summary>
        public static Payload FromJson(string json)
        {
            // --- Deserialize the raw grid, then convert each non-null tile snapshot into a cell.

            if (string.IsNullOrWhiteSpace(json))
                return Empty;

            PayloadCell.TileData?[,]? dataGrid = JsonConvert.DeserializeObject<PayloadCell.TileData?[,]>(json);
            if (dataGrid is null)
                return Empty;

            int width = dataGrid.GetLength(0);
            int height = dataGrid.GetLength(1);
            if (width <= 0 || height <= 0)
                return Empty;

            var cells = new PayloadCell[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    PayloadCell.TileData? data = dataGrid[x, y];
                    if (data is not null)
                        cells[x, y] = PayloadCell.FromData(data);
                }
            }

            return new Payload(width, height, cells);
        }

        /// <summary>
        /// Decodes a sparse payload: width, height, count, then (x, y, json) tuples.
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
                string json = reader.ReadString();
                PayloadCell cell = PayloadCell.FromJson(json);
                if (!cell.IsEmpty)
                    entries.Add(new Entry(new Point(x, y), cell));
            }

            return FromSparse(entries, width, height);
        }

        /// <summary>Independent copy of <paramref name="other"/> (same cells; new 2D array).</summary>
        public static Payload FromClone(Payload other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (!other.IsDefined)
                return Empty;

            var cells = new PayloadCell[other._width, other._height];
            Array.Copy(other._cells, cells, other._cells.Length);

            return new Payload(other._width, other._height, cells);
        }

        /// <summary>Snapshot for one world <see cref="Tile"/> (air omitted).</summary>
        public static PayloadCell CellFromTile(Tile tile) => PayloadCell.FromTile(tile);

        /// <summary>
        /// Encodes a payload: width, height, count, then (x, y, json) tuples for non-air cells.
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

            int nonEmpty = 0;
            foreach (PayloadCell cell in _cells)
            {
                if (!cell.IsEmpty)
                    nonEmpty++;
            }

            writer.Write(nonEmpty);
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    PayloadCell cell = _cells[x, y];
                    if (cell.IsEmpty)
                        continue;

                    writer.Write(x);
                    writer.Write(y);
                    writer.Write(cell.ToJson());
                }
            }
        }

        /// <summary>Returns a JSON string of raw 2D tile-data snapshots.</summary>
        public string ToJson()
        {
            // --- Project cells to nullable tile data so empty cells remain explicit JSON nulls.

            if (!IsDefined)
                return JsonConvert.SerializeObject(new PayloadCell.TileData?[0, 0]);

            var dataGrid = new PayloadCell.TileData?[_width, _height];
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                    dataGrid[x, y] = _cells[x, y].Data;
            }

            return JsonConvert.SerializeObject(dataGrid);
        }

        public bool TryGetCell(Point relative, out PayloadCell cell)
        {
            if (!IsDefined || (uint)relative.X >= (uint)_width || (uint)relative.Y >= (uint)_height)
            {
                cell = default;
                return false;
            }

            cell = _cells[relative.X, relative.Y];
            return true;
        }
    }
}
