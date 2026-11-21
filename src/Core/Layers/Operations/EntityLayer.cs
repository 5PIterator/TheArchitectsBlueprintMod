using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Core.Layers
{
    /// <summary>
    /// Chest, sign, and TileEntity sidecar data. Host tiles are placed by Frame/Block;
    /// this layer overrides entity contents after those hosts exist.
    /// </summary>
    internal sealed class EntityLayer : Layer
    {
        public static readonly EntityLayer Definition = new();

        private EntityLayer() : base("Entity", LayerType.Entity) { }

        /// <summary>
        /// Always false so Transparent planning still queues a content override.
        /// Inventory-aware matching of entity contents is planned separately.
        /// </summary>
        public override bool Match(in PasteTile tile) => false;

        /// <summary>Claims the origin cell of a payload entity (place) or a live chest/sign/TE (clear).</summary>
        public override LayerResult Identify(
            in PasteTile tile,
            in PasteTileLookup lookup,
            in PasteContext context,
            PasteSubject identifySubject)
        {
            // --- Payload sidecar for place; live FullName at the world cell for clear.

            if (identifySubject == PasteSubject.Payload)
            {
                if (context.Payload is null || !context.Payload.TryGetEntity(tile.Relative, out PayloadEntity entity))
                    return LayerResult.IdentifyNone();

                return LayerResult.Identified(
                    TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                    LayerLogFormat.FormatEntity(entity.FullName));
            }

            if (!PayloadEntityHandlers.TryGetLiveFullName(tile.World.X, tile.World.Y, out string liveName))
                return LayerResult.IdentifyNone();

            return LayerResult.Identified(
                TileBatch.FromCells([new Blueprint.BlueprintCell(tile.Relative, tile.World, true)]),
                LayerLogFormat.FormatEntity(liveName));
        }

        /// <summary>True when a live chest, sign, or TileEntity exists at the target cell.</summary>
        public override bool CanClear(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context) =>
            PayloadEntityHandlers.TryGetLiveFullName(tile.World.X, tile.World.Y, out _);

        /// <summary>True when the payload has sidecar data for this origin; host tiles are Frame's job.</summary>
        public override bool PlacementValid(in PasteTile tile, in PasteTileLookup lookup, in PasteContext context)
        {
            // --- Sidecar presence is enough at plan time; Place fails if Frame left no live host.

            return context.Payload is not null && context.Payload.TryGetEntity(tile.Relative, out _);
        }

        /// <summary>Wipes live entity contents without destroying the host tile.</summary>
        public override LayerResult Clear(TileBatch batch, in PasteContext context)
        {
            // --- Direct wipe (void-equivalent). Future: Store-style paste arrows extract items first.

            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            var remaining = new List<PasteTile>();
            string? message = null;
            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                if (!PayloadEntityHandlers.TryGetLiveFullName(tile.World.X, tile.World.Y, out string liveName))
                    continue;

                message ??= LayerLogFormat.FormatEntity(liveName);

                // TODO(entity-arrows): when ItemDrop is Store, fire paste arrows that move items out of
                // the chest/TE into player or storage inventory, then wipe leftovers.
                if (!WorldEntityOps.TryClearEntity(tile.World, in context))
                    remaining.Add(tile);
            }

            if (remaining.Count == 0)
                return LayerResult.Ok(message, LayerLogFormat.ClearedLabel);
            if (remaining.Count < tiles.Count)
                return LayerResult.OkWithRemaining(remaining, message, LayerLogFormat.ClearedLabel);
            return LayerResult.Fail(LayerFailReason.ClearBlocked, message);
        }

        /// <summary>Finds the live host and replaces its data with the payload sidecar.</summary>
        public override LayerResult Place(TileBatch batch, in PasteContext context)
        {
            // --- Direct override (no item consume). Missing host is InvalidPlacement, same as other layers.

            IReadOnlyList<PasteTile> tiles = batch.Tiles;
            if (tiles.Count == 0)
                return LayerResult.Ok();

            Payload? payload = context.Payload;
            if (payload is null || !payload.IsDefined)
                return LayerResult.Fail(LayerFailReason.InvalidPlacement);

            var remaining = new List<PasteTile>();
            string? message = null;
            string okLabel = LayerLogFormat.PlacedLabel(in context);
            for (int i = 0; i < tiles.Count; i++)
            {
                PasteTile tile = tiles[i];
                if (!payload.TryGetEntity(tile.Relative, out PayloadEntity entity))
                    continue;

                message ??= LayerLogFormat.FormatEntity(entity.FullName);

                // TODO(entity-arrows): when RequireItems && !IsSweep, fire paste arrows that
                // consume inventory items and insert them into the entity instead of LoadData override.
                if (!WorldEntityOps.TryPlaceEntity(tile.World, in entity, in context))
                    remaining.Add(tile);
            }

            if (remaining.Count == 0)
                return LayerResult.Ok(message, okLabel);
            if (remaining.Count < tiles.Count)
                return LayerResult.OkWithRemaining(remaining, message, okLabel);
            return LayerResult.Fail(LayerFailReason.InvalidPlacement, message);
        }

        public override void BeginPass(in PreviewDrawContext ctx) =>
            EntityPreviewStrategies.BeginPass();

        public override void DrawPass(in PreviewDrawContext ctx)
        {
            // --- Payload sidecars get marker + content; live hosts under the projection get markers only.

            var covered = new HashSet<Point>();
            IReadOnlyList<PayloadEntity>? entities = ctx.Entities;
            if (entities is not null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    PayloadEntity entity = entities[i];
                    Point rel = entity.Relative;
                    if (!ctx.IsCellVisible(rel.X, rel.Y))
                        continue;

                    covered.Add(rel);

                    PayloadCell originCell = default;
                    if (ctx.CellLookup is not null)
                        ctx.CellLookup.TryGetValue(rel, out originCell);

                    var args = new PreviewEntityDrawArgs
                    {
                        Ctx = ctx,
                        Entity = entity,
                        OriginCell = originCell,
                        RelX = rel.X,
                        RelY = rel.Y,
                    };
                    EntityPreviewResolver.TryDraw(in args);
                }
            }

            if (ctx.WorldOriginX is int worldOriginX && ctx.WorldOriginY is int worldOriginY)
                DrawLiveHosts(in ctx, worldOriginX, worldOriginY, covered);

            if (EntityPreviewStrategies.UsedPlayerRenderer)
                RestoreSpriteBatch(in ctx);
        }

        /// <summary>
        /// Draws live chest/sign/TE origin markers for a cached world blit that omitted world-origin passes.
        /// </summary>
        public static void DrawLiveHostMarkers(
            SpriteBatch sb,
            int worldOriginX,
            int worldOriginY,
            int minX,
            int maxX,
            int minY,
            int maxY,
            Color tint,
            Func<Point, bool>? includeCell)
        {
            // --- Build a minimal preview context and reuse the live-host marker path.

            var ctx = new PreviewDrawContext
            {
                SpriteBatch = sb,
                Position = new Vector2(worldOriginX * PreviewDrawContext.TilePixelSize, worldOriginY * PreviewDrawContext.TilePixelSize)
                    - Main.screenPosition,
                Scale = 1f,
                Tint = tint,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                IncludeCell = includeCell,
                WorldOriginX = worldOriginX,
                WorldOriginY = worldOriginY,
            };

            EntityPreviewStrategies.BeginPass();
            DrawLiveHosts(in ctx, worldOriginX, worldOriginY, covered: null);
            if (EntityPreviewStrategies.UsedPlayerRenderer)
                RestoreSpriteBatch(in ctx);
        }

        /// <summary>Marks live entity hosts under the visible footprint that are not already covered.</summary>
        private static void DrawLiveHosts(
            in PreviewDrawContext ctx,
            int worldOriginX,
            int worldOriginY,
            HashSet<Point>? covered)
        {
            // --- Scan world chests/signs/TEs in the visible world bounds only.

            var worldBounds = new Rectangle(
                worldOriginX + ctx.MinX,
                worldOriginY + ctx.MinY,
                ctx.MaxX - ctx.MinX,
                ctx.MaxY - ctx.MinY);

            foreach (PayloadEntityHandlers.HostAtOrigin host in PayloadEntityHandlers.EnumerateHostsInBounds(worldBounds))
            {
                int relX = host.WorldX - worldOriginX;
                int relY = host.WorldY - worldOriginY;
                if (!ctx.IsCellVisible(relX, relY))
                    continue;
                if (covered is not null && !covered.Add(new Point(relX, relY)))
                    continue;

                var args = new PreviewEntityDrawArgs
                {
                    Ctx = ctx,
                    Entity = new PayloadEntity(new Point(relX, relY), host.FullName),
                    OriginCell = default,
                    RelX = relX,
                    RelY = relY,
                };
                EntityPreviewStrategies.TryDrawOriginMarker(in args);
            }
        }

        private static void RestoreSpriteBatch(in PreviewDrawContext ctx)
        {
            SpriteBatch sb = ctx.SpriteBatch;
            try
            {
                sb.End();
            }
            catch
            {
                // Batch may already be ended by DrawPlayer.
            }

            if (ctx.WorldOriginX is null)
            {
                sb.Begin();
                return;
            }

            sb.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                Main.DefaultSamplerState,
                DepthStencilState.None,
                Main.Rasterizer,
                null,
                Main.GameViewMatrix.TransformationMatrix);
        }
    }
}
