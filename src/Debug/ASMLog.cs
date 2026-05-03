using System;
using ArchitectsStencilMod.Config;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Debug
{
    /// <summary>
    /// Centralized logging for the mod. Uses tModLoader's Mod.Logger (log4net).
    /// Logs go to Documents/My Games/Terraria/tModLoader/Logs/Mods/.
    /// Initialize in mod Load(); Unload in mod Unload().
    /// </summary>
    internal static class ASMLog
    {
        private static Mod? _mod;

        /// <summary>
        /// Call from ArchitectsStencilMod.Load(). Required before any logging.
        /// </summary>
        public static void Initialize(Mod mod)
        {
            _mod = mod;
        }

        /// <summary>
        /// Call from ArchitectsStencilMod.Unload(). Clears the Mod reference.
        /// </summary>
        public static void Unload()
        {
            _mod = null;
        }

        /// <summary>
        /// True when debug logging is enabled (<see cref="ClientLogConfig.ShowDebugLogs"/>). Use to avoid expensive string builds.
        /// </summary>
        public static bool IsDebugEnabled => ClientLogConfig.Instance?.ShowDebugLogs ?? false;

        /// <summary>
        /// Logs a debug message. Only emitted when client debug logs are enabled.
        /// </summary>
        public static void Debug(string message, string? category = null)
        {
            if (!IsDebugEnabled) return;
            Log(message, category, level: 0);
        }

        /// <summary>
        /// Logs an info message. Only emitted when client debug logs are enabled.
        /// </summary>
        public static void Info(string message, string? category = null)
        {
            if (!IsDebugEnabled) return;
            Log(message, category, level: 1);
            Chat(message, category);
        }

        /// <summary>
        /// Logs a warning. Always emitted regardless of config.
        /// </summary>
        public static void Warn(string message, string? category = null)
        {
            Log(message, category, level: 2);
        }

        /// <summary>
        /// Logs an error. Always emitted regardless of config.
        /// </summary>
        public static void Error(string message, string? category = null)
        {
            Log(message, category, level: 3);
        }

        /// <summary>
        /// Logs an error with exception. Always emitted regardless of config.
        /// </summary>
        public static void Error(string message, Exception ex, string? category = null)
        {
            var mod = _mod;
            if (mod is null) return;

            var full = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
            mod.Logger.Error(full, ex);
        }

        public static void Chat(string message, string? category = null)
        {
            if (!IsDebugEnabled) return;
            if (Main.netMode == NetmodeID.Server) return;

            string full = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
            Main.NewText(full, Color.LightYellow);
        }

        private static void Log(string message, string? category, int level)
        {
            var mod = _mod;
            if (mod is null) return;

            var full = string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";

            switch (level)
            {
                case 0: mod.Logger.Debug(full); break;
                case 1: mod.Logger.Info(full); break;
                case 2: mod.Logger.Warn(full); break;
                case 3: mod.Logger.Error(full); break;
                default: mod.Logger.Info(full); break;
            }
        }
    }
}
