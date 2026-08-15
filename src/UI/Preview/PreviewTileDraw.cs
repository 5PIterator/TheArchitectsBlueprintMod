using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.UI.Preview
{
    /// <summary>Shared payload-tile materialization and source-rect resolution for preview layers.</summary>
    internal static class PreviewTileDraw
    {
        private const int TreeWoodColumnWidth = 176;
        private const int PalmBiomeRowHeight = 22;
        private const int TreeTileSize = 20;

        private static FieldInfo? _paintSystemField;
        private static TilePaintSystemV2? _paintSystem;

        /// <summary>Sanitized tile plus draw metadata prepared from a payload cell.</summary>
        internal readonly struct PreparedTile
        {
            public required Tile Tile { get; init; }
            public required ushort TileType { get; init; }
            public required TileDrawing Renderer { get; init; }
        }

        /// <summary>Resolved sprite batch inputs for a single preview tile draw.</summary>
        internal readonly struct DrawSprite
        {
            public required Texture2D Texture { get; init; }
            public required Rectangle Source { get; init; }
            public required SpriteEffects Effects { get; init; }
            public required int TileWidth { get; init; }
            public required int TileHeight { get; init; }
            public required int TileTop { get; init; }
            public required int HalfBrickHeight { get; init; }
        }

        /// <summary>Materializes a payload cell as a wall-scrubbed tile ready for preview drawing.</summary>
        public static bool TryPrepare(PayloadCell cell, out PreparedTile prepared)
        {
            // --- Build a visible tile snapshot and strip wall frame/type state before any draw API use.

            prepared = default;
            if (!cell.Detail.HasTile)
                return false;

            Tile tile = cell.ToTile();
            if (!TileDrawing.IsVisible(tile))
                return false;

            ushort tileType = cell.Block.Type;
            Main.instance.LoadTiles(tileType);

            // Preview tile draw data must not depend on wall animation or wall type state.
            TileWallWireStateData state = tile.Get<TileWallWireStateData>();
            int tileFrameNumber = state.TileFrameNumber;
            state.WallFrameX = 0;
            state.WallFrameY = 0;
            state.WallFrameNumber = 0;
            state.TileFrameNumber = tileFrameNumber;
            tile.Get<TileWallWireStateData>() = state;
            tile.Get<WallTypeData>() = default;

            prepared = new PreparedTile
            {
                Tile = tile,
                TileType = tileType,
                Renderer = Main.instance.TilesRenderer,
            };
            return true;
        }

        /// <summary>Resolves the minecart-track source rectangle without generic tile animation offsets.</summary>
        public static bool TryResolveMinecartTrack(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Use Minecart.GetSourceRect with the stored frame ID and track-only animation frame.

            sprite = default;
            if (prepared.TileType != TileID.MinecartTrack)
                return false;

            // Track junction/style is encoded in FrameX as a frame ID, not a pixel offset.
            int frameId = prepared.Tile.TileFrameX;
            int animationFrame = Main.tileFrame[TileID.MinecartTrack];
            Rectangle source = Minecart.GetSourceRect(frameId, animationFrame);
            if (source.Width <= 0 || source.Height <= 0)
                return false;

            GetSafePreviewCoords(out int safeX, out int safeY);
            Texture2D texture = prepared.Renderer.GetTileDrawTexture(prepared.Tile, safeX, safeY)
                ?? TextureAssets.Tile[TileID.MinecartTrack].Value;

            sprite = new DrawSprite
            {
                Texture = texture,
                Source = source,
                Effects = SpriteEffects.None,
                TileWidth = PreviewDrawContext.TilePixelSize,
                TileHeight = PreviewDrawContext.TilePixelSize,
                TileTop = 0,
                HalfBrickHeight = 0,
            };
            return true;
        }

        /// <summary>Resolves generic tile draw data for non-track tiles via Terraria's tile renderer.</summary>
        public static bool TryResolveGeneric(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Query GetTileDrawData at safe in-world coords, then build the source rect from its outputs.

            sprite = default;

            // Tree and palm biome columns are resolved from payload soil, not live Main.tile.
            if (prepared.TileType == TileID.Trees || prepared.TileType == TileID.PalmTree)
                return false;

            // Prefer the dedicated track path; generic animation offsets are wrong for FrameX frame IDs.
            if (prepared.TileType == TileID.MinecartTrack)
                return false;

            // GetTileDrawData probes neighbors through WorldGen.SolidTile; origin (0,0) is out of bounds.
            GetSafePreviewCoords(out int payloadX, out int payloadY);

            short frameX = prepared.Tile.TileFrameX;
            short frameY = prepared.Tile.TileFrameY;
            try
            {
                prepared.Renderer.GetTileDrawData(
                    payloadX,
                    payloadY,
                    prepared.Tile,
                    prepared.TileType,
                    ref frameX,
                    ref frameY,
                    out int tileWidth,
                    out int tileHeight,
                    out int tileTop,
                    out int halfBrickHeight,
                    out int addFrX,
                    out int addFrY,
                    out SpriteEffects effects,
                    out Texture2D glowTexture,
                    out Rectangle glowSourceRect,
                    out Color glowColor);

                Texture2D texture = prepared.Renderer.GetTileDrawTexture(prepared.Tile, payloadX, payloadY);
                if (texture is null)
                    return false;

                sprite = new DrawSprite
                {
                    Texture = texture,
                    Source = new Rectangle(frameX + addFrX, frameY + addFrY, tileWidth, tileHeight - halfBrickHeight),
                    Effects = effects,
                    TileWidth = tileWidth,
                    TileHeight = tileHeight,
                    TileTop = tileTop,
                    HalfBrickHeight = halfBrickHeight,
                };
                return true;
            }
            catch
            {
                // Fall back to the payload frame when vanilla draw-data probing still fails.
                return TryResolveRawFrame(in prepared, out sprite);
            }
        }

        /// <summary>Builds a basic 16x16 sprite from the payload tile frame without vanilla neighbor probes.</summary>
        private static bool TryResolveRawFrame(in PreparedTile prepared, out DrawSprite sprite)
        {
            // --- Use stored FrameX/FrameY and the tile atlas when GetTileDrawData cannot run safely.

            sprite = default;
            Main.instance.LoadTiles(prepared.TileType);
            Texture2D texture = TextureAssets.Tile[prepared.TileType].Value;
            if (texture is null)
                return false;

            sprite = new DrawSprite
            {
                Texture = texture,
                Source = new Rectangle(
                    prepared.Tile.TileFrameX,
                    prepared.Tile.TileFrameY,
                    PreviewDrawContext.TilePixelSize,
                    PreviewDrawContext.TilePixelSize),
                Effects = SpriteEffects.None,
                TileWidth = PreviewDrawContext.TilePixelSize,
                TileHeight = PreviewDrawContext.TilePixelSize,
                TileTop = 0,
                HalfBrickHeight = 0,
            };
            return true;
        }

        /// <summary>Picks in-world coordinates with neighbor margin for vanilla tile draw APIs.</summary>
        private static void GetSafePreviewCoords(out int x, out int y)
        {
            // --- Stay away from world edges so SolidTile neighbor checks stay in-range.

            const int margin = 2;
            int maxX = System.Math.Max(margin, Main.maxTilesX - 1 - margin);
            int maxY = System.Math.Max(margin, Main.maxTilesY - 1 - margin);
            x = System.Math.Clamp(Main.maxTilesX / 2, margin, maxX);
            y = System.Math.Clamp(Main.maxTilesY / 2, margin, maxY);
        }

        /// <summary>Chooses tree-wood, track, or generic source rectangle for a prepared payload tile.</summary>
        public static bool TryResolve(
            in PreparedTile prepared,
            in PreviewDrawContext ctx,
            int relX,
            int relY,
            out DrawSprite sprite)
        {
            // --- Prefer payload-soil tree columns, then minecart tracks, otherwise generic draw data.

            if (TryResolveTreeWood(in prepared, in ctx, relX, relY, out sprite))
                return true;

            if (TryResolveMinecartTrack(in prepared, out sprite))
                return true;

            return TryResolveGeneric(in prepared, out sprite);
        }

        /// <summary>Resolves Tiles_5 / palm wood from payload soil instead of GetTreeBiome world walks.</summary>
        private static bool TryResolveTreeWood(
            in PreparedTile prepared,
            in PreviewDrawContext ctx,
            int relX,
            int relY,
            out DrawSprite sprite)
        {
            // --- Apply vanilla 176px biome columns / 22px palm rows using payload support soil.

            sprite = default;
            ushort tileType = prepared.TileType;
            if (tileType != TileID.Trees && tileType != TileID.PalmTree)
                return false;

            TreePreviewResolve.Style style = TreePreviewResolve.Resolve(
                in ctx,
                tileType,
                prepared.Tile.TileFrameX,
                prepared.Tile.TileFrameY,
                relX,
                relY);

            int frameX = prepared.Tile.TileFrameX;
            int frameY = prepared.Tile.TileFrameY;
            int paintStyle = 0;
            if (tileType == TileID.Trees)
            {
                // Forest is variant -1, which adds a 0px column offset like vanilla GetTileDrawData.
                if (style.WoodVariant < 7)
                    frameX += TreeWoodColumnWidth * (style.WoodVariant + 1);
                paintStyle = style.WoodVariant;
            }
            else
            {
                // Palm biome is stored as a row in the palm sheet, not in the payload FrameY sway.
                frameY = PalmBiomeRowHeight * style.PalmBiome;
                paintStyle = style.PalmBiome;
            }

            Main.instance.LoadTiles(tileType);
            Texture2D texture = TextureAssets.Tile[tileType].Value;
            TilePaintSystemV2? paintSystem = TryGetPaintSystem();
            Texture2D? painted = paintSystem?.TryGetTileAndRequestIfNotReady(tileType, paintStyle, prepared.Tile.TileColor);
            if (painted is not null)
                texture = painted;
            if (texture is null)
                return false;

            sprite = new DrawSprite
            {
                Texture = texture,
                Source = new Rectangle(frameX, frameY, TreeTileSize, TreeTileSize),
                Effects = SpriteEffects.None,
                TileWidth = TreeTileSize,
                TileHeight = TreeTileSize,
                TileTop = 0,
                HalfBrickHeight = 0,
            };
            return true;
        }

        /// <summary>Gets the tile paint system used for painted tiles, tree tops, and branches.</summary>
        internal static TilePaintSystemV2? TryGetPaintSystem()
        {
            // --- Read TileDrawing's private paint system once, matching WallPreviewLayer's wall path.

            if (_paintSystem is not null)
                return _paintSystem;

            // Cache the private TileDrawing paint-system field after the first lookup.
            _paintSystemField ??= typeof(TileDrawing).GetField("_paintSystem", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_paintSystemField is null)
                return null;

            _paintSystem = _paintSystemField.GetValue(Main.instance.TilesRenderer) as TilePaintSystemV2;
            return _paintSystem;
        }

        /// <summary>Computes the unscaled draw origin for a resolved tile sprite within the preview grid.</summary>
        public static Vector2 GetDrawPosition(in PreviewDrawContext ctx, int relX, int relY, in DrawSprite sprite)
        {
            // --- Offset by grid cell, centering wider sprites and applying tileTop / half-brick shifts.

            return ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize - (sprite.TileWidth - PreviewDrawContext.TilePixelSize) / 2f,
                relY * PreviewDrawContext.TilePixelSize + sprite.TileTop + sprite.HalfBrickHeight);
        }
    }
}
