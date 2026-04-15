namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    /// <summary>
    /// DTOs for CheatSheet 2D array JSON (implicit schema 0; no schema field). Pair with <c>CheatSheetFormat.cs</c> for identify/import.
    /// </summary>
    public sealed class CheatSheetCellDto
    {
        public CheatSheetTileTypeData? TileTypeData { get; set; }

        public CheatSheetWallTypeData? WallTypeData { get; set; }

        public CheatSheetTileWallWireStateData? TileWallWireStateData { get; set; }

        public CheatSheetLiquidData? LiquidData { get; set; }

        public CheatSheetTileWallBrightnessInvisibilityData? TileWallBrightnessInvisibilityData { get; set; }
    }

    public sealed class CheatSheetTileTypeData
    {
        public ushort Type { get; set; }
    }

    public sealed class CheatSheetWallTypeData
    {
        public ushort Type { get; set; }
    }

    public sealed class CheatSheetTileWallWireStateData
    {
        public short TileFrameX { get; set; }

        public short TileFrameY { get; set; }

        public bool HasTile { get; set; }

        public bool IsActuated { get; set; }

        public bool HasActuator { get; set; }

        public byte TileColor { get; set; }

        public byte WallColor { get; set; }

        public int TileFrameNumber { get; set; }

        public int WallFrameNumber { get; set; }

        public int WallFrameX { get; set; }

        public int WallFrameY { get; set; }

        public bool IsHalfBlock { get; set; }

        public byte Slope { get; set; }

        public int WireData { get; set; }

        public bool RedWire { get; set; }

        public bool BlueWire { get; set; }

        public bool GreenWire { get; set; }

        public bool YellowWire { get; set; }

        public int NonFrameBits { get; set; }

        public byte BlockType { get; set; }
    }

    public sealed class CheatSheetLiquidData
    {
        public byte Amount { get; set; }

        public int LiquidType { get; set; }

        public bool SkipLiquid { get; set; }

        public bool CheckingLiquid { get; set; }
    }

    public sealed class CheatSheetTileWallBrightnessInvisibilityData
    {
        public int Data { get; set; }

        public bool IsTileInvisible { get; set; }

        public bool IsWallInvisible { get; set; }

        public bool IsTileFullbright { get; set; }

        public bool IsWallFullbright { get; set; }
    }
}
