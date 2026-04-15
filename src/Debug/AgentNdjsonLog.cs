using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchitectsStencilMod.Debug
{
    /// <summary>
    /// Session-scoped NDJSON lines for debug ingest (e.g. Cursor debug mode). Safe no-op on I/O failure.
    /// </summary>
    internal static class AgentNdjsonLog
    {
        public const string SessionId = "72b7ee";

        private const string LogFileName = "debug-72b7ee.log";

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>ModSources path under the default Terraria tModLoader Documents layout.</summary>
        public static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games",
                "Terraria",
                "tModLoader",
                "ModSources",
                "ArchitectsStencilMod",
                LogFileName);

        /// <summary>Writes one NDJSON object: sessionId, hypothesisId, location, message, data, timestamp, optional runId.</summary>
        public static void Write<T>(string hypothesisId, string location, string message, T data, string? runId = null)
        {
            try
            {
                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string json = runId is null
                    ? JsonSerializer.Serialize(
                        new
                        {
                            sessionId = SessionId,
                            hypothesisId,
                            location,
                            message,
                            data,
                            timestamp = ts,
                        },
                        Options)
                    : JsonSerializer.Serialize(
                        new
                        {
                            sessionId = SessionId,
                            hypothesisId,
                            location,
                            message,
                            data,
                            runId,
                            timestamp = ts,
                        },
                        Options);

                File.AppendAllText(LogPath, json + "\n");
            }
            catch
            {
                // intentional: debug logging must never throw into game code
            }
        }
    }
}
