using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core.TileMapping;
using TheArchitectsBlueprintMod.Debug;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Orchestrates tile mapping compilation across layer sub-systems.
    /// Layers: Wall, Tile, Liquid, Wire, Paint.
    /// Centralizes path resolution for mod output (SourceMaps, debug logs). CompiledMaps are managed by ITRStorage.
    /// </summary>
    public sealed class TileMappingSystem : ModSystem
    {
        private enum QueuedMappingAction
        {
            DiscoverPlacement,
            DiscoverWand,
            DiscoverRubble,
            DiscoverPaint,
            DiscoverPlant,
            DiscoverConversion,
            DiscoverBiome,
            DiscoverShimmer,
            CompileOnly,
            FullDiscoveryAndCompile,
        }

        private const string ModDirName = "TheArchitectsBlueprintMod";
        private const string SourceMapsDir = "SourceMaps";
        internal const string SourceMapExtension = ".itr.json";
        private static readonly Queue<QueuedMappingAction> PendingActions = new();

        public override void Unload()
        {
            PendingActions.Clear();
        }

        /// <summary>
        /// Queues one-shot mapping actions requested from <see cref="TileMappingConfig"/>.
        /// </summary>
        internal static void QueueConfiguredActions(TileMappingConfig cfg)
        {
            if (cfg.RunPlacementDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverPlacement);
            if (cfg.RunWandDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverWand);
            if (cfg.RunRubbleDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverRubble);
            if (cfg.RunPaintDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverPaint);
            if (cfg.RunPlantDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverPlant);
            if (cfg.RunConversionDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverConversion);
            if (cfg.RunBiomeDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverBiome);
            if (cfg.RunShimmerDiscovery)
                PendingActions.Enqueue(QueuedMappingAction.DiscoverShimmer);
            if (cfg.RunCompileNow)
                PendingActions.Enqueue(QueuedMappingAction.CompileOnly);
            if (cfg.RunFullDiscoveryAndCompile)
                PendingActions.Enqueue(QueuedMappingAction.FullDiscoveryAndCompile);
        }

        /// <summary>
        /// Gets the mod's output directory. Creates it if missing.
        /// </summary>
        public static string GetModDirectory()
        {
            var dir = Path.Combine(Main.SavePath, ModDirName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Gets full path for a file in TheArchitectsBlueprintMod root (e.g. debug logs).
        /// </summary>
        public static string GetPath(string fileName) => Path.Combine(GetModDirectory(), fileName);

        /// <summary>Builds a SourceMaps file name with the <c>.itr.json</c> extension.</summary>
        public static string GetSourceMapFileName(string stem) => stem + SourceMapExtension;

        /// <summary>
        /// Gets the SourceMaps directory. Creates it if missing. Discovery output and editable config live here.
        /// </summary>
        public static string GetSourceMapsDirectory()
        {
            var dir = Path.Combine(GetModDirectory(), SourceMapsDir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Gets full path for a file in TheArchitectsBlueprintMod\SourceMaps.
        /// </summary>
        public static string GetSourceMapPath(string fileName)
        {
            return Path.Combine(GetSourceMapsDirectory(), fileName);
        }

        /// <summary>
        /// Reads only format and mapType from the metadata without parsing the full document.
        /// Returns (format, mapType) or (null, null) if metadata is missing or invalid.
        /// </summary>
        public static (string? Format, string? MapType) ReadMetadataOnly(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return (null, null);

                string? format = null;
                string? mapType = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    var propName = reader.GetString();
                    reader.Read();

                    if (string.Equals(propName, "metadata", StringComparison.Ordinal) && reader.TokenType == JsonTokenType.StartObject)
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndObject) break;
                            if (reader.TokenType != JsonTokenType.PropertyName) continue;

                            var name = reader.GetString();
                            reader.Read();

                            if (string.Equals(name, "format", StringComparison.Ordinal))
                                format = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            else if (string.Equals(name, "mapType", StringComparison.Ordinal))
                                mapType = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                            else
                                reader.Skip();
                        }
                        return (format, mapType);
                    }

                    reader.Skip();
                }

                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Finds the first <c>.itr.json</c> SourceMap matching the given metadata. Returns null if none found.
        /// Uses lightweight metadata-only parsing to avoid full document load.
        /// </summary>
        public static string? FindFileByMetadata(string directory, string? format, string? mapType)
        {
            if (!Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, "*" + SourceMapExtension))
            {
                var (fileFormat, fileMapType) = ReadMetadataOnly(path);
                if (MatchesMetadata(fileFormat, fileMapType, format, mapType))
                    return path;
            }
            return null;
        }

        private static bool MatchesMetadata(string? fileFormat, string? fileMapType, string? format, string? mapType)
        {
            if (format != null && !string.Equals(fileFormat, format, StringComparison.OrdinalIgnoreCase))
                return false;
            if (mapType != null && (fileMapType == null || !string.Equals(fileMapType, mapType, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        public override void PostUpdateEverything()
        {
            ProcessQueuedActions();
        }

        private static void ProcessQueuedActions()
        {
            if (PendingActions.Count == 0)
                return;

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                PendingActions.Clear();
                TABLog.Warn("Tile mapping config actions are server/host only in multiplayer.", "TileMapping");
                return;
            }

            if (Main.gameMenu)
                return;

            var mod = ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>();

            while (PendingActions.Count > 0)
            {
                QueuedMappingAction action = PendingActions.Dequeue();
                ExecuteQueuedAction(mod, action);
            }
        }

        private static void ExecuteQueuedAction(Mod mod, QueuedMappingAction action)
        {
            switch (action)
            {
                case QueuedMappingAction.DiscoverPlacement:
                {
                    if (!TryGetDiscoveryPlayer(out Player? player))
                    {
                        TABLog.Warn("Skipped placement discovery from config: no active player available.", "TileMapping");
                        return;
                    }

                    TABLog.Info("Config action: placement discovery.", "TileMapping");
                    PlacementDiscovery.Run(mod, player, allowStepping: false);
                    return;
                }

                case QueuedMappingAction.DiscoverWand:
                {
                    TABLog.Info("Config action: wand discovery.", "TileMapping");
                    WandDiscovery.Run(mod);
                    return;
                }

                case QueuedMappingAction.DiscoverRubble:
                {
                    TABLog.Info("Config action: rubble discovery.", "TileMapping");
                    RubbleDiscovery.Run(mod);
                    return;
                }

                case QueuedMappingAction.DiscoverPaint:
                {
                    TABLog.Info("Config action: paint discovery.", "TileMapping");
                    PaintDiscovery.Run(mod);
                    return;
                }

                case QueuedMappingAction.DiscoverPlant:
                {
                    TABLog.Info("Config action: plant discovery.", "TileMapping");
                    PlantDiscovery.Run(mod);
                    return;
                }

                case QueuedMappingAction.DiscoverConversion:
                {
                    TABLog.Info("Config action: conversion discovery.", "TileMapping");
                    ConversionDiscovery.Run(mod);
                    return;
                }

                case QueuedMappingAction.DiscoverBiome:
                {
                    if (!TryGetDiscoveryPlayer(out Player? player))
                    {
                        TABLog.Warn("Skipped biome discovery from config: no active player available.", "TileMapping");
                        return;
                    }

                    TABLog.Info("Config action: biome discovery.", "TileMapping");
                    BiomeDiscovery.Run(mod, player, allowStepping: false);
                    return;
                }

                case QueuedMappingAction.DiscoverShimmer:
                {
                    if (!TryGetDiscoveryPlayer(out Player? player))
                    {
                        TABLog.Warn("Skipped shimmer discovery from config: no active player available.", "TileMapping");
                        return;
                    }

                    TABLog.Info("Config action: shimmer discovery.", "TileMapping");
                    ShimmerDiscovery.Run(mod, player, allowStepping: false);
                    return;
                }

                case QueuedMappingAction.CompileOnly:
                    TABLog.Info("Config action: compile interaction map.", "TileMapping");
                    Compile(mod);
                    return;

                case QueuedMappingAction.FullDiscoveryAndCompile:
                {
                    if (!TryGetDiscoveryPlayer(out Player? player))
                    {
                        TABLog.Warn("Skipped full discovery from config: no active player available.", "TileMapping");
                        return;
                    }

                    TABLog.Info("Config action: full discovery + compile.", "TileMapping");
                    StartFullDiscovery(mod, player, allowStepping: false, compileAtEnd: true);
                    return;
                }

                default:
                    return;
            }
        }

        /// <summary>
        /// Runs every discovery phase, optionally ending with compile, budgeted across frames (or stick-stepped).
        /// </summary>
        public static void StartFullDiscovery(Mod mod, Player player, bool allowStepping = true, bool compileAtEnd = false)
        {
            // --- Compose one enumerator so a single stepper session owns the whole pipeline.

            ArgumentNullException.ThrowIfNull(mod);
            ArgumentNullException.ThrowIfNull(player);

            (IEnumerator enumerator, Action onCancel) = CreateFullDiscoveryEnumerator(mod, player, compileAtEnd);
            DebugStepper.RunEnumerator(
                enumerator,
                player,
                allowStepping,
                onCancel: onCancel,
                log: msg => TABLog.Debug(msg, "TileMapping"));
        }

        /// <summary>Builds the full discovery enumerator and biome cancel hook.</summary>
        private static (IEnumerator Enumerator, Action OnCancel) CreateFullDiscoveryEnumerator(Mod mod, Player player, bool compileAtEnd)
        {
            // --- Chain each discovery's yields; keep biome ring teardown for mid-run cancel.

            (IEnumerator biomeEnumerator, Action biomeCancel) = BiomeDiscovery.CreateEnumeratorWithCancel(mod, player);
            IEnumerator pipeline = FullDiscoveryPipeline(mod, player, biomeEnumerator, compileAtEnd);
            return (pipeline, biomeCancel);
        }

        /// <summary>Yields each discovery phase in order; compiles when <paramref name="compileAtEnd"/> is true.</summary>
        private static IEnumerator FullDiscoveryPipeline(Mod mod, Player player, IEnumerator biomeEnumerator, bool compileAtEnd)
        {
            // --- Placement first (heavy), then lighter maps, biome/shimmer, then compile.

            TABLog.Info("Discovery: Placement...", "TileMapping");
            IEnumerator placement = PlacementDiscovery.CreateEnumerator(mod, player);
            while (placement.MoveNext())
                yield return placement.Current;

            TABLog.Info("Discovery: Wand...", "TileMapping");
            WandDiscovery.Run(mod);
            yield return null;

            TABLog.Info("Discovery: Rubble...", "TileMapping");
            RubbleDiscovery.Run(mod);
            yield return null;

            TABLog.Info("Discovery: Paint...", "TileMapping");
            PaintDiscovery.Run(mod);
            yield return null;

            TABLog.Info("Discovery: Plant...", "TileMapping");
            PlantDiscovery.Run(mod);
            yield return null;

            TABLog.Info("Discovery: Conversion...", "TileMapping");
            ConversionDiscovery.Run(mod);
            yield return null;

            TABLog.Info("Discovery: Biome...", "TileMapping");
            while (biomeEnumerator.MoveNext())
                yield return biomeEnumerator.Current;

            TABLog.Info("Discovery: Shimmer...", "TileMapping");
            IEnumerator shimmer = ShimmerDiscovery.CreateEnumerator(mod);
            while (shimmer.MoveNext())
                yield return shimmer.Current;

            if (compileAtEnd)
            {
                TABLog.Info("Compiling tile map...", "TileMapping");
                Compile(mod);
                TABLog.Info("Full discovery and compile complete.", "TileMapping");
            }
            else
            {
                TABLog.Info("Full discovery complete.", "TileMapping");
            }
        }

        private static bool TryGetDiscoveryPlayer(out Player? player)
        {
            player = null;

            if (Main.netMode == NetmodeID.Server)
            {
                for (int i = 0; i < Main.maxPlayers; i++)
                {
                    Player p = Main.player[i];
                    if (p is not null && p.active)
                    {
                        player = p;
                        return true;
                    }
                }

                return false;
            }

            int my = Main.myPlayer;
            if (my < 0 || my >= Main.maxPlayers)
                return false;

            Player local = Main.player[my];
            if (local is null || !local.active)
                return false;

            player = local;
            return true;
        }

        /// <summary>
        /// Compiles all layer maps and writes them to JSON.
        /// Loads SourceMaps once, merges by layer, writes CompiledMaps.
        /// Progress is routed through TABLog under the TileMapping category.
        /// </summary>
        public static void Compile(Mod mod)
        {
            if (!TileMappingAuthority.UsesLocalMappingFilesystem())
            {
                TABLog.Warn("Tile map compilation runs only in single-player or on the server / listen host (not on remote multiplayer clients).", "TileMapping");
                return;
            }

            TABLog.Info("Tile map compilation started.", "TileMapping");
            ItrCompiler.Compile(mod);
            TABLog.Info("Tile map compilation complete.", "TileMapping");
        }
    }
}
