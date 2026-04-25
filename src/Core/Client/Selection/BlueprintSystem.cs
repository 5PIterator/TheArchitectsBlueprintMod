using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Core;

/// <summary>
/// Central place to enumerate and resolve all client <see cref="Blueprint"/> instances for a player:
/// the live <see cref="Selection"/>, every <see cref="Projection"/>, and any additionally registered blueprints.
/// </summary>
public sealed class BlueprintSystem : ModSystem
{
    private sealed class PlayerBlueprintState
    {
        public readonly List<Blueprint> AdditionalBlueprints = [];
    }

    private readonly PlayerBlueprintState[] _playerStates = new PlayerBlueprintState[Main.maxPlayers];

    /// <summary>
    /// Bottom-to-top order: world selection (if defined), then projections in list order (older to newer),
    /// then <see cref="RegisterBlueprint"/> entries in append order.
    /// </summary>
    public IEnumerable<Blueprint> EnumerateBlueprints(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        Selection selection = ModContent.GetInstance<SelectionSystem>().GetSelection(player);
        if (selection.IsDefined)
        {
            yield return selection;
        }

        IReadOnlyList<Projection> projections = ModContent.GetInstance<ProjectionSystem>().GetProjections(player);
        for (int i = 0; i < projections.Count; i++)
        {
            yield return projections[i];
        }

        List<Blueprint> extra = GetState(player).AdditionalBlueprints;
        for (int i = 0; i < extra.Count; i++)
        {
            yield return extra[i];
        }
    }

    /// <summary>
    /// Hit order (topmost first): additional blueprints (last registered wins), then projections (newest first), then selection.
    /// </summary>
    public bool TryGetTopmostBlueprintAt(Player player, Point cursorTile, out Blueprint? blueprint)
    {
        ArgumentNullException.ThrowIfNull(player);

        List<Blueprint> extra = GetState(player).AdditionalBlueprints;
        for (int i = extra.Count - 1; i >= 0; i--)
        {
            Blueprint b = extra[i];
            if (Hits(b, cursorTile))
            {
                blueprint = b;
                return true;
            }
        }

        IReadOnlyList<Projection> projections = ModContent.GetInstance<ProjectionSystem>().GetProjections(player);
        for (int i = projections.Count - 1; i >= 0; i--)
        {
            Projection p = projections[i];
            if (Hits(p, cursorTile))
            {
                blueprint = p;
                return true;
            }
        }

        Selection selection = ModContent.GetInstance<SelectionSystem>().GetSelection(player);
        if (Hits(selection, cursorTile))
        {
            blueprint = selection;
            return true;
        }

        blueprint = null;
        return false;
    }

    /// <summary>
    /// Same as <see cref="TryGetTopmostBlueprintAt"/> but only succeeds when the topmost hit is a <see cref="Projection"/>.
    /// </summary>
    public bool TryGetTopmostProjectionAt(Player player, Point cursorTile, out Projection? projection)
    {
        if (!TryGetTopmostBlueprintAt(player, cursorTile, out Blueprint? b) || b is not Projection p)
        {
            projection = null;
            return false;
        }

        projection = p;
        return true;
    }

    /// <summary>
    /// Every <see cref="Blueprint"/> whose bounds contain <paramref name="cursorTile"/>, from bottom to top.
    /// </summary>
    public void CollectBlueprintsAt(Player player, Point cursorTile, List<Blueprint> into)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(into);

        Selection selection = ModContent.GetInstance<SelectionSystem>().GetSelection(player);
        if (Hits(selection, cursorTile))
        {
            into.Add(selection);
        }

        IReadOnlyList<Projection> projections = ModContent.GetInstance<ProjectionSystem>().GetProjections(player);
        for (int i = 0; i < projections.Count; i++)
        {
            if (Hits(projections[i], cursorTile))
            {
                into.Add(projections[i]);
            }
        }

        List<Blueprint> extra = GetState(player).AdditionalBlueprints;
        for (int i = 0; i < extra.Count; i++)
        {
            if (Hits(extra[i], cursorTile))
            {
                into.Add(extra[i]);
            }
        }
    }

    /// <summary>
    /// Registers a blueprint not owned by <see cref="SelectionSystem"/> or <see cref="ProjectionSystem"/> (e.g. a future stencil type).
    /// Last-registered is topmost for <see cref="TryGetTopmostBlueprintAt"/>.
    /// </summary>
    public void RegisterBlueprint(Player player, Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(blueprint);
        GetState(player).AdditionalBlueprints.Add(blueprint);
    }

    public bool UnregisterBlueprint(Player player, Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(blueprint);
        return GetState(player).AdditionalBlueprints.Remove(blueprint);
    }

    public IReadOnlyList<Blueprint> GetAdditionalBlueprints(Player player)
    {
        return GetState(player).AdditionalBlueprints;
    }

    public override void OnWorldUnload()
    {
        for (int i = 0; i < _playerStates.Length; i++)
        {
            _playerStates[i]?.AdditionalBlueprints.Clear();
        }
    }

    private static bool Hits(Blueprint blueprint, Point cursorTile)
    {
        return blueprint.IsDefined && blueprint.Area.Contains(cursorTile);
    }

    private PlayerBlueprintState GetState(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        int index = player.whoAmI;
        if (index < 0 || index >= _playerStates.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(player), $"Invalid player index: {index}");
        }

        return _playerStates[index] ??= new PlayerBlueprintState();
    }
}
