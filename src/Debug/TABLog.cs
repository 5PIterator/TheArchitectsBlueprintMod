using System;
using System.Collections.Generic;
using System.Text;
using TheArchitectsBlueprintMod.Config;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Debug
{
    /// <summary>
    /// Centralized logging for the mod. Uses tModLoader's Mod.Logger (log4net).
    /// Logs go to Documents/My Games/Terraria/tModLoader/Logs/Mods/.
    /// Initialize in mod Load(); Unload in mod Unload().
    /// Debug and info records are buffered per frame and flushed once at frame end.
    /// </summary>
    internal sealed class TABLog : ModSystem
    {
        public const string GenericCategory = "Generic";
        public const string TileExecutorCategory = "TileExecutor";
        public const string InventoryCategory = "Inventory";
        public const string ManifestCategory = "Manifest";

        private static Mod? _mod;
        private static uint _batchFrame = uint.MaxValue;
        private static readonly List<PendingEntry> _pending = [];

        private sealed class PendingEntry
        {
            public required string Message { get; init; }
            public string? Category { get; init; }
            public TabLogLevel Level { get; init; }
            public bool EchoToChat { get; init; }
            public Color ChatColor { get; init; }
            public int? OwnerPlayer { get; init; }
        }

        /// <summary>
        /// Call from TheArchitectsBlueprintMod.Load(). Required before any logging.
        /// </summary>
        public override void Load()
        {
            _mod = this.Mod;
            _batchFrame = uint.MaxValue;
            _pending.Clear();
        }

        public override void PostUpdateEverything()
        {
            Flush();
        }

        /// <summary>
        /// Call from TheArchitectsBlueprintMod.Unload(). Flushes pending records and clears the Mod reference.
        /// </summary>
        public override void Unload()
        {
            Flush();
            _mod = null;
            _batchFrame = uint.MaxValue;
            _pending.Clear();
        }

        /// <summary>
        /// Flushes buffered debug/info records for the current frame.
        /// </summary>
        internal static void Flush()
        {
            var mod = _mod;
            if (mod is null || _pending.Count == 0)
                return;

            // --- Join buffered lines by level so each frame issues at most two log4net writes.
            var debugLines = new StringBuilder();
            var infoLines = new StringBuilder();

            for (int i = 0; i < _pending.Count; i++)
            {
                PendingEntry entry = _pending[i];
                string line = FormatLine(entry.Message, entry.Category);

                if (entry.Level == TabLogLevel.Debug)
                    debugLines.AppendLine(line);
                else if (entry.Level == TabLogLevel.Info)
                    infoLines.AppendLine(line);

                if (entry.EchoToChat)
                    Chat(entry.Message, entry.Category, entry.ChatColor, entry.OwnerPlayer);
            }

            if (debugLines.Length > 0)
                mod.Logger.Debug(debugLines.ToString().TrimEnd());

            if (infoLines.Length > 0)
                mod.Logger.Info(infoLines.ToString().TrimEnd());

            _pending.Clear();
            _batchFrame = uint.MaxValue;
        }

        /// <summary>
        /// True when debug logging should also be echoed to chat.
        /// </summary>
        public static bool IsDebugChatEnabled => ClientLogConfig.Instance?.PrintDebugLogsToChat ?? false;

        /// <summary>
        /// True when info logging should also be echoed to chat.
        /// </summary>
        public static bool IsInfoChatEnabled => ClientLogConfig.Instance?.PrintInfoLogsToChat ?? false;

        /// <summary>
        /// True when debug/info records for the given category should be emitted.
        /// </summary>
        public static bool IsCategoryEnabled(string? category) => ShouldEmitCategory(category);

        /// <summary>True when the given severity meets the client minimum log level.</summary>
        public static bool IsLevelEnabled(TabLogLevel level) => MeetsThreshold(level);

        /// <summary>True when debug records meet the client minimum log level.</summary>
        public static bool IsDebugEnabled => IsLevelEnabled(TabLogLevel.Debug);

        /// <summary>
        /// Queues a debug message for the current frame batch.
        /// </summary>
        public static void Debug(string message, string? category = null, int? ownerPlayer = null)
        {
            Enqueue(
                message,
                category,
                TabLogLevel.Debug,
                echoToChat: IsDebugChatEnabled,
                chatColor: Color.LightGray,
                ownerPlayer);
        }

        /// <summary>
        /// Queues an info message for the current frame batch.
        /// </summary>
        public static void Info(string message, string? category = null, Color? chatColor = null, int? ownerPlayer = null)
            => Info(message, category, chatColor, ownerPlayer, echoToChat: IsInfoChatEnabled);

        /// <summary>
        /// Queues an info message for the current frame batch, optionally echoing to chat.
        /// </summary>
        public static void Info(
            string message,
            string? category,
            Color? chatColor,
            int? ownerPlayer,
            bool echoToChat)
        {
            Enqueue(
                message,
                category,
                TabLogLevel.Info,
                echoToChat,
                chatColor: chatColor ?? Color.LightYellow,
                ownerPlayer);
        }

        /// <summary>
        /// Logs a warning immediately when the level and category filters allow it.
        /// </summary>
        public static void Warn(string message, string? category = null)
        {
            Flush();
            Log(message, category, TabLogLevel.Warn);
        }

        /// <summary>
        /// Logs an error immediately when the level and category filters allow it.
        /// </summary>
        public static void Error(string message, string? category = null)
        {
            Flush();
            Log(message, category, TabLogLevel.Error);
        }

        /// <summary>
        /// Logs an error with exception immediately when the level and category filters allow it.
        /// </summary>
        public static void Error(string message, Exception ex, string? category = null)
        {
            Flush();

            var mod = _mod;
            if (mod is null || !MeetsThreshold(TabLogLevel.Error) || !ShouldEmitCategory(category))
                return;

            var full = FormatLine(message, category);
            mod.Logger.Error(full, ex);
        }

        /// <summary>
        /// Prints a formatted line to local chat when category and owner filters allow it.
        /// </summary>
        public static void Chat(string message, string? category = null, Color? color = null, int? ownerPlayer = null)
        {
            // --- Dedicated servers have no chat UI; owner-scoped lines only show for that local player.
            if (Main.netMode == NetmodeID.Server || !ShouldEmitCategory(category) || !ShouldShowForOwner(ownerPlayer))
                return;

            string full = FormatLine(message, category);
            Main.NewText(full, color ?? Color.LightYellow);
        }

        /// <summary>
        /// Returns whether the severity is at or above the configured client minimum.
        /// </summary>
        private static bool MeetsThreshold(TabLogLevel level)
        {
            // --- Prefer the live client config; fall back to Info when no client config is loaded.
            TabLogLevel minimum = ClientLogConfig.Instance?.MinimumLogLevel ?? TabLogLevel.Info;
            return level >= minimum;
        }

        /// <summary>
        /// Returns whether owner-scoped client diagnostics should appear for this runtime.
        /// </summary>
        private static bool ShouldShowForOwner(int? ownerPlayer)
        {
            // --- Unscoped lines always pass; scoped lines are client-only and local-owner-only.
            if (ownerPlayer is null)
                return true;

            if (Main.netMode == NetmodeID.Server)
                return false;

            return Main.myPlayer == ownerPlayer.Value;
        }

        /// <summary>
        /// Maps a category string onto the matching client tag toggle.
        /// </summary>
        private static bool ShouldEmitCategory(string? category)
        {
            ClientLogConfig? cfg = ClientLogConfig.Instance;
            if (cfg is null)
                return true;

            return category switch
            {
                GenericCategory => cfg.TagGeneric,
                "Paste" => cfg.TagPaste,
                "Projection" or "ProjectionSystem" => cfg.TagProjection,
                "Selection" or "SelectionSystem" or "Blueprint" => cfg.TagSelection,
                "Layers" => cfg.TagLayers,
                "FrameLayer" => cfg.TagFrameLayer,
                "TileMapping" => cfg.TagTileMapping,
                "InteractionMap" => cfg.TagInteractionMap,
                "PasteNet" => cfg.TagNetworking,
                "ToolAction" => cfg.TagToolAction,
                "Template" => cfg.TagTemplate,
                TileExecutorCategory => cfg.TagTileExecutor,
                InventoryCategory => cfg.TagInventory,
                ManifestCategory => cfg.TagManifest,
                null or "" => cfg.TagOther,
                _ => cfg.TagOther,
            };
        }

        /// <summary>
        /// Appends a debug/info record to the current frame batch, rolling the batch forward when the frame changes.
        /// </summary>
        private static void Enqueue(
            string message,
            string? category,
            TabLogLevel level,
            bool echoToChat,
            Color chatColor,
            int? ownerPlayer)
        {
            if (_mod is null
                || !MeetsThreshold(level)
                || !ShouldEmitCategory(category)
                || !ShouldShowForOwner(ownerPlayer))
                return;

            // --- A new frame started before the previous batch was flushed; flush the stale batch first.
            uint frame = Main.GameUpdateCount;
            if (_batchFrame != uint.MaxValue && frame != _batchFrame)
                Flush();

            _batchFrame = frame;
            _pending.Add(new PendingEntry
            {
                Message = message,
                Category = category,
                Level = level,
                EchoToChat = echoToChat,
                ChatColor = chatColor,
                OwnerPlayer = ownerPlayer,
            });
        }

        /// <summary>
        /// Writes a warn/error line immediately to the matching native logger sink.
        /// </summary>
        private static void Log(string message, string? category, TabLogLevel level)
        {
            var mod = _mod;
            if (mod is null || !MeetsThreshold(level) || !ShouldEmitCategory(category))
                return;

            var full = FormatLine(message, category);

            switch (level)
            {
                case TabLogLevel.Debug: mod.Logger.Debug(full); break;
                case TabLogLevel.Info: mod.Logger.Info(full); break;
                case TabLogLevel.Warn: mod.Logger.Warn(full); break;
                case TabLogLevel.Error: mod.Logger.Error(full); break;
                default: mod.Logger.Info(full); break;
            }
        }

        private static string FormatLine(string message, string? category) =>
            string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
    }
}
