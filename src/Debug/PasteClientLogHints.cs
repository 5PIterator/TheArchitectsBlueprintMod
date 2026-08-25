using System.IO;
using TheArchitectsBlueprintMod.Config;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>Client diagnostic preferences sent with each paste so the server knows what to relay.</summary>
    internal readonly struct PasteClientLogHints
    {
        public TabLogLevel MinimumLogLevel { get; init; }
        public bool PrintDebugLogsToChat { get; init; }
        public bool PrintInfoLogsToChat { get; init; }
        public bool TagGeneric { get; init; }
        public bool TagPaste { get; init; }
        public bool TagTileExecutor { get; init; }
        public bool TagInventory { get; init; }
        public bool TagProjection { get; init; }
        public bool TagSelection { get; init; }
        public bool TagLayers { get; init; }
        public bool TagFrameLayer { get; init; }
        public bool TagTileMapping { get; init; }
        public bool TagInteractionMap { get; init; }
        public bool TagNetworking { get; init; }
        public bool TagToolAction { get; init; }
        public bool TagTemplate { get; init; }
        public bool TagManifest { get; init; }
        public bool TagOther { get; init; }

        /// <summary>Snapshots the local ClientLogConfig for an outbound paste request.</summary>
        public static PasteClientLogHints FromClientConfig()
        {
            // --- Copy every client logging toggle the server needs for OwnerChat filtering.

            ClientLogConfig cfg = ModContent.GetInstance<ClientLogConfig>();
            return new PasteClientLogHints
            {
                MinimumLogLevel = cfg.MinimumLogLevel,
                PrintDebugLogsToChat = cfg.PrintDebugLogsToChat,
                PrintInfoLogsToChat = cfg.PrintInfoLogsToChat,
                TagGeneric = cfg.TagGeneric,
                TagPaste = cfg.TagPaste,
                TagTileExecutor = cfg.TagTileExecutor,
                TagInventory = cfg.TagInventory,
                TagProjection = cfg.TagProjection,
                TagSelection = cfg.TagSelection,
                TagLayers = cfg.TagLayers,
                TagFrameLayer = cfg.TagFrameLayer,
                TagTileMapping = cfg.TagTileMapping,
                TagInteractionMap = cfg.TagInteractionMap,
                TagNetworking = cfg.TagNetworking,
                TagToolAction = cfg.TagToolAction,
                TagTemplate = cfg.TagTemplate,
                TagManifest = cfg.TagManifest,
                TagOther = cfg.TagOther,
            };
        }

        /// <summary>Writes this hint set onto a paste request packet.</summary>
        public void Write(BinaryWriter writer)
        {
            // --- Compact bool/level wire format matching Read.

            writer.Write((byte)MinimumLogLevel);
            writer.Write(PrintDebugLogsToChat);
            writer.Write(PrintInfoLogsToChat);
            writer.Write(TagGeneric);
            writer.Write(TagPaste);
            writer.Write(TagTileExecutor);
            writer.Write(TagInventory);
            writer.Write(TagProjection);
            writer.Write(TagSelection);
            writer.Write(TagLayers);
            writer.Write(TagFrameLayer);
            writer.Write(TagTileMapping);
            writer.Write(TagInteractionMap);
            writer.Write(TagNetworking);
            writer.Write(TagToolAction);
            writer.Write(TagTemplate);
            writer.Write(TagManifest);
            writer.Write(TagOther);
        }

        /// <summary>Reads a hint set previously written by Write.</summary>
        public static PasteClientLogHints Read(BinaryReader reader)
        {
            // --- Validate level byte, then read the same bool order as Write.

            byte levelByte = reader.ReadByte();
            if (levelByte > (byte)TabLogLevel.Error)
                throw new InvalidDataException($"Paste request has invalid log level byte: {levelByte}.");

            return new PasteClientLogHints
            {
                MinimumLogLevel = (TabLogLevel)levelByte,
                PrintDebugLogsToChat = reader.ReadBoolean(),
                PrintInfoLogsToChat = reader.ReadBoolean(),
                TagGeneric = reader.ReadBoolean(),
                TagPaste = reader.ReadBoolean(),
                TagTileExecutor = reader.ReadBoolean(),
                TagInventory = reader.ReadBoolean(),
                TagProjection = reader.ReadBoolean(),
                TagSelection = reader.ReadBoolean(),
                TagLayers = reader.ReadBoolean(),
                TagFrameLayer = reader.ReadBoolean(),
                TagTileMapping = reader.ReadBoolean(),
                TagInteractionMap = reader.ReadBoolean(),
                TagNetworking = reader.ReadBoolean(),
                TagToolAction = reader.ReadBoolean(),
                TagTemplate = reader.ReadBoolean(),
                TagManifest = reader.ReadBoolean(),
                TagOther = reader.ReadBoolean(),
            };
        }

        /// <summary>True when this owner's prefs want the given line relayed to their chat.</summary>
        public bool ShouldRelay(string? category, TabLogLevel level)
        {
            // --- Chat echo toggle for severity, then category tag, then minimum level.

            if (level < MinimumLogLevel)
                return false;

            bool chatEnabled = level switch
            {
                TabLogLevel.Debug => PrintDebugLogsToChat,
                TabLogLevel.Info => PrintInfoLogsToChat,
                _ => true,
            };
            if (!chatEnabled)
                return false;

            return IsCategoryEnabled(category);
        }

        /// <summary>Maps a category string onto the matching tag toggle in this hint set.</summary>
        public bool IsCategoryEnabled(string? category)
        {
            // --- Mirror TABLog.ShouldEmitCategory against the snapshot, not live client config.

            return category switch
            {
                TABLog.GenericCategory => TagGeneric,
                "Paste" => TagPaste,
                "Projection" or "ProjectionSystem" => TagProjection,
                "Selection" or "SelectionSystem" or "Blueprint" => TagSelection,
                "Layers" => TagLayers,
                "FrameLayer" => TagFrameLayer,
                "TileMapping" => TagTileMapping,
                "InteractionMap" => TagInteractionMap,
                "PasteNet" => TagNetworking,
                "ToolAction" => TagToolAction,
                "Template" => TagTemplate,
                TABLog.TileExecutorCategory => TagTileExecutor,
                TABLog.InventoryCategory => TagInventory,
                TABLog.ManifestCategory => TagManifest,
                null or "" => TagOther,
                _ => TagOther,
            };
        }
    }
}
