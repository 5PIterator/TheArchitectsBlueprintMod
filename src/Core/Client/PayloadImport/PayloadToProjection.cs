using System;
using System.Collections.Generic;
using ArchitectsStencilMod.Core;
using ArchitectsStencilMod.Debug;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ArchitectsStencilMod.Core.Client.PayloadImport
{
    public static class PayloadProjectionExtensions
    {
        /// <summary>
        /// Fills the projection <see cref="Projection"/> payload from <paramref name="document"/> and sets selection to the listed cells.
        /// <see cref="Projection.TargetArea"/> size must match <see cref="PayloadDocument.Width"/> / <see cref="PayloadDocument.Height"/>.
        /// Does not set <see cref="Projection.SourceArea"/> (JSON has no world capture region).
        /// </summary>
        public static void ApplyPayload(this Projection projection, PayloadDocument document)
        {
            ArgumentNullException.ThrowIfNull(projection);
            ArgumentNullException.ThrowIfNull(document);
            PayloadJson.ValidateSchema(document);

            if (!projection.IsDefined)
                throw new InvalidOperationException("Projection must be defined before applying a payload.");

            if (document.Width != projection.Bounds.Width || document.Height != projection.Bounds.Height)
            {
                throw new ArgumentException(
                    $"Document size {document.Width}x{document.Height} does not match projection bounds {projection.Bounds.Width}x{projection.Bounds.Height}.");
            }

            var entries = new List<Projection.PayloadEntry>(document.Payload.Count);
            var selected = new List<Blueprint.BoundedTile>(document.Payload.Count);

            foreach (PayloadCell cell in document.Payload)
            {
                var rel = new Point(cell.X, cell.Y);
                if ((uint)rel.X >= (uint)document.Width || (uint)rel.Y >= (uint)document.Height)
                    continue;

                entries.Add(new Projection.PayloadEntry(rel, cell.Facets));
                selected.Add(new Blueprint.BoundedTile(rel, selected: true));
            }

            projection.LoadPayloadFromImport(
                entries,
                document.Width,
                document.Height,
                selected);
        }

        /// <summary>
        /// Every selected <see cref="Projection.SourceTiles"/> cell, including <c>Facets == null</c> (air).
        /// Used for multiplayer paste so the wire format matches <see cref="PayloadDocument"/>.
        /// </summary>
        internal static PayloadDocument ToPayloadDocumentForNetwork(this Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            if (!projection.IsDefined)
                throw new InvalidOperationException("Projection must be defined.");

            var doc = new PayloadDocument
            {
                SchemaVersion = PayloadJson.CurrentSchemaVersion,
                ProjectName = string.Empty,
                PlayerName = Main.LocalPlayer?.name ?? string.Empty,
                Width = projection.Bounds.Width,
                Height = projection.Bounds.Height,
            };

            foreach (Blueprint.BoundedTile bt in projection.SourceTiles)
            {
                if (!bt.Selected)
                    continue;

                projection.TryGetPayloadData(bt.Relative, out PayloadFacets? facets);
                doc.Payload.Add(new PayloadCell
                {
                    X = bt.Relative.X,
                    Y = bt.Relative.Y,
                    Facets = facets,
                });

                // #region agent log
                if (doc.Width * doc.Height <= 64)
                {
                    AgentNdjsonLog.Write(
                        "H4",
                        "PayloadToProjection.ToPayloadDocumentForNetwork",
                        "export_cell",
                        new
                        {
                            bt.Relative.X,
                            bt.Relative.Y,
                            blockType = facets?.Block?.TileType,
                            blockHas = facets?.Block?.HasTile,
                        });
                }
                // #endregion
            }

            return doc;
        }

        /// <summary>
        /// Builds a document from the projection payload for each selected cell that has non-empty facets.
        /// </summary>
        public static PayloadDocument ToPayloadDocument(
            this Projection projection,
            string name,
            string? creatorAlias = null)
        {
            ArgumentNullException.ThrowIfNull(projection);
            if (!projection.IsDefined)
                throw new InvalidOperationException("Projection must be defined before exporting a tile grid payload.");

            string alias = creatorAlias ?? Main.LocalPlayer?.name ?? string.Empty;

            var doc = new PayloadDocument
            {
                SchemaVersion = PayloadJson.CurrentSchemaVersion,
                ProjectName = name ?? string.Empty,
                PlayerName = alias,
                Width = projection.Bounds.Width,
                Height = projection.Bounds.Height,
            };

            foreach (Blueprint.BoundedTile bt in projection.SourceTiles)
            {
                if (!bt.Selected)
                    continue;

                if (!projection.TryGetPayloadData(bt.Relative, out PayloadFacets? facets) || facets is null)
                    continue;

                doc.Payload.Add(new PayloadCell
                {
                    X = bt.Relative.X,
                    Y = bt.Relative.Y,
                    Facets = facets,
                });
            }

            return doc;
        }

        /// <summary>
        /// True if the tile has no wall, no active tile, no liquid, no wires, and default paint.
        /// </summary>
        public static bool IsPayloadCellVisuallyEmpty(Tile tile)
        {
            return tile.WallType == WallID.None
                && !tile.HasTile
                && tile.LiquidAmount == 0
                && !tile.RedWire && !tile.BlueWire && !tile.GreenWire && !tile.YellowWire
                && !tile.HasActuator
                && tile.TileColor == PaintID.None
                && tile.WallColor == PaintID.None;
        }
    }
}
