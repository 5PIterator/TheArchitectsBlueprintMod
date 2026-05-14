using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsBlueprintMod.Core;

/// <summary>
/// Central place to enumerate and resolve all client <see cref="Blueprint"/> instances for a player:
/// the live <see cref="Selection"/>, every <see cref="Projection"/>, and any additionally registered blueprints.
/// </summary>
public sealed class BlueprintSystem : ModSystem
{
    private enum BlueprintType { Selection, Projection, Additional }
    private sealed record BlueprintEntry(Blueprint blueprint)
    {
        public Blueprint Blueprint { get; } = blueprint;
        public BlueprintType Type { get; } =
            blueprint is Selection ? BlueprintType.Selection :
            blueprint is Projection ? BlueprintType.Projection :
            BlueprintType.Additional;
        public bool Hits(Point point) => Blueprint.Area.Contains(point);
    }

    /// <summary>
    /// Collection of all blueprints for the current client, sorted by depth (topmost last).
    /// </summary>
    private LinkedList<BlueprintEntry> BlueprintCollection { get; set; } = new();

    public void RegisterBlueprint(Blueprint blueprint)
    {
        BlueprintCollection.AddLast(new BlueprintEntry(blueprint));
    }

    public void UnregisterBlueprint(Blueprint blueprint)
    {
        BlueprintCollection.Remove(new BlueprintEntry(blueprint));
    }

    public void BringBlueprintToFront(Blueprint blueprint)
    {
        BlueprintCollection.Remove(new BlueprintEntry(blueprint));
        BlueprintCollection.AddLast(new BlueprintEntry(blueprint));
    }

    LinkedList<BlueprintEntry> GetBlueprintEntries() => BlueprintCollection;
    IEnumerable<BlueprintEntry> GetSelectionEntries() => GetBlueprintEntries().Where(e => e.Type == BlueprintType.Selection);
    IEnumerable<BlueprintEntry> GetProjectionEntries() => GetBlueprintEntries().Where(e => e.Type == BlueprintType.Projection);
    IEnumerable<BlueprintEntry> GetAdditionalEntries() => GetBlueprintEntries().Where(e => e.Type == BlueprintType.Additional);

    public IEnumerable<Blueprint> GetBlueprints() => GetBlueprintEntries().Select(e => e.Blueprint);
    public IEnumerable<Selection> GetSelections() => GetSelectionEntries().Select(e => e.Blueprint as Selection);
    public IEnumerable<Projection> GetProjections() => GetProjectionEntries().Select(e => e.Blueprint as Projection);
    public IEnumerable<Blueprint> GetAdditionalBlueprints() => GetAdditionalEntries().Select(e => e.Blueprint);

    public Blueprint GetTopmostBlueprint() => GetBlueprintEntries().LastOrDefault()?.Blueprint;
    public Selection GetTopmostSelection() => GetSelectionEntries().LastOrDefault()?.Blueprint as Selection;
    public Projection GetTopmostProjection() => GetProjectionEntries().LastOrDefault()?.Blueprint as Projection;

    public bool GetTopmostBlueprintAt(Point cursorTile, out Blueprint? blueprint)
    {
        blueprint = GetBlueprintEntries().LastOrDefault(e => e.Hits(cursorTile))?.Blueprint;
        return blueprint is not null;
    }

    public bool GetTopmostSelectionAt(Point cursorTile, out Selection? selection)
    {
        selection = GetSelectionEntries().LastOrDefault(e => e.Hits(cursorTile))?.Blueprint as Selection;
        return selection is not null;
    }

    public bool GetTopmostProjectionAt(Point cursorTile, out Projection? projection)
    {
        projection = GetProjectionEntries().LastOrDefault(e => e.Hits(cursorTile))?.Blueprint as Projection;
        return projection is not null;
    }

    public override void OnWorldUnload()
    {
        BlueprintCollection.Clear();
    }
}
