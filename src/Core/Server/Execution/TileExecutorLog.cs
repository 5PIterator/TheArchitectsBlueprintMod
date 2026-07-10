using Microsoft.Xna.Framework;
using TheArchitectsBlueprintMod.Debug;

namespace TheArchitectsBlueprintMod.Core
{
    /// <summary>Concise TileExecutor debug lines: register, projectile, apply.</summary>
    internal static class TileExecutorLog
    {
        internal static void Registered(in TileMutation mutation) =>
            TABLog.Debug(
                $"Register id={mutation.MutationId} {mutation.Kind} ({mutation.World.X},{mutation.World.Y}) projectile={FormatProjectile(mutation.ProjectileMode)}",
                TABLog.TileExecutorCategory);

        internal static void Projectile(in TileMutation mutation, bool fired) =>
            TABLog.Debug(
                $"Projectile id={mutation.MutationId} {mutation.Kind} ({mutation.World.X},{mutation.World.Y}) fired={(fired ? "yes" : "no")}",
                TABLog.TileExecutorCategory);

        internal static void ArrowState(in TileMutation mutation, string state, string? detail = null)
        {
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail={detail}";
            TABLog.Debug(
                $"Arrow id={mutation.MutationId} state={state} mode={FormatProjectile(mutation.ProjectileMode)} ({mutation.World.X},{mutation.World.Y}){suffix}",
                TABLog.TileExecutorCategory);
        }

        internal static void Applied(in TileMutation mutation, bool ok) =>
            TABLog.Debug(
                $"{Verb(mutation.Kind)} id={mutation.MutationId} ({mutation.World.X},{mutation.World.Y}) {(ok ? "ok" : "fail")}",
                TABLog.TileExecutorCategory);

        internal static void ImpactResolved(in TileMutation mutation, bool arrived, bool ok, string? detail = null)
        {
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail={detail}";
            TABLog.Debug(
                $"Impact id={mutation.MutationId} {mutation.Kind} ({mutation.World.X},{mutation.World.Y}) arrived={(arrived ? "yes" : "no")} result={(ok ? "ok" : "fail")}{suffix}",
                TABLog.TileExecutorCategory);
        }

        internal static void LaunchFailed(in TileMutation mutation, string reason) =>
            TABLog.Debug(
                $"LaunchFail id={mutation.MutationId} {mutation.Kind} ({mutation.World.X},{mutation.World.Y}) reason={reason}",
                TABLog.TileExecutorCategory);

        private static string FormatProjectile(PasteProjectileMode? mode) =>
            mode?.ToString() ?? "none";

        private static string Verb(TileMutationKind kind) => kind switch
        {
            TileMutationKind.PlaceTile
            or TileMutationKind.PlaceObject
            or TileMutationKind.PlaceWall
            or TileMutationKind.PlaceWireRed
            or TileMutationKind.PlaceWireBlue
            or TileMutationKind.PlaceWireGreen
            or TileMutationKind.PlaceWireYellow
            or TileMutationKind.PlaceActuator
            or TileMutationKind.ApplyTileFrame
            or TileMutationKind.ApplyPayloadBlock => "Place",
            TileMutationKind.ApplyDetailState
            or TileMutationKind.ApplyPaintState
            or TileMutationKind.SetLiquid => "Mutate",
            TileMutationKind.Sweep => "Sweep",
            _ => "Clear",
        };
    }
}
