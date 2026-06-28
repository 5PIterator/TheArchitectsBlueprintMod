using System.Collections.Generic;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Core.Layers;
using Microsoft.Xna.Framework;
using Terraria;

namespace TheArchitectsBlueprintMod.Core
{
    internal interface IPastingTask
    {
        TileBatch Batch { get; }
        IReadOnlyList<PasteTile> Tiles { get; }

        /// <summary>
        /// Adds this task's world points to the list, up to maxRemaining. Returns the number added.
        /// </summary>
        int AddTilesTo(List<Point> list, int maxRemaining);

        /// <summary>
        /// Appends this task's world tile positions to <paramref name="dst"/>, up to <paramref name="maxRemaining"/>. Returns the number added.
        /// </summary>
        int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue);
    }

    internal sealed class ClearingTask(Layer layer, TileBatch batch) : IPastingTask
    {
        private readonly Layer _layer = layer;
        public TileBatch Batch { get; } = batch;

        public IReadOnlyList<PasteTile> Tiles => Batch.Tiles;

        public int AddTilesTo(List<Point> list, int maxRemaining)
            => Batch.AddTilesTo(list, maxRemaining);

        public int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue)
            => Batch.CollectOverlayPoints(dst, maxRemaining);

        /// <summary>
        /// Executes this clearing task. Returns true if the task is complete, false if it needs to be retried with the remaining tiles.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool Execute(in PasteContext context, in PasteProgress progress)
        {
            /// If there are no tiles, consider this task complete. This can happen if the task was created with no tiles, or if all tiles were consumed in a previous attempt.

            // Validate Tile positions.
            if (Batch.IsEmpty)
                return true;

            if (!WorldGen.InWorld(Batch.Head.World.X, Batch.Head.World.Y))
                return true;

            PasteTile head = Batch.Head;

            LayerResult res = _layer.Clear(Batch.Tiles, in context);
            TABLog.Debug($"Clear execute layer={_layer.Id} world=({Batch.Head.World.X},{Batch.Head.World.Y}) success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}", "Paste");

            bool taskCompletes = ResolveClearTaskCompletes(in res);
            PasteProgressOutcome outcome = PasteProgress.ClassifyClearAttempt(in res, taskCompletes);
            string? detail = PasteProgress.ResolveBatchRetryDetail(Batch, in res, outcome);
            PastingInfoLog.Write(progress.WithTaskResult(
                in head,
                outcome,
                res.FailReason,
                detail,
                layerId: _layer.Id));

            if (!res.Success)
            {
                if (res.FailReason == LayerFailReason.InsufficientPickaxePower)
                    return true;
                return false;
            }

            // If there are remaining tiles, requeue the task and return false.
            if (res.RemainingBatch is not null && res.RemainingBatch.Count > 0)
            {
                Batch.ReplaceWith(res.RemainingBatch.Tiles);
                return false;
            }

            return true;
        }

        private static bool ResolveClearTaskCompletes(in LayerResult res)
        {
            if (res.Success)
                return res.RemainingBatch is null || res.RemainingBatch.Count == 0;

            return res.FailReason == LayerFailReason.InsufficientPickaxePower;
        }
    }

    internal sealed class PlacingTask(Layer layer, TileBatch batch) : IPastingTask
    {
        private readonly Layer _layer = layer;
        public TileBatch Batch { get; } = batch;

        public IReadOnlyList<PasteTile> Tiles => Batch.Tiles;

        public int AddTilesTo(List<Point> list, int maxRemaining)
            => Batch.AddTilesTo(list, maxRemaining);

        public int CollectOverlayPoints(List<Point> dst, int maxRemaining = int.MaxValue)
            => Batch.CollectOverlayPoints(dst, maxRemaining);

        /// <summary>
        /// Executes this placing task. Returns true if the task is complete, false if it needs to be retried with the remaining tiles.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        internal bool Execute(in PasteContext context, in PasteProgress progress)
        {
            /// If there are no tiles, consider this task complete. This can happen if the task was created with no tiles, or if all tiles were consumed in a previous attempt.

            // Validate Tile positions.
            if (Batch.IsEmpty)
                return true;

            if (!WorldGen.InWorld(Batch.Head.World.X, Batch.Head.World.Y))
                return true;

            var head = Batch.Head;
            if (_layer.Id == LayerSystem.Wiring.Id)
            {
                TABLog.Debug(
                    $"Place begin layer=Wiring batch={Batch.Count} rel=({head.Relative.X},{head.Relative.Y}) world=({head.World.X},{head.World.Y}) intent={PasteProgress.FormatWireState(head.PayloadCell.Wire)} before={PasteProgress.FormatWireStateAt(head.World.X, head.World.Y)}",
                    "Paste");
            }
            else if (_layer.Id == LayerSystem.Detail.Id)
            {
                TABLog.Debug(
                    $"Place begin layer=Detail batch={Batch.Count} rel=({head.Relative.X},{head.Relative.Y}) world=({head.World.X},{head.World.Y}) intent={PasteProgress.FormatDetailState(head.PayloadCell.Detail)} before={PasteProgress.FormatDetailState(head.Target)}",
                    "Paste");
            }
            else if (_layer.Id == LayerSystem.Paint.Id)
            {
                TABLog.Debug(
                    $"Place begin layer=Paint batch={Batch.Count} rel=({head.Relative.X},{head.Relative.Y}) world=({head.World.X},{head.World.Y}) intent={PasteProgress.FormatPaintState(head.PayloadCell)} before={PasteProgress.FormatPaintState(head.Target)}",
                    "Paste");
            }
            else
            {
                TABLog.Debug(
                    $"Place begin layer={_layer.Id} batch={Batch.Count} rel=({head.Relative.X},{head.Relative.Y}) world=({head.World.X},{head.World.Y}) payload[hasTile={head.PayloadCell.HasTile()} type={head.PayloadCell.Block.Type} frame=({head.PayloadCell.Block.FrameX},{head.PayloadCell.Block.FrameY})] source[hasTile={head.Source.HasTile} type={head.Source.TileType}] target[hasTile={head.Target.HasTile} type={head.Target.TileType}]",
                    "Paste");
            }

            // Execute the placing operation. If it fails, return false and requeue the task.
            LayerResult res = _layer.Place(Batch.Tiles, in context);
            if (_layer.Id == LayerSystem.Wiring.Id)
            {
                TABLog.Debug(
                    $"Place execute layer=Wiring world=({head.World.X},{head.World.Y}) after={PasteProgress.FormatWireStateAt(head.World.X, head.World.Y)} success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}",
                    "Paste");
            }
            else if (_layer.Id == LayerSystem.Detail.Id)
            {
                TABLog.Debug(
                    $"Place execute layer=Detail world=({head.World.X},{head.World.Y}) after={PasteProgress.FormatDetailStateAt(head.World.X, head.World.Y)} success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}",
                    "Paste");
            }
            else if (_layer.Id == LayerSystem.Paint.Id)
            {
                TABLog.Debug(
                    $"Place execute layer=Paint world=({head.World.X},{head.World.Y}) after={PasteProgress.FormatPaintStateAt(head.World.X, head.World.Y)} success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}",
                    "Paste");
            }
            else
            {
                TABLog.Debug($"Place execute layer={_layer.Id} world=({Batch.Head.World.X},{Batch.Head.World.Y}) success={res.Success} failReason={res.FailReason} remaining={res.RemainingTiles.Count}", "Paste");
            }
            PasteTile logHead = Batch.Head;

            bool taskCompletes = ResolvePlaceTaskCompletes(in res, in context);
            PasteProgressOutcome outcome = PasteProgress.ClassifyPlaceAttempt(in res, in context, taskCompletes);
            string? detail = PasteProgress.ResolveBatchRetryDetail(Batch, in res, outcome);
            PastingInfoLog.Write(progress.WithTaskResult(in logHead, outcome, res.FailReason, detail, layerId: _layer.Id));

            if (!res.Success)
            {
                if (!context.RetryOnMissingItems && res.FailReason == LayerFailReason.MissingItems)
                    return true;
                if (!context.RetryOnInvalidPlacement && res.FailReason == LayerFailReason.InvalidPlacement)
                    return true;
                return false;
            }

            // Success with remaining tiles means the layer placed part of a batch (e.g. FallBlock columns).
            if (res.RemainingBatch is not null && res.RemainingBatch.Count > 0)
            {
                Batch.ReplaceWith(res.RemainingBatch.Tiles);
                return false;
            }

            return true;
        }

        private static bool ResolvePlaceTaskCompletes(in LayerResult res, in PasteContext context)
        {
            if (res.Success)
                return res.RemainingBatch is null || res.RemainingBatch.Count == 0;

            if (!context.RetryOnMissingItems && res.FailReason == LayerFailReason.MissingItems)
                return true;

            if (!context.RetryOnInvalidPlacement && res.FailReason == LayerFailReason.InvalidPlacement)
                return true;

            return false;
        }
    }
}
