using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TheArchitectsBlueprintMod.UI;
using TheArchitectsBlueprintMod.UI.TmodGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>
    /// Bridges projection payloads to local payload files and the import/export UI.
    /// </summary>
    public sealed class PayloadSerialization : ModSystem
    {
        public const string PayloadFileExtension = PayloadMetadata.TabExtension;
        public const string LegacyPayloadFileExtension = PayloadMetadata.LegacyJsonExtension;
        private const string PayloadDirectoryName = "Payloads";
        private static readonly Regex InvalidFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);
        private const string BrowserOverlayFamily = "payloadBrowser";
        private const string BrowserConnectorOverlayId = "projectionConnector";

        private static readonly Projection?[] _payloadIoHighlight = new Projection[Main.maxPlayers];
        private static PayloadBrowserSession? _browserSession;

        private sealed class ProjectionSnapshot
        {
            public ProjectionSnapshot(Projection projection, Selection selection, Payload payload, bool cutSource)
            {
                Projection = projection;
                Selection = selection;
                Payload = payload;
                CutSource = cutSource;
            }

            public Projection Projection { get; }
            public Selection Selection { get; set; }
            public Payload Payload { get; set; }
            public bool CutSource { get; set; }
            public bool PreviewDirty { get; set; }
        }

        private sealed class PayloadBrowserSession
        {
            public Dictionary<int, ProjectionSnapshot> Snapshots { get; } = new();
            public Projection? ActiveProjection { get; set; }
        }

        internal static bool TryGetPayloadIoHighlight(Player player, out Projection? projection)
        {
            ArgumentNullException.ThrowIfNull(player);
            int who = player.whoAmI;
            if ((uint)who >= _payloadIoHighlight.Length)
            {
                projection = null;
                return false;
            }

            projection = _payloadIoHighlight[who];
            return projection is not null;
        }

        private static void BeginPayloadIoHighlight(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            int who = projection.Owner;
            if ((uint)who >= _payloadIoHighlight.Length)
                return;

            _payloadIoHighlight[who] = projection;
            projection.UpdateOverlay();
        }

        private static void EndPayloadIoHighlightForOwner(int ownerWhoAmI)
        {
            if ((uint)ownerWhoAmI >= _payloadIoHighlight.Length || _payloadIoHighlight[ownerWhoAmI] is null)
                return;

            _payloadIoHighlight[ownerWhoAmI] = null;
            Player player = Main.player[ownerWhoAmI];
            if (player.active)
                ModContent.GetInstance<ProjectionSystem>().UpdateProjectionOverlay(player);
        }

        public override void OnWorldUnload()
        {
            CloseBrowserSession(commitActivePreview: false);
            Array.Clear(_payloadIoHighlight);
        }

        public static bool OpenBrowser(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            if (!CanUseProjection(projection, requirePayload: false))
                return false;

            CloseBrowserSession(commitActivePreview: false);
            _browserSession = new PayloadBrowserSession();
            return SetActiveBrowserProjection(projection);
        }

        internal static bool HasBrowserSession => _browserSession is not null;

        internal static bool TryGetActiveBrowserProjection(out Projection? projection)
        {
            if (_browserSession?.ActiveProjection is not Projection active)
            {
                projection = null;
                return false;
            }

            projection = active;
            return true;
        }

        internal static bool TryResolveProjectionFromCursor(Player player, out Projection? projection)
        {
            projection = null;
            ArgumentNullException.ThrowIfNull(player);

            Point cursorTile = Terraria.Utils.ToTileCoordinates(Main.MouseWorld);
            if (!ModContent.GetInstance<BlueprintSystem>().GetTopmostBlueprintAt(cursorTile, out Blueprint? blueprint) || blueprint == null)
                return false;

            projection = blueprint switch
            {
                Projection p => p,
                Selection s => ModContent.GetInstance<ProjectionSystem>().NewProjection(player, s, captureWorldPayload: true),
                _ => null
            };

            return projection is not null;
        }

        internal static bool UpdateActiveProjectionFromCursor(Player player)
        {
            if (_browserSession is null || !TryResolveProjectionFromCursor(player, out Projection? projection) || projection is null)
                return false;

            return SetActiveBrowserProjection(projection);
        }

        internal static bool SetActiveBrowserProjection(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            if (_browserSession is null || !CanUseProjection(projection, requirePayload: false))
                return false;

            _browserSession.ActiveProjection = projection;
            EnsureSnapshot(projection);
            BeginPayloadIoHighlight(projection);
            return true;
        }

        internal static bool PreviewImportOnActiveProjection(string fullPath, out string? error)
        {
            error = null;
            if (_browserSession?.ActiveProjection is not Projection projection)
            {
                error = "No active projection selected.";
                return false;
            }

            if (!TryReadPayloadFromFile(fullPath, out Payload payload, out PayloadDocumentInfo docInfo, out error))
                return false;

            EnsureSnapshot(projection);
            projection.SetPayload(payload, docInfo.ModsLoaded);
            projection.RefreshInventories();
            ProjectionSnapshot snapshot = _browserSession.Snapshots[projection.OverlayInstanceKey];
            snapshot.PreviewDirty = true;
            return true;
        }

        internal static bool CommitActiveImportPreview(out string? error)
        {
            error = null;
            if (_browserSession?.ActiveProjection is not Projection projection)
            {
                error = "No active projection selected.";
                return false;
            }

            EnsureSnapshot(projection);
            ProjectionSnapshot snapshot = _browserSession.Snapshots[projection.OverlayInstanceKey];
            snapshot.Selection = CaptureSelectionSnapshot(projection);
            snapshot.Payload = projection.HasPayload ? Payload.FromClone(projection.Payload) : Payload.Empty;
            snapshot.CutSource = projection.CutSource;
            snapshot.PreviewDirty = false;
            return true;
        }

        internal static void CloseBrowserSession(bool commitActivePreview)
        {
            if (_browserSession is null)
            {
                OverlaySystem.UnregisterFamily(BrowserOverlayFamily);
                return;
            }

            if (commitActivePreview)
                CommitActiveImportPreview(out _);

            var touchedOwners = new HashSet<int>();
            foreach (ProjectionSnapshot snapshot in _browserSession.Snapshots.Values)
            {
                touchedOwners.Add(snapshot.Projection.Owner);
                if (snapshot.PreviewDirty)
                    RestoreSnapshot(snapshot);
            }

            foreach (int owner in touchedOwners)
                EndPayloadIoHighlightForOwner(owner);

            _browserSession = null;
            OverlaySystem.UnregisterFamily(BrowserOverlayFamily);
        }

        internal static bool ExportActiveProjection(string projectName, out string? error)
        {
            error = null;
            if (_browserSession?.ActiveProjection is not Projection projection)
            {
                error = "No active projection selected.";
                return false;
            }

            if (!CanUseProjection(projection, requirePayload: true))
            {
                error = "Projection cannot be exported in current state.";
                return false;
            }

            string displayName = string.IsNullOrWhiteSpace(projectName)
                ? BuildDefaultProjectName()
                : projectName.Trim();
            string path = GetAvailablePayloadPath(displayName);
            if (!TryWriteExportedWrapper(projection, displayName, path, out error))
                return false;

            Main.NewText($"Exported payload: {Path.GetFileName(path)}", Color.LightGreen);
            return true;
        }

        internal static string BuildDefaultProjectName() => $"TABPayload{DateTime.Now:yyyyMMdd_HHmmss}";

        internal static void UpdateBrowserConnectorOverlay(Rectangle windowRect)
        {
            if (_browserSession?.ActiveProjection is not Projection projection ||
                !projection.IsDefined ||
                projection.Area.IsEmpty)
            {
                OverlaySystem.UnregisterFamily(BrowserOverlayFamily);
                return;
            }

            if (windowRect.IsEmpty)
                return;

            Rectangle projectionRect = ToScreenRect(projection.Area);
            Point[] projectionCorners = BuildCorners(projectionRect);
            Point[] windowCorners = BuildCorners(windowRect);
            FindNearestCornerPair(windowCorners, projectionCorners, out Point windowCorner, out Point projectionCorner);

            OverlaySystem.RegScreen(
                BrowserConnectorOverlayId,
                () => DrawScreenLine(windowCorner.ToVector2(), projectionCorner.ToVector2(), ArchitectVisualPalette.PayloadIoHighlightColor),
                BrowserOverlayFamily);
        }

        private static void EnsureSnapshot(Projection projection)
        {
            if (_browserSession == null || _browserSession.Snapshots.ContainsKey(projection.OverlayInstanceKey))
                return;

            Selection selection = CaptureSelectionSnapshot(projection);
            Payload payload = projection.HasPayload ? Payload.FromClone(projection.Payload) : Payload.Empty;
            _browserSession.Snapshots[projection.OverlayInstanceKey] =
                new ProjectionSnapshot(projection, selection, payload, projection.CutSource);
        }

        private static Selection CaptureSelectionSnapshot(Projection projection)
        {
            var selection = new Selection(projection.Owner);
            Rectangle sourceArea = projection.SourceArea;
            if (sourceArea.IsEmpty)
                return selection;

            selection.Define(sourceArea);
            if (projection.SourceTiles == null)
                return selection;

            int width = Math.Min(sourceArea.Width, projection.SourceTiles.GetLength(0));
            int height = Math.Min(sourceArea.Height, projection.SourceTiles.GetLength(1));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    selection.SourceTiles[x, y].Included = projection.SourceTiles[x, y].Included;
            }

            return selection;
        }

        private static void RestoreSnapshot(ProjectionSnapshot snapshot)
        {
            Projection projection = snapshot.Projection;
            if (Main.dedServ || Main.gameMenu || !projection.IsDefined)
                return;

            projection.AssignSelection(snapshot.Selection.Clone());
            projection.SetPayload(snapshot.Payload);
            projection.SetCut(snapshot.CutSource);
            projection.RefreshInventories();
            snapshot.PreviewDirty = false;
        }

        private static Rectangle ToScreenRect(Rectangle tileRect)
        {
            int x = (int)(tileRect.Left * 16 - Main.screenPosition.X);
            int y = (int)(tileRect.Top * 16 - Main.screenPosition.Y);
            int w = tileRect.Width * 16;
            int h = tileRect.Height * 16;
            return new Rectangle(x, y, w, h);
        }

        private static Point[] BuildCorners(Rectangle rect) =>
        [
            new Point(rect.Left, rect.Top),
            new Point(rect.Right, rect.Top),
            new Point(rect.Left, rect.Bottom),
            new Point(rect.Right, rect.Bottom),
        ];

        private static void FindNearestCornerPair(Point[] a, Point[] b, out Point nearestA, out Point nearestB)
        {
            nearestA = a[0];
            nearestB = b[0];
            float bestDist = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    float dist = Vector2.DistanceSquared(a[i].ToVector2(), b[j].ToVector2());
                    if (dist >= bestDist)
                        continue;

                    bestDist = dist;
                    nearestA = a[i];
                    nearestB = b[j];
                }
            }
        }

        private static void DrawScreenLine(Vector2 start, Vector2 end, Color color)
        {
            Vector2 edge = end - start;
            float length = edge.Length();
            if (length <= 0.5f)
                return;

            float rotation = (float)Math.Atan2(edge.Y, edge.X);
            Main.spriteBatch.Draw(
                ModUtils.DummyTexture,
                start,
                new Rectangle(0, 0, 1, 1),
                color * 0.85f,
                rotation,
                Vector2.Zero,
                new Vector2(length, 2f),
                SpriteEffects.None,
                0f);
        }

        public static string GetPayloadDirectory()
        {
            string dir = Path.Combine(TileMappingSystem.GetModDirectory(), PayloadDirectoryName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static IReadOnlyList<PayloadImportListEntry> DiscoverPayloadFiles() =>
            PayloadImportListEntry.DiscoverInDirectory(GetPayloadDirectory());

        internal static bool TryWriteExportedWrapper(Projection projection, string projectDisplayName, string fullPath, out string? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(projection);
            try
            {
                if (!projection.HasPayload)
                {
                    error = "Projection has no payload.";
                    return false;
                }

                string displayName = string.IsNullOrWhiteSpace(projectDisplayName)
                    ? $"TABPayload{DateTime.Now:yyyyMMdd_HHmmss}"
                    : projectDisplayName.Trim();
                WriteExportWrapperToFile(projection, displayName, fullPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void WriteExportWrapperToFile(Projection projection, string displayName, string fullPath)
        {
            PayloadExportMetadata meta = BuildExportMetadata(displayName, ResolvePlayerName(projection.Owner), projection.Payload);
            using FileStream stream = File.Create(fullPath);
            PayloadTagIO.WriteDocument(stream, projection.Payload, meta);
        }

        private static PayloadExportMetadata BuildExportMetadata(string displayName, string playerName, Payload payload)
        {
            Texture2D? thumb = PayloadPreview.MakeThumbnail(payload);
            try
            {
                byte[]? png = PayloadTagIO.TryEncodeThumbnailPng(thumb);
                return PayloadTagIO.CreateExportMetadata(displayName, playerName, png);
            }
            finally
            {
                thumb?.Dispose();
            }
        }

        private static string ResolvePlayerName(int ownerWhoAmI)
        {
            if ((uint)ownerWhoAmI >= Main.maxPlayers)
                return string.Empty;

            Player player = Main.player[ownerWhoAmI];
            return player.active ? player.name : string.Empty;
        }

        internal static byte[] BuildExportDocumentBytes(Projection projection, string displayName)
        {
            PayloadExportMetadata meta = BuildExportMetadata(displayName, ResolvePlayerName(projection.Owner), projection.Payload);
            using var ms = new MemoryStream();
            PayloadTagIO.WriteDocument(ms, projection.Payload, meta);
            return ms.ToArray();
        }

        internal static bool TryReadPayloadFromFile(string fullPath, out Payload payload, out string? error) =>
            TryReadPayloadFromFile(fullPath, out payload, out _, out error);

        internal static bool TryReadPayloadFromFile(
            string fullPath,
            out Payload payload,
            out PayloadDocumentInfo documentInfo,
            out string? error)
        {
            payload = Payload.Empty;
            documentInfo = default;
            error = null;
            try
            {
                if (!File.Exists(fullPath))
                {
                    error = $"File not found: {fullPath}";
                    return false;
                }

                if (PayloadMetadata.IsLegacyJsonPath(fullPath))
                {
                    string json = File.ReadAllText(fullPath, Encoding.UTF8);
                    if (!TryParseLegacyJsonDocument(json, out payload, out error))
                        return false;
                    return true;
                }

                using FileStream stream = File.OpenRead(fullPath);
                if (!PayloadTagIO.TryReadDocument(stream, out payload, out documentInfo, out error))
                    return false;

                if (!payload.IsDefined)
                {
                    error = "Document did not contain a defined tile grid.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryParsePayloadDocument(string json, out Payload payload, out string? error)
        {
            payload = Payload.Empty;
            error = null;
            try
            {
                return TryParseLegacyJsonDocument(json, out payload, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryParsePayloadDocument(byte[] bytes, out Payload payload, out PayloadDocumentInfo info, out string? error)
        {
            payload = Payload.Empty;
            info = default;
            error = null;
            try
            {
                using var ms = new MemoryStream(bytes);
                if (PayloadTagIO.TryReadDocument(ms, out payload, out info, out error))
                    return payload.IsDefined;

                ms.Position = 0;
                string text = Encoding.UTF8.GetString(bytes);
                if (TryParseLegacyJsonDocument(text, out payload, out error))
                    return payload.IsDefined;

                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParseLegacyJsonDocument(string json, out Payload payload, out string? error)
        {
            payload = Payload.Empty;
            error = null;
            try
            {
                string payloadJson = ExtractPayloadJson(json);
                payload = Payload.FromJson(payloadJson);
                if (!payload.IsDefined)
                {
                    error = "Document did not contain a defined tile grid.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ExtractPayloadJson(string json)
        {
            JToken root = JToken.Parse(json);
            if (root is JObject obj && obj.TryGetValue("payload", StringComparison.OrdinalIgnoreCase, out JToken? payloadToken))
                return payloadToken.ToString(Formatting.None);

            return root.ToString(Formatting.None);
        }

        private static string GetAvailablePayloadPath(string projectName)
        {
            string safeName = SanitizeFileName(projectName);
            string dir = GetPayloadDirectory();
            string path = Path.Combine(dir, safeName + PayloadFileExtension);
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{safeName}_{suffix}{PayloadFileExtension}");
                suffix++;
            }

            return path;
        }

        private static string SanitizeFileName(string projectName)
        {
            string sanitized = InvalidFileNameChars.Replace(projectName, "_").Trim(' ', '.');
            return string.IsNullOrWhiteSpace(sanitized)
                ? $"TABPayload{DateTime.Now:yyyyMMdd_HHmmss}"
                : sanitized;
        }

        private static bool CanUseProjection(Projection projection, bool requirePayload)
        {
            if (Main.dedServ || Main.gameMenu)
                return false;

            if (!projection.IsDefined || projection.IsLocked)
                return false;

            if (requirePayload && !projection.HasPayload)
            {
                Main.NewText("Export failed: projection has no payload.", Color.OrangeRed);
                return false;
            }

            return true;
        }
    }
}
