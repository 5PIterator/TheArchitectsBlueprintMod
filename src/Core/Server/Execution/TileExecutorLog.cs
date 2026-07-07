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

        internal static void Applied(in TileMutation mutation, bool ok) =>
            TABLog.Debug(
                $"{Verb(mutation.Kind)} id={mutation.MutationId} ({mutation.World.X},{mutation.World.Y}) {(ok ? "ok" : "fail")}",
                TABLog.TileExecutorCategory);

        private static string FormatProjectile(PasteProjectileMode? mode) =>
            mode?.ToString() ?? "none";

        private static string Verb(TileMutationKind kind) => kind switch
        {
            TileMutationKind.PlaceTile or TileMutationKind.PlaceObject or TileMutationKind.PlaceWall => "Place",
            TileMutationKind.Sweep => "Sweep",
            _ => "Clear",
        };
    }
}
