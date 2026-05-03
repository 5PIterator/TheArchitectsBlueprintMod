using System;
using ArchitectsStencilMod.Config;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core
{
    /// <summary>Per-paste preferences from the client; step size is capped by the server when pasting in multiplayer.</summary>
    internal readonly struct ClientPreferences(bool ignoreItems, bool ignoreInvalid, bool transparentPaste, int pasteStepSize)
    {
        public bool IgnoreItems { get; } = ignoreItems;
        public bool IgnoreInvalid { get; } = ignoreInvalid;
        public bool TransparentPaste { get; } = transparentPaste;
        public int PasteStepSize { get; } = pasteStepSize;

        public static ClientPreferences FromLocalConfigs()
        {
            var client = ModContent.GetInstance<ClientPasteConfig>();
            var server = ModContent.GetInstance<PastingConfig>();
            int step = Math.Max(1, Math.Min(client.PasteStepSize, server.MaxPasteStepSize));
            return new ClientPreferences(client.IgnoreItems, client.IgnoreInvalid, client.TransparentPaste, step);
        }

        /// <summary>Re-clamp step after reading network values so clients cannot exceed the server's cap.</summary>
        public static ClientPreferences FromNetwork(bool ignoreItems, bool ignoreInvalid, bool transparentPaste, int pasteStepSize, PastingConfig serverConfig)
        {
            int step = Math.Max(1, Math.Min(pasteStepSize, serverConfig.MaxPasteStepSize));
            return new ClientPreferences(ignoreItems, ignoreInvalid, transparentPaste, step);
        }
    }

    /// <summary>
    /// Arguments for layer execution; <see cref="ArchitectsStencilMod.Core.Layers.Layer.Place"/> performs placement and calls <see cref="ArchitectsStencilMod.Core.Layers.Layer.Consume"/> when appropriate.
    /// </summary>
    internal readonly struct PasteContext(int playerIndex, Item[]? items, PastingConfig serverConfig, ClientPreferences clientPrefs)
    {
        /// <summary>Player performing the paste; used for placement validation and reach checks.</summary>
        public readonly Player Player = Main.player[playerIndex];
        /// <summary>When true, sweep-related behavior may bypass normal item requirements (debug/cheat path).</summary>
        public readonly bool CheatSweep = serverConfig.CheatSweep;
        /// <summary>When true, skip queuing cells missing items and treat missing-item place failures as done.</summary>
        public readonly bool IgnoreItems = clientPrefs.IgnoreItems;
        /// <summary>When true, skip queuing invalid placements and treat invalid-placement failures as done.</summary>
        public readonly bool IgnoreInvalid = clientPrefs.IgnoreInvalid;
        /// <summary>When true, layers must resolve and consume items via <see cref="Items"/>.</summary>
        public readonly bool NeedsItems = serverConfig.NeedsItems;
        /// <summary>Maximum tiles processed per paste step; clamped to at least 1.</summary>
        public readonly int PasteStepSize = Math.Max(1, clientPrefs.PasteStepSize);
        /// <summary>Delay in milliseconds between paste steps; clamped to non-negative.</summary>
        public readonly int PasteDelayMs = Math.Max(0, serverConfig.PasteDelayMs);
        /// <summary>Flat list of items in the inventory; null skips item checks and consumption.</summary>
        public readonly Item[]? Items = items;
    }
}
