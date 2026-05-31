using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Core
{
    public enum PayloadSortMode
    {
        ModifiedDesc,
        ModifiedAsc,
        NameAsc,
        NameDesc,
        SizeDesc
    }

    public enum PayloadFilterMode
    {
        All,
        ValidOnly,
        InvalidOnly,
        MissingMods,
        LegacyOnly
    }

    public enum PayloadExportFormat
    {
        CompactTab,
        ReadableJson,
    }

    /// <summary>Payload detail panel fields for the browser UI.</summary>
    public readonly record struct PayloadBrowserPanelState(
        bool HasProjection,
        string ProjectName,
        string AuthorName,
        byte[]? EntryThumbnailPng,
        bool MakeThumbnailFromPayload,
        IReadOnlyDictionary<string, string> Metadata);

    /// <summary>Client-side payload browser session: projection preview, file catalog, and I/O actions.</summary>
    public sealed class PayloadBrowser
    {
        private static PayloadBrowser? _active;
        private static readonly Projection?[] _payloadIoHighlight = new Projection[Main.maxPlayers];

        private readonly List<PayloadImportListEntry> _allEntries = new();
        private readonly List<PayloadImportListEntry> _filteredEntries = new();
        private readonly Dictionary<int, ProjectionSnapshot> _snapshots = new();

        private Projection? _activeProjection;
        private string _searchText = string.Empty;
        private PayloadSortMode _sortMode = PayloadSortMode.ModifiedDesc;
        private PayloadFilterMode _filterMode = PayloadFilterMode.All;
        private bool _exportAsReadableJson;
        private string? _previewedEntryPath;
        private int _panelRevision;

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

        public static bool HasActive => _active is not null;

        public static bool TryGetActive(out PayloadBrowser? browser)
        {
            browser = _active;
            return browser is not null;
        }

        public IReadOnlyList<PayloadImportListEntry> Entries => _filteredEntries;

        public int EntryCount => _filteredEntries.Count;

        public PayloadSortMode SortMode => _sortMode;

        public PayloadFilterMode FilterMode => _filterMode;

        public bool ExportAsReadableJson
        {
            get => _exportAsReadableJson;
            set => _exportAsReadableJson = value;
        }

        public int PanelRevision => _panelRevision;

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

        public static PayloadBrowser? Open(Projection? projection = null)
        {
            Close(commitActivePreview: false);
            var browser = new PayloadBrowser();
            _active = browser;

            if (projection is not null && CanUseProjection(projection, requirePayload: false))
                browser.SetActiveProjection(projection);
            else
                browser.SyncActiveProjectionFromTopmost();

            browser.ReloadEntries();
            return browser;
        }

        public static void Close(bool commitActivePreview = false)
        {
            if (_active is null)
                return;

            _active.CloseSession(commitActivePreview);
            _active = null;
        }

        public void SetSearch(string text)
        {
            _searchText = text ?? string.Empty;
            ApplyEntryQuery();
        }

        public void SetSort(PayloadSortMode mode)
        {
            _sortMode = mode;
            ApplyEntryQuery();
        }

        public void SetFilter(PayloadFilterMode mode)
        {
            _filterMode = mode;
            ApplyEntryQuery();
        }

        public void CycleSort()
        {
            _sortMode = _sortMode switch
            {
                PayloadSortMode.ModifiedDesc => PayloadSortMode.ModifiedAsc,
                PayloadSortMode.ModifiedAsc => PayloadSortMode.NameAsc,
                PayloadSortMode.NameAsc => PayloadSortMode.NameDesc,
                PayloadSortMode.NameDesc => PayloadSortMode.SizeDesc,
                _ => PayloadSortMode.ModifiedDesc
            };
            ApplyEntryQuery();
        }

        public void CycleFilter()
        {
            _filterMode = _filterMode switch
            {
                PayloadFilterMode.All => PayloadFilterMode.ValidOnly,
                PayloadFilterMode.ValidOnly => PayloadFilterMode.InvalidOnly,
                PayloadFilterMode.InvalidOnly => PayloadFilterMode.MissingMods,
                PayloadFilterMode.MissingMods => PayloadFilterMode.LegacyOnly,
                _ => PayloadFilterMode.All
            };
            ApplyEntryQuery();
        }

        public void ReloadEntries()
        {
            _allEntries.Clear();
            _allEntries.AddRange(PayloadImportListEntry.DiscoverInDirectory(PayloadSerialization.GetPayloadDirectory()));
            ApplyEntryQuery();
        }

        public void SyncActiveProjectionFromTopmost()
        {
            Projection? topmost = ModContent.GetInstance<BlueprintSystem>().GetTopmostProjection();
            if (topmost is null || !CanUseProjection(topmost, requirePayload: false))
            {
                ClearActiveProjection();
                return;
            }

            if (ReferenceEquals(_activeProjection, topmost))
                return;

            SetActiveProjection(topmost);
        }

        public bool TryGetActiveProjection(out Projection? projection)
        {
            if (_activeProjection is not Projection active)
            {
                projection = null;
                return false;
            }

            projection = active;
            return true;
        }

        public bool TryBuildPanelState(out PayloadBrowserPanelState state)
        {
            if (_activeProjection is not Projection projection)
            {
                state = default;
                return false;
            }

            PayloadImportListEntry? previewEntry = FindPreviewedEntry();
            state = new PayloadBrowserPanelState(
                HasProjection: true,
                ProjectName: previewEntry?.DisplayName ?? BuildDefaultProjectName(),
                AuthorName: previewEntry?.PlayerName
                    ?? (projection.HasPayload ? Main.LocalPlayer.name : PayloadMetadata.UnknownPlayer),
                EntryThumbnailPng: previewEntry?.ThumbnailPng,
                MakeThumbnailFromPayload: previewEntry?.ThumbnailPng is not { Length: > 0 } && projection.HasPayload,
                Metadata: BuildMetadata(projection, previewEntry));
            return true;
        }

        public static PayloadBrowserPanelState EmptyPanelState => new(
            HasProjection: false,
            ProjectName: string.Empty,
            AuthorName: PayloadMetadata.UnknownPlayer,
            EntryThumbnailPng: null,
            MakeThumbnailFromPayload: false,
            Metadata: new Dictionary<string, string>());

        public bool TrySelectEntry(PayloadImportListEntry entry, out string? error)
        {
            error = null;
            if (!entry.IsValid)
                return false;

            if (string.Equals(_previewedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                CancelPreview();
                return true;
            }

            if (_activeProjection is not Projection projection)
            {
                error = "No active projection selected.";
                return false;
            }

            if (!PayloadSerialization.TryReadPayloadFromFile(entry.FullPath, out Payload payload, out PayloadDocumentInfo docInfo, out error))
                return false;

            EnsureSnapshot(projection);
            projection.SetPayload(payload, docInfo.ModsLoaded);
            projection.RefreshInventories();
            _snapshots[projection.OverlayInstanceKey].PreviewDirty = true;
            _previewedEntryPath = entry.FullPath;
            BumpPanel();
            return true;
        }

        public void CancelPreview()
        {
            RevertActiveImportPreview();
            _previewedEntryPath = null;
            BumpPanel();
        }

        public bool CommitImport(out string? error)
        {
            error = null;
            if (_activeProjection is not Projection projection)
            {
                error = "No active projection selected.";
                return false;
            }

            EnsureSnapshot(projection);
            ProjectionSnapshot snapshot = _snapshots[projection.OverlayInstanceKey];
            snapshot.Selection = CaptureSelectionSnapshot(projection);
            snapshot.Payload = projection.HasPayload ? Payload.FromClone(projection.Payload) : Payload.Empty;
            snapshot.CutSource = projection.CutSource;
            snapshot.PreviewDirty = false;
            _previewedEntryPath = null;
            BumpPanel();
            return true;
        }

        public bool Export(string projectName, out string? error)
        {
            error = null;
            if (_activeProjection is not Projection projection)
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
            string extension = _exportAsReadableJson
                ? PayloadSerialization.ReadablePayloadFileExtension
                : PayloadSerialization.PayloadFileExtension;
            string path = GetAvailablePayloadPath(displayName, extension);
            PayloadExportFormat format = _exportAsReadableJson
                ? PayloadExportFormat.ReadableJson
                : PayloadExportFormat.CompactTab;
            if (!PayloadSerialization.TryWriteExportedWrapper(projection, displayName, path, format, out error))
                return false;

            Main.NewText($"Exported payload: {Path.GetFileName(path)}", Color.LightGreen);
            ReloadEntries();
            return true;
        }

        public static string BuildDefaultProjectName() => $"TABPayload{DateTime.Now:yyyyMMdd_HHmmss}";

        public static string BuildEntryHoverText(PayloadImportListEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine(entry.DisplayName);
            sb.AppendLine($"Author: {entry.PlayerName}");

            if (entry.Width > 0 && entry.Height > 0)
                sb.AppendLine($"Size: {entry.Width}x{entry.Height}");
            if (!string.IsNullOrWhiteSpace(entry.CreatedUtc))
                sb.AppendLine($"Created: {entry.CreatedUtc}");
            if (!string.IsNullOrWhiteSpace(entry.ModifiedUtc))
                sb.AppendLine(entry.ModifiedUtc);
            if (entry.ModsLoaded is { Count: > 0 })
            {
                IReadOnlyList<string> missing = PayloadCompatibility.ComputeMissingMods(entry.ModsLoaded);
                if (missing.Count > 0)
                    sb.AppendLine($"Missing mods: {string.Join(", ", missing)}");
            }

            sb.AppendLine(Path.GetFileName(entry.FullPath));
            if (!entry.IsValid && !string.IsNullOrWhiteSpace(entry.ErrorSummary))
                sb.Append(entry.ErrorSummary);

            return sb.ToString();
        }

        private void CloseSession(bool commitActivePreview)
        {
            if (commitActivePreview)
                CommitImport(out _);

            var touchedOwners = new HashSet<int>();
            foreach (ProjectionSnapshot snapshot in _snapshots.Values)
            {
                touchedOwners.Add(snapshot.Projection.Owner);
                if (snapshot.PreviewDirty)
                    RestoreSnapshot(snapshot);
            }

            foreach (int owner in touchedOwners)
                EndPayloadIoHighlightForOwner(owner);

            _snapshots.Clear();
            _activeProjection = null;
            _previewedEntryPath = null;
        }

        private void ApplyEntryQuery()
        {
            _filteredEntries.Clear();
            _filteredEntries.AddRange(ApplyQuery(_allEntries, _searchText, _sortMode, _filterMode));
        }

        private static List<PayloadImportListEntry> ApplyQuery(
            IReadOnlyList<PayloadImportListEntry> source,
            string searchText,
            PayloadSortMode sort,
            PayloadFilterMode filter)
        {
            IEnumerable<PayloadImportListEntry> query = source;

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string needle = searchText.Trim();
                query = query.Where(entry => MatchesSearch(entry, needle));
            }

            query = filter switch
            {
                PayloadFilterMode.ValidOnly => query.Where(e => e.IsValid),
                PayloadFilterMode.InvalidOnly => query.Where(e => !e.IsValid),
                PayloadFilterMode.LegacyOnly => query.Where(e => e.IsLegacy),
                PayloadFilterMode.MissingMods => query.Where(HasMissingMods),
                _ => query
            };

            return Sort(query, sort).ToList();
        }

        private static bool MatchesSearch(PayloadImportListEntry entry, string needle) =>
            Contains(entry.DisplayName, needle)
            || Contains(entry.PlayerName, needle);

        private static bool Contains(string? haystack, string needle) =>
            haystack != null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        private static bool HasMissingMods(PayloadImportListEntry entry)
        {
            if (entry.ModsLoaded is not { Count: > 0 })
                return false;

            return PayloadCompatibility.ComputeMissingMods(entry.ModsLoaded).Count > 0;
        }

        private static IEnumerable<PayloadImportListEntry> Sort(
            IEnumerable<PayloadImportListEntry> query,
            PayloadSortMode sort) =>
            sort switch
            {
                PayloadSortMode.ModifiedAsc => query.OrderBy(GetModifiedUtc),
                PayloadSortMode.NameAsc => query.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase),
                PayloadSortMode.NameDesc => query.OrderByDescending(e => e.DisplayName, StringComparer.OrdinalIgnoreCase),
                PayloadSortMode.SizeDesc => query.OrderByDescending(e => e.Width * e.Height),
                _ => query.OrderByDescending(GetModifiedUtc)
            };

        private static DateTime GetModifiedUtc(PayloadImportListEntry entry)
        {
            try
            {
                return File.GetLastWriteTimeUtc(entry.FullPath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private PayloadImportListEntry? FindPreviewedEntry()
        {
            if (string.IsNullOrWhiteSpace(_previewedEntryPath))
                return null;

            for (int i = 0; i < _filteredEntries.Count; i++)
            {
                if (string.Equals(_filteredEntries[i].FullPath, _previewedEntryPath, StringComparison.OrdinalIgnoreCase))
                    return _filteredEntries[i];
            }

            return null;
        }

        private static IReadOnlyDictionary<string, string> BuildMetadata(
            Projection projection,
            PayloadImportListEntry? previewEntry)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (previewEntry is PayloadImportListEntry entry)
            {
                TryAdd(metadata, PayloadMetadataKeys.CreatedUtc, entry.CreatedUtc);
                TryAdd(metadata, PayloadMetadataKeys.Modified, entry.ModifiedUtc);
                TryAdd(metadata, PayloadMetadataKeys.ModVersion, entry.ModVersion);
                TryAdd(metadata, PayloadMetadataKeys.TerrariaVersion, entry.TerrariaVersion);
                if (entry.Width > 0 && entry.Height > 0)
                    TryAdd(metadata, PayloadMetadataKeys.Dimensions, $"{entry.Width}x{entry.Height}");
                if (entry.ModsLoaded is { Count: > 0 })
                    TryAdd(metadata, PayloadMetadataKeys.ModsLoaded, string.Join(", ", entry.ModsLoaded));
                return metadata;
            }

            if (projection.HasPayload)
            {
                TryAdd(
                    metadata,
                    PayloadMetadataKeys.Dimensions,
                    $"{projection.Payload.Width}x{projection.Payload.Height}");
            }

            TryAdd(
                metadata,
                PayloadMetadataKeys.ModVersion,
                ModContent.GetInstance<global::TheArchitectsBlueprintMod.TheArchitectsBlueprintMod>().Version.ToString());
            TryAdd(metadata, PayloadMetadataKeys.TerrariaVersion, Main.versionNumber);
            return metadata;
        }

        private static void TryAdd(Dictionary<string, string> metadata, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            metadata[key] = value.Trim();
        }

        private void RevertActiveImportPreview()
        {
            if (_activeProjection is not Projection projection)
                return;

            if (!_snapshots.TryGetValue(projection.OverlayInstanceKey, out ProjectionSnapshot snapshot) || !snapshot.PreviewDirty)
                return;

            RestoreSnapshot(snapshot);
            snapshot.PreviewDirty = false;
        }

        private void SetActiveProjection(Projection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            if (!CanUseProjection(projection, requirePayload: false))
                return;

            if (_activeProjection is Projection previous && !ReferenceEquals(previous, projection))
            {
                RevertProjectionPreviewIfDirty(previous);
                EndPayloadIoHighlightForOwner(previous.Owner);
                _previewedEntryPath = null;
            }

            _activeProjection = projection;
            EnsureSnapshot(projection);
            BeginPayloadIoHighlight(projection);
            BumpPanel();
        }

        private void ClearActiveProjection()
        {
            if (_activeProjection is not Projection active)
                return;

            RevertProjectionPreviewIfDirty(active);
            EndPayloadIoHighlightForOwner(active.Owner);
            _activeProjection = null;
            _previewedEntryPath = null;
            BumpPanel();
        }

        private void RevertProjectionPreviewIfDirty(Projection projection)
        {
            if (!_snapshots.TryGetValue(projection.OverlayInstanceKey, out ProjectionSnapshot snapshot) || !snapshot.PreviewDirty)
                return;

            RestoreSnapshot(snapshot);
            snapshot.PreviewDirty = false;
        }

        private void EnsureSnapshot(Projection projection)
        {
            if (_snapshots.ContainsKey(projection.OverlayInstanceKey))
                return;

            Selection selection = CaptureSelectionSnapshot(projection);
            Payload payload = projection.HasPayload ? Payload.FromClone(projection.Payload) : Payload.Empty;
            _snapshots[projection.OverlayInstanceKey] =
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

        private static string GetAvailablePayloadPath(string projectName, string extension)
        {
            string safeName = SanitizeFileName(projectName);
            string dir = PayloadSerialization.GetPayloadDirectory();
            string path = Path.Combine(dir, safeName + extension);
            int suffix = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{safeName}_{suffix}{extension}");
                suffix++;
            }

            return path;
        }

        private static string SanitizeFileName(string projectName)
        {
            string sanitized = PayloadSerialization.SanitizeFileName(projectName);
            return string.IsNullOrWhiteSpace(sanitized)
                ? BuildDefaultProjectName()
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

        private void BumpPanel() => _panelRevision++;
    }
}
