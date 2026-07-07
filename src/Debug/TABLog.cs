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
        public const string TileExecutorCategory = "TileExecutor";

        private static Mod? _mod;
        private static uint _batchFrame = uint.MaxValue;
        private static readonly List<PendingEntry> _pending = [];

        private sealed class PendingEntry
        {
            public required string Message { get; init; }
            public string? Category { get; init; }
            public int Level { get; init; }
            public bool EchoToChat { get; init; }
            public Color ChatColor { get; init; }
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
            _mod = null;
            _batchFrame = uint.MaxValue;
            _pending.Clear();
        }

        /// <summary>
        /// Flushes buffered debug/info records for the current frame. Called from <see cref="TABLogSystem"/>.
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

                if (entry.Level == 0)
                    debugLines.AppendLine(line);
                else if (entry.Level == 1)
                    infoLines.AppendLine(line);

                if (entry.EchoToChat)
                    Chat(entry.Message, entry.Category, entry.ChatColor);
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

        /// <summary>
        /// Always true: debug/info log records are always written to mod logs when their category passes <see cref="CategoryFilter"/>.
        /// Kept for existing call sites that guard expensive message construction.
        /// </summary>
        public static bool IsDebugEnabled => ShouldEmitCategory(null) || ShouldEmitCategory(TileExecutorCategory);

        /// <summary>
        /// Queues a debug message for the current frame batch.
        /// </summary>
        public static void Debug(string message, string? category = null)
        {
            Enqueue(message, category, level: 0, echoToChat: IsDebugChatEnabled, chatColor: Color.LightGray);
        }

        /// <summary>
        /// Queues an info message for the current frame batch.
        /// </summary>
        public static void Info(string message, string? category = null, Color? chatColor = null)
        {
            Enqueue(message, category, level: 1, echoToChat: IsInfoChatEnabled, chatColor: chatColor ?? Color.LightYellow);
        }

        /// <summary>
        /// Logs a warning. Always emitted immediately regardless of config.
        /// </summary>
        public static void Warn(string message, string? category = null)
        {
            Flush();
            Log(message, category, level: 2);
        }

        /// <summary>
        /// Logs an error. Always emitted immediately regardless of config.
        /// </summary>
        public static void Error(string message, string? category = null)
        {
            Flush();
            Log(message, category, level: 3);
        }

        /// <summary>
        /// Logs an error with exception. Always emitted immediately regardless of config.
        /// </summary>
        public static void Error(string message, Exception ex, string? category = null)
        {
            Flush();

            var mod = _mod;
            if (mod is null || !ShouldEmitCategory(category))
                return;

            var full = FormatLine(message, category);
            mod.Logger.Error(full, ex);
        }

        public static void Chat(string message, string? category = null, Color? color = null)
        {
            if (Main.netMode == NetmodeID.Server || !ShouldEmitCategory(category))
                return;

            string full = FormatLine(message, category);
            Main.NewText(full, color ?? Color.LightYellow);
        }

        /// <summary>
        /// Appends a debug/info record to the current frame batch, rolling the batch forward when the frame changes.
        /// </summary>
        private static bool ShouldEmitCategory(string? category)
        {
            LogCategoryFilter filter = ClientLogConfig.Instance?.CategoryFilter ?? LogCategoryFilter.All;
            return filter switch
            {
                LogCategoryFilter.TileExecutorOnly => category == TileExecutorCategory,
                _ => true,
            };
        }

        private static void Enqueue(string message, string? category, int level, bool echoToChat, Color chatColor)
        {
            if (_mod is null || !ShouldEmitCategory(category))
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
            });
        }

        private static void Log(string message, string? category, int level)
        {
            var mod = _mod;
            if (mod is null || !ShouldEmitCategory(category))
                return;

            var full = FormatLine(message, category);

            switch (level)
            {
                case 0: mod.Logger.Debug(full); break;
                case 1: mod.Logger.Info(full); break;
                case 2: mod.Logger.Warn(full); break;
                case 3: mod.Logger.Error(full); break;
                default: mod.Logger.Info(full); break;
            }
        }

        private static string FormatLine(string message, string? category) =>
            string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
    }
}
