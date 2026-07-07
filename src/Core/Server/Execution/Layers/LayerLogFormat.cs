using System.Text;
using Terraria;
using Terraria.Enums;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>Formats layer log content for Identify, Clear, and Place results.</summary>
    internal static class LayerLogFormat
    {
        public static string FormatTile(in Tile tile) =>
            tile.HasTile
                ? FormatTileType(tile.TileType)
                : FormatContentDisplayName("Air");

        public static string FormatTileType(int tileType) =>
            NormalizeContentName(ContentNameResolver.GetTileName(tileType));

        /// <summary>Formats payload block intent without the shared <see cref="PayloadCell.ToTile"/> scratch buffer.</summary>
        public static string FormatPayloadTile(in PasteTile tile) =>
            tile.PayloadCell.HasTile()
                ? FormatTileType(tile.PayloadCell.Block.Type)
                : FormatFromSubject(in tile, PasteSubject.Payload);

        public static string FormatWall(in Tile tile) =>
            tile.WallType != WallID.None
                ? NormalizeContentName(ContentNameResolver.GetWallName(tile.WallType))
                : FormatContentDisplayName("Air");

        public static string FormatLiquid(in Tile tile) =>
            tile.LiquidAmount > 0
                ? NormalizeContentName(ContentNameResolver.GetLiquidTypeName(tile.LiquidType))
                : FormatContentDisplayName("Air");

        public static string FormatFromSubject(in PasteTile tile, PasteSubject subject) =>
            FormatTile(PasteTile.GetTileForIdentify(in tile, subject));

        public static string FormatContentDisplayName(string? rawName) =>
            ContentNameResolver.FormatContentDisplayName(rawName);

        public static string NormalizeContentName(string rawName) =>
            ContentNameResolver.FormatContentDisplayName(rawName);

        public static string ClearedLabel => "Cleared";

        public static string PlacedLabel(in PasteContext context) =>
            context.UseReplacePlace ? "Replaced" : "Placed";

        public static string WiredLabel => "Wired";

        public static string DetailedLabel => "Detailed";

        public static string PaintedLabel => "Painted";

        /// <summary>Formats wire colors and actuator state for progress and debug logs.</summary>
        public static string FormatWireState(in PayloadCell.WireData wire)
        {
            if (!wire.RedWire && !wire.BlueWire && !wire.GreenWire && !wire.YellowWire && !wire.HasActuator)
                return "Wire:none";

            var parts = new StringBuilder(24);
            if (wire.RedWire) parts.Append(parts.Length > 0 ? "+R" : "R");
            if (wire.BlueWire) parts.Append(parts.Length > 0 ? "+B" : "B");
            if (wire.GreenWire) parts.Append(parts.Length > 0 ? "+G" : "G");
            if (wire.YellowWire) parts.Append(parts.Length > 0 ? "+Y" : "Y");
            if (wire.HasActuator) parts.Append(parts.Length > 0 ? "+Actuator" : "Actuator");
            return $"Wire:{parts}";
        }

        /// <summary>Formats wire colors and actuator state on a <see cref="Tile"/> snapshot.</summary>
        public static string FormatWireState(in Tile tile)
        {
            if (!tile.RedWire && !tile.BlueWire && !tile.GreenWire && !tile.YellowWire && !tile.HasActuator)
                return "Wire:none";

            var parts = new StringBuilder(24);
            if (tile.RedWire) parts.Append(parts.Length > 0 ? "+R" : "R");
            if (tile.BlueWire) parts.Append(parts.Length > 0 ? "+B" : "B");
            if (tile.GreenWire) parts.Append(parts.Length > 0 ? "+G" : "G");
            if (tile.YellowWire) parts.Append(parts.Length > 0 ? "+Y" : "Y");
            if (tile.HasActuator) parts.Append(parts.Length > 0 ? "+Actuator" : "Actuator");
            return $"Wire:{parts}";
        }

        /// <summary>Formats live world wire state at <paramref name="x"/>, <paramref name="y"/>.</summary>
        public static string FormatWireStateAt(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return "Wire:?";

            return FormatWireState(Main.tile[x, y]);
        }

        /// <summary>Formats slope, half-block, and actuation detail for progress and debug logs.</summary>
        public static string FormatDetailState(in PayloadCell.DetailData detail)
        {
            var parts = new StringBuilder(32);
            AppendDetailParts(parts, detail.BlockType, detail.Slope, detail.IsHalfBlock, detail.IsActuated);
            return parts.Length == 0 ? "Detail:none" : $"Detail:{parts}";
        }

        /// <summary>Formats slope, half-block, and actuation detail on a <see cref="Tile"/> snapshot.</summary>
        public static string FormatDetailState(in Tile tile)
        {
            var parts = new StringBuilder(32);
            AppendDetailParts(parts, tile.BlockType, tile.Slope, tile.IsHalfBlock, tile.IsActuated);
            return parts.Length == 0 ? "Detail:none" : $"Detail:{parts}";
        }

        /// <summary>Formats live world detail state at <paramref name="x"/>, <paramref name="y"/>.</summary>
        public static string FormatDetailStateAt(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return "Detail:?";

            return FormatDetailState(Main.tile[x, y]);
        }

        /// <summary>Formats tile and wall paint for progress and debug logs.</summary>
        public static string FormatPaintState(in PayloadCell cell)
        {
            var parts = new StringBuilder(64);
            AppendPaintSurface(parts, in cell);
            AppendPaintColors(parts, cell.Paint.TileColor, cell.Paint.WallColor);
            AppendPaintCoatings(
                parts,
                cell.Paint.TileInvisible,
                cell.Paint.WallInvisible,
                cell.Paint.TileFullbright,
                cell.Paint.WallFullbright);
            return parts.Length == 0 ? "Paint:none" : $"Paint:{parts}";
        }

        /// <summary>Formats tile and wall paint on a <see cref="Tile"/> snapshot.</summary>
        public static string FormatPaintState(in Tile tile)
        {
            var parts = new StringBuilder(64);
            AppendPaintSurface(parts, in tile);
            AppendPaintColors(parts, tile.TileColor, tile.WallColor);
            AppendPaintCoatings(parts, tile.IsTileInvisible, tile.IsWallInvisible, tile.IsTileFullbright, tile.IsWallFullbright);
            return parts.Length == 0 ? "Paint:none" : $"Paint:{parts}";
        }

        /// <summary>Formats live world paint state at <paramref name="x"/>, <paramref name="y"/>.</summary>
        public static string FormatPaintStateAt(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return "Paint:?";

            return FormatPaintState(Main.tile[x, y]);
        }

        /// <summary>Describes sweep payload intent for logging.</summary>
        public static string FormatSweepPayload(in PasteTile tile)
        {
            if (tile.PayloadCell.IsEmpty)
                return FormatContentDisplayName("Air");
            if (tile.PayloadCell.HasTile())
                return FormatTileType(tile.PayloadCell.Block.Type);
            if (tile.PayloadCell.Wall.Type != WallID.None)
                return NormalizeContentName(ContentNameResolver.GetWallName(tile.PayloadCell.Wall.Type));
            if (tile.PayloadCell.Liquid.Amount > 0)
                return NormalizeContentName(ContentNameResolver.GetLiquidTypeName(tile.PayloadCell.Liquid.Type));
            return FormatContentDisplayName(null);
        }

        private static void AppendDetailParts(
            StringBuilder parts,
            BlockType blockType,
            SlopeType slope,
            bool isHalfBlock,
            bool isActuated)
        {
            if (blockType != BlockType.Solid)
                AppendDetailLabel(parts, blockType.ToString());
            else if (slope != SlopeType.Solid)
                AppendDetailLabel(parts, slope.ToString());

            if (isHalfBlock && blockType != BlockType.HalfBlock)
                AppendDetailLabel(parts, "HalfBlock");

            if (isActuated)
                AppendDetailLabel(parts, "Actuated");
        }

        private static void AppendDetailLabel(StringBuilder parts, string label)
        {
            if (parts.Length > 0)
                parts.Append('+');
            parts.Append(label);
        }

        private static void AppendPaintSurface(StringBuilder parts, in PayloadCell cell)
        {
            if (cell.HasTile())
                AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetTileName(cell.Block.Type)));
            else if (cell.Wall.Type != WallID.None)
                AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetWallName(cell.Wall.Type)));
        }

        private static void AppendPaintSurface(StringBuilder parts, in Tile tile)
        {
            if (tile.HasTile)
                AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetTileName(tile.TileType)));
            else if (tile.WallType != WallID.None)
                AppendDetailLabel(parts, NormalizeSurfaceName(ContentNameResolver.GetWallName(tile.WallType)));
        }

        private static void AppendPaintColors(StringBuilder parts, byte tileColor, byte wallColor)
        {
            if (tileColor != PaintID.None)
                AppendDetailLabel(parts, $"Tile={ContentNameResolver.GetPaintName(tileColor)}");
            if (wallColor != PaintID.None)
                AppendDetailLabel(parts, $"Wall={ContentNameResolver.GetPaintName(wallColor)}");
        }

        private static void AppendPaintCoatings(
            StringBuilder parts,
            bool tileInvisible,
            bool wallInvisible,
            bool tileFullbright,
            bool wallFullbright)
        {
            if (tileInvisible)
                AppendDetailLabel(parts, "TileCoating=Echo");
            if (wallInvisible)
                AppendDetailLabel(parts, "WallCoating=Echo");
            if (tileFullbright)
                AppendDetailLabel(parts, "TileCoating=Illuminant");
            if (wallFullbright)
                AppendDetailLabel(parts, "WallCoating=Illuminant");
        }

        private static string NormalizeSurfaceName(string rawName) =>
            ContentNameResolver.FormatContentDisplayName(rawName);
    }
}
