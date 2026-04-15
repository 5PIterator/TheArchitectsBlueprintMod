namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    /// <summary>
    /// DTOs for layer-facet payload JSON (schema version 1). Pair with <c>LayerFacetsV1Format.cs</c> for identify/import.
    /// </summary>
    public sealed class PayloadCell
    {
        public int X { get; set; }

        public int Y { get; set; }

        public PayloadFacets? Facets { get; set; }
    }

    public sealed class PayloadFacets
    {
        public WallFacetDto? Wall { get; set; }

        public BlockFacetDto? Block { get; set; }

        public FrameFacetDto? Frame { get; set; }

        public LiquidFacetDto? Liquid { get; set; }

        public PaintFacetDto? Paint { get; set; }

        public WiringFacetDto? Wiring { get; set; }

        public DetailFacetDto? Detail { get; set; }
    }

    public sealed class WallFacetDto
    {
        public ushort Type { get; set; }

        public byte Color { get; set; }

        public int FrameX { get; set; }

        public int FrameY { get; set; }

        public int FrameNumber { get; set; }
    }

    public sealed class BlockFacetDto
    {
        public bool HasTile { get; set; }

        public ushort TileType { get; set; }

        public short FrameX { get; set; }

        public short FrameY { get; set; }

        public int FrameNumber { get; set; }
    }

    /// <summary>Reserved for anchor discrimination metadata; optional in v1.</summary>
    public sealed class FrameFacetDto
    {
        public string? AnchorKind { get; set; }
    }

    public sealed class LiquidFacetDto
    {
        public byte Amount { get; set; }

        public int LiquidType { get; set; }

        public bool SkipLiquid { get; set; }

        public bool CheckingLiquid { get; set; }
    }

    public sealed class PaintFacetDto
    {
        public byte TileColor { get; set; }

        public byte WallColor { get; set; }

        public bool IsTileInvisible { get; set; }

        public bool IsWallInvisible { get; set; }

        public bool IsTileFullbright { get; set; }

        public bool IsWallFullbright { get; set; }
    }

    public sealed class WiringFacetDto
    {
        public bool RedWire { get; set; }

        public bool BlueWire { get; set; }

        public bool GreenWire { get; set; }

        public bool YellowWire { get; set; }

        public bool HasActuator { get; set; }

        public int WireData { get; set; }
    }

    public sealed class DetailFacetDto
    {
        public bool IsActuated { get; set; }

        public byte BlockType { get; set; }

        public byte Slope { get; set; }

        public bool IsHalfBlock { get; set; }

        public int NonFrameBits { get; set; }
    }
}
