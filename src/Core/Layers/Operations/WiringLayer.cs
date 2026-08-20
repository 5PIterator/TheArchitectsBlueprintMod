using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Core.TileMapping;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Wire colors and actuators on existing tiles. Mechanism tiles are placed by Block or anchored layers;
    /// this layer applies <see cref="PayloadCell.Wire"/> via <see cref="WorldGen"/> after those layers run.
    /// </summary>
    internal sealed class WiringLayer : Layer
    {
        public static readonly WiringLayer Definition = new();

        private WiringLayer() : base("Wiring", LayerType.Wiring) { }

        public override LayerResult Identify(in PasteTile tile, in PasteTileLookup _, in PasteContext context, PasteSubject identifySubject)
        {
            Tile subjectTile = PasteTile.GetTileForIdentify(in tile, identifySubject);
            bool hasWiring = subjectTile.RedWire || subjectTile.BlueWire || subjectTile.GreenWire || subjectTile.YellowWire || subjectTile.HasActuator;
            if (!hasWiring)
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatWireState(subjectTile));
        }

        public override bool CanClear(in PasteTile tile, in PasteTileLookup _, in PasteContext context)
        {
            Tile t = tile.Target;
            return t.RedWire || t.BlueWire || t.GreenWire || t.YellowWire || t.HasActuator;
        }

        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup _, in PasteContext context) => true;

        public override bool Match(in PasteTile tile)
        {
            if (TileComparison.IsSourceCutIntent(in tile))
                return tile.Source.RedWire == tile.Target.RedWire
                    && tile.Source.BlueWire == tile.Target.BlueWire
                    && tile.Source.GreenWire == tile.Target.GreenWire
                    && tile.Source.YellowWire == tile.Target.YellowWire
                    && tile.Source.HasActuator == tile.Target.HasActuator;
            Tile intent = tile.PayloadTile;
            return intent.RedWire == tile.Target.RedWire
                && intent.BlueWire == tile.Target.BlueWire
                && intent.GreenWire == tile.Target.GreenWire
                && intent.YellowWire == tile.Target.YellowWire
                && intent.HasActuator == tile.Target.HasActuator;
        }

        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            string message = LayerLogFormat.FormatWireState(Main.tile[x, y]);

            if (IsWireClear(Main.tile[x, y]))
                return LayerResult.Ok();

            bool changed = false;
            if (!ClearWiringAt(x, y, in context, ref changed))
                return LayerResult.Fail(LayerFailReason.ClearBlocked, message);

            return changed
                ? LayerResult.Ok(message, LayerLogFormat.ClearedLabel)
                : LayerResult.Ok();
        }

        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var tile = tiles[0];
            int x = tile.World.X, y = tile.World.Y;
            var intent = tile.PayloadCell.Wire;
            string message = LayerLogFormat.FormatWireState(in intent);

            if (WireMatches(in intent, Main.tile[x, y]))
                return LayerResult.Ok();

            bool changed = false;
            if (!ApplyWireIntent(x, y, in intent, in context, ref changed))
                return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);

            return changed
                ? LayerResult.Ok(message, LayerLogFormat.WiredLabel)
                : LayerResult.Ok();
        }

        /// <summary>Queues removal of every wire color and actuator at the world cell.</summary>
        private static bool ClearWiringAt(int x, int y, in PasteContext context, ref bool changed)
        {
            if (Main.tile[x, y].RedWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].BlueWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire2(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].GreenWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire3(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].YellowWire)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearWire4(x, y, in context))
                    return false;
            }

            if (Main.tile[x, y].HasActuator)
            {
                changed = true;
                if (!TileExecutor.EnqueueClearActuator(x, y, in context))
                    return false;
            }

            return true;
        }

        /// <summary>Queues wire/actuator reconciliation to payload intent through TileExecutor.</summary>
        private static bool ApplyWireIntent(int x, int y, in PayloadCell.WireData intent, in PasteContext context, ref bool changed)
        {
            bool consumeItem = context.RequireItems && !context.IsSweep;

            if (Main.tile[x, y].RedWire != intent.RedWire)
            {
                changed = true;
                bool queued = intent.RedWire
                    ? TileExecutor.EnqueuePlaceWire(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].BlueWire != intent.BlueWire)
            {
                changed = true;
                bool queued = intent.BlueWire
                    ? TileExecutor.EnqueuePlaceWire2(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire2(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].GreenWire != intent.GreenWire)
            {
                changed = true;
                bool queued = intent.GreenWire
                    ? TileExecutor.EnqueuePlaceWire3(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire3(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].YellowWire != intent.YellowWire)
            {
                changed = true;
                bool queued = intent.YellowWire
                    ? TileExecutor.EnqueuePlaceWire4(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearWire4(x, y, in context);
                if (!queued)
                    return false;
            }

            if (Main.tile[x, y].HasActuator != intent.HasActuator)
            {
                bool queued = intent.HasActuator
                    ? TileExecutor.EnqueuePlaceActuator(x, y, in context, consumeItem)
                    : TileExecutor.EnqueueClearActuator(x, y, in context);
                changed = true;
                if (!queued)
                    return false;
            }

            return true;
        }

        private static bool WireMatches(in PayloadCell.WireData intent, in Tile live) =>
            live.RedWire == intent.RedWire
            && live.BlueWire == intent.BlueWire
            && live.GreenWire == intent.GreenWire
            && live.YellowWire == intent.YellowWire
            && live.HasActuator == intent.HasActuator;

        private static bool IsWireClear(in Tile live) =>
            !live.RedWire && !live.BlueWire && !live.GreenWire && !live.YellowWire && !live.HasActuator;

        public override bool DrawsCells => true;

        public override void DrawCell(in PreviewDrawContext ctx, PayloadCell cell, int relX, int relY)
        {
            PayloadCell.WireData wire = cell.Wire;
            if (!wire.RedWire && !wire.BlueWire && !wire.GreenWire && !wire.YellowWire && !wire.HasActuator)
                return;

            if (BuilderToggle.HideAllWires.CurrentState != 0)
                return;

            Vector2 pos = ctx.Position + new Vector2(
                relX * PreviewDrawContext.TilePixelSize,
                relY * PreviewDrawContext.TilePixelSize);

            if (wire.RedWire && BuilderToggle.RedWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire.Value, w => w.RedWire);

            if (wire.BlueWire && BuilderToggle.BlueWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire2.Value, w => w.BlueWire);

            if (wire.GreenWire && BuilderToggle.GreenWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire3.Value, w => w.GreenWire);

            if (wire.YellowWire && BuilderToggle.YellowWireVisibility.CurrentState != 0)
                DrawWireColor(in ctx, relX, relY, pos, TextureAssets.Wire4.Value, w => w.YellowWire);

            if (wire.HasActuator && BuilderToggle.ActuatorsVisibility.CurrentState != 0)
                DrawActuatorPreview(ctx.SpriteBatch, pos, ctx.Scale, ctx.Tint);
        }

        private static void DrawWireColor(
            in PreviewDrawContext ctx,
            int relX,
            int relY,
            Vector2 pos,
            Texture2D texture,
            Func<PayloadCell.WireData, bool> hasWire)
        {
            bool up = ctx.NeighborHasWire(relX, relY, 0, -1, hasWire);
            bool down = ctx.NeighborHasWire(relX, relY, 0, 1, hasWire);
            bool left = ctx.NeighborHasWire(relX, relY, -1, 0, hasWire);
            bool right = ctx.NeighborHasWire(relX, relY, 1, 0, hasWire);
            Rectangle source = GetWireSourceRect(up, down, left, right);
            ctx.SpriteBatch.Draw(texture, pos * ctx.Scale, source, ctx.Tint, 0f, Vector2.Zero, ctx.Scale, SpriteEffects.None, 0f);
        }

        private static void DrawActuatorPreview(SpriteBatch sb, Vector2 pos, float scale, Color tint)
        {
            Texture2D texture = TextureAssets.Actuator.Value;
            var source = new Rectangle(0, 0, texture.Width, texture.Height);
            sb.Draw(texture, pos * scale, source, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static Rectangle GetWireSourceRect(bool up, bool down, bool left, bool right)
        {
            if (up)
            {
                if (down)
                {
                    if (left)
                        return right ? new Rectangle(18, 18, 16, 16) : new Rectangle(54, 0, 16, 16);
                    return right ? new Rectangle(36, 0, 16, 16) : new Rectangle(0, 0, 16, 16);
                }

                if (left)
                    return right ? new Rectangle(0, 18, 16, 16) : new Rectangle(54, 18, 16, 16);
                return right ? new Rectangle(36, 18, 16, 16) : new Rectangle(36, 36, 16, 16);
            }

            if (down)
            {
                if (left)
                    return right ? new Rectangle(72, 0, 16, 16) : new Rectangle(72, 18, 16, 16);
                return right ? new Rectangle(0, 36, 16, 16) : new Rectangle(18, 36, 16, 16);
            }

            if (left)
                return right ? new Rectangle(18, 0, 16, 16) : new Rectangle(54, 36, 16, 16);
            return right ? new Rectangle(72, 36, 16, 16) : new Rectangle(0, 54, 16, 16);
        }
    }
}
