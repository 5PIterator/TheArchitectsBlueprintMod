using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Config;
using TheArchitectsBlueprintMod.Core;
using TheArchitectsBlueprintMod.Debug;
using TheArchitectsBlueprintMod.Utils;

namespace TheArchitectsBlueprintMod.Content.Projectiles
{
    internal sealed class PasteArrow : ModProjectile
    {
        private const float ArrivalDistance = 6f;

        private bool _arrivedAtDestination;
        private bool _spawnFxPlayed;
        private int _ageTicks;
        private int _timeoutTicks;
        private int _maneuverTicks;
        private float _minSpeed;
        private float _maxSpeed;
        private int _cachedItemType;
        private bool _hasSyncedDestination;
        private Vector2 _syncedDestination;
        private bool _hasSyncedFlightProfile;
        private ArrowFlightProfile _syncedFlightProfile;
        private bool _missingDestinationLogged;

        public override string Texture => "Terraria/Images/Projectile_1";

        public static bool TryFire(TileEffectSession session, TileMutation mutation, in PasteContext ctx) =>
            TileExecutor.TryFirePasteArrow(session, mutation, in ctx);

        public static void ProcessActive(TileEffectSession session) =>
            TileExecutor.ProcessActiveArrows(session);

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = 10;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.timeLeft = 240;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.netImportant = true;
            Projectile.light = 0.45f;
        }

        public override void OnSpawn(IEntitySource source)
        {
            _arrivedAtDestination = false;
            _spawnFxPlayed = false;
            _ageTicks = 0;
            _cachedItemType = 0;
            _hasSyncedDestination = false;
            _syncedDestination = Vector2.Zero;
            _hasSyncedFlightProfile = false;
            _syncedFlightProfile = default;
            _missingDestinationLogged = false;
            ApplyFlightProfile(ResolveFlightProfile());
            RefreshCachedItemType();
        }

        /// <summary>Writes destination, item type, and flight profile for MP clients that lack server sessions.</summary>
        public override void SendExtraAI(BinaryWriter writer)
        {
            // --- Mirror SpawnProjectile localAI plus the authoritative flight profile from the server session.

            writer.Write(Projectile.localAI[0]);
            writer.Write(Projectile.localAI[1]);

            int itemType = Projectile.damage > 0 ? Projectile.damage : (int)Projectile.localAI[2];
            writer.Write(itemType);

            ArrowFlightProfile profile = ResolveFlightProfile();
            writer.Write(profile.TimeoutTicks);
            writer.Write(profile.ManeuverTicks);
            writer.Write(profile.MinSpeed);
            writer.Write(profile.MaxSpeed);
        }

        /// <summary>Restores arrow payload received from the dedicated server over the network.</summary>
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            // --- Populate synced fields so client AI can fly and draw without TileEffectSession.

            _syncedDestination = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            _hasSyncedDestination = _syncedDestination != Vector2.Zero;

            int itemType = reader.ReadInt32();
            if (itemType > 0 && itemType < ItemLoader.ItemCount)
            {
                if (Projectile.damage <= 0)
                    Projectile.damage = itemType;
                Projectile.localAI[2] = itemType;
            }

            _syncedFlightProfile = new ArrowFlightProfile
            {
                TimeoutTicks = reader.ReadInt32(),
                ManeuverTicks = reader.ReadInt32(),
                MinSpeed = reader.ReadSingle(),
                MaxSpeed = reader.ReadSingle(),
            };
            _hasSyncedFlightProfile = true;

            Projectile.localAI[0] = _syncedDestination.X;
            Projectile.localAI[1] = _syncedDestination.Y;

            ApplyFlightProfile(_syncedFlightProfile);
            _cachedItemType = 0;
            RefreshCachedItemType();
        }

        public override void AI()
        {
            _ageTicks++;
            RefreshCachedItemType();

            bool hasDestination = TryGetDestination(out Vector2 destination);
            if (!hasDestination && _ageTicks == 1 && Main.netMode == NetmodeID.MultiplayerClient && !_missingDestinationLogged)
            {
                _missingDestinationLogged = true;
                if (TABLog.IsDebugEnabled)
                {
                    TABLog.Debug(
                        $"PasteArrow missing destination op={(int)Projectile.ai[1]} mut={(int)Projectile.ai[2]}",
                        TABLog.TileExecutorCategory);
                }
            }
            if (_ageTicks >= _timeoutTicks)
            {
                _arrivedAtDestination = false;
                Projectile.Kill();
                return;
            }

            if (!hasDestination)
                return;

            UpdateTrailCache();

            Vector2 toDestination = destination - Projectile.Center;
            float distance = toDestination.Length();
            if (distance <= 0.1f)
            {
                Projectile.velocity = Vector2.Zero;
                _arrivedAtDestination = true;
                Projectile.Kill();
                return;
            }

            int remainingManeuverTicks = Math.Max(1, _maneuverTicks - _ageTicks + 1);
            float desiredSpeed = distance / remainingManeuverTicks;
            float speed = MathHelper.Clamp(desiredSpeed, _minSpeed, _maxSpeed);
            float speedFactor = MathHelper.Clamp((speed - _minSpeed) / Math.Max(0.001f, _maxSpeed - _minSpeed), 0f, 1f);
            Vector2 dir = toDestination / distance;
            Projectile.velocity = dir * speed;

            if (Projectile.velocity.LengthSquared() > 0.01f)
                Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            Projectile.light = MathHelper.Lerp(0.25f, 0.65f, speedFactor);

            if (Main.netMode != NetmodeID.Server)
            {
                if (!_spawnFxPlayed)
                {
                    SpawnBurstFx(Projectile.Center - dir * 8f, intensity: 6, arrival: false);
                    _spawnFxPlayed = true;
                }

                SpawnMotionFx();
            }

            if (distance <= ArrivalDistance)
            {
                _arrivedAtDestination = true;
                Projectile.Kill();
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.netMode != NetmodeID.Server && _arrivedAtDestination)
                SpawnBurstFx(Projectile.Center, intensity: 10, arrival: true);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            int operationId = TileEffectSession.ReadProjectileOperationId(Projectile);
            int mutationId = TileEffectSession.ReadProjectileMutationId(Projectile);
            Point dropTile = new((int)(Projectile.Center.X / 16f), (int)(Projectile.Center.Y / 16f));
            TileExecutorSystem.TryCompleteProjectileImpact(
                    operationId,
                    mutationId,
                    _arrivedAtDestination,
                    dropTile,
                    out _);
        }

        private bool IsPlaceMode() => (int)Projectile.ai[0] == (int)PasteProjectileMode.Place;

        private void UpdateTrailCache()
        {
            for (int i = Projectile.oldPos.Length - 1; i > 0; i--)
            {
                Projectile.oldPos[i] = Projectile.oldPos[i - 1];
                Projectile.oldRot[i] = Projectile.oldRot[i - 1];
            }

            Projectile.oldPos[0] = Projectile.position;
            Projectile.oldRot[0] = Projectile.rotation;
        }

        private void SpawnMotionFx()
        {
            int dustType = IsPlaceMode() ? DustID.BlueCrystalShard : DustID.Enchanted_Gold;
            Color dustColor = IsPlaceMode() ? new Color(120, 200, 255) : new Color(255, 210, 90);

            if (Main.rand.NextBool(2))
            {
                Vector2 spawn = Projectile.Center - Projectile.velocity * 0.35f + Main.rand.NextVector2Circular(4f, 4f);
                Dust dust = Dust.NewDustPerfect(spawn, dustType, -Projectile.velocity * 0.08f + Main.rand.NextVector2Circular(0.6f, 0.6f), 80, dustColor, 0.9f);
                dust.noGravity = true;
                dust.fadeIn = 0.6f;
            }

            if (Main.rand.NextBool(5))
            {
                Dust sparkle = Dust.NewDustPerfect(
                    Projectile.Center,
                    DustID.MagicMirror,
                    Main.rand.NextVector2Circular(0.8f, 0.8f),
                    60,
                    Color.White,
                    0.75f);
                sparkle.noGravity = true;
            }
        }

        private void SpawnBurstFx(Vector2 center, int intensity, bool arrival)
        {
            int dustType = IsPlaceMode() ? DustID.BlueCrystalShard : DustID.Enchanted_Gold;
            Color dustColor = IsPlaceMode() ? new Color(120, 200, 255) : new Color(255, 210, 90);

            for (int i = 0; i < intensity; i++)
            {
                float spread = arrival ? 2.2f : 1.4f;
                Vector2 velocity = Main.rand.NextVector2CircularEdge(spread, spread);
                if (arrival)
                    velocity *= 0.55f;

                Dust dust = Dust.NewDustPerfect(center, dustType, velocity, 50, dustColor, arrival ? 1.1f : 0.85f);
                dust.noGravity = true;
                dust.fadeIn = arrival ? 0.8f : 0.5f;
            }
        }

        private bool TryGetDestination(out Vector2 destination)
        {
            if (_hasSyncedDestination)
            {
                destination = _syncedDestination;
                return true;
            }

            int operationId = TileEffectSession.ReadProjectileOperationId(Projectile);
            int mutationId = TileEffectSession.ReadProjectileMutationId(Projectile);
            if (TileExecutorSystem.TryGetProjectileDestination(operationId, mutationId, out destination))
                return true;

            destination = new Vector2(Projectile.localAI[0], Projectile.localAI[1]);
            return destination != Vector2.Zero;
        }

        private void RefreshCachedItemType()
        {
            if (_cachedItemType > 0)
                return;

            if (!TryResolveItemType(out int itemType))
                return;

            _cachedItemType = itemType;
        }

        private bool TryResolveItemType(out int itemType)
        {
            itemType = Projectile.damage;
            if (itemType <= 0)
                itemType = (int)Projectile.localAI[2];
            if (itemType > 0 && itemType < ItemLoader.ItemCount)
                return true;

            int operationId = TileEffectSession.ReadProjectileOperationId(Projectile);
            int mutationId = TileEffectSession.ReadProjectileMutationId(Projectile);
            if (TileExecutorSystem.TryGetProjectileItemType(operationId, mutationId, out itemType))
                return true;

            itemType = 0;
            return false;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            RefreshCachedItemType();
            int itemType = _cachedItemType;
            // itemType 0 → default wooden-arrow projectile sprite (flight rotation preserved).
            if (itemType <= 0 || itemType >= ItemLoader.ItemCount)
                return true;

            ModUtils.LoadItem(itemType);
            Texture2D texture = TextureAssets.Item[itemType].Value;
            if (texture is null || texture.Width <= 1 || texture.Height <= 1)
                return true;

            Vector2 origin = texture.Size() * 0.5f;
            Main.EntitySpriteDraw(
                texture,
                Projectile.Center - Main.screenPosition,
                null,
                Projectile.GetAlpha(lightColor),
                0f,
                origin,
                1f,
                SpriteEffects.None);
            return false;
        }

        private ArrowFlightProfile ResolveFlightProfile()
        {
            if (_hasSyncedFlightProfile)
                return _syncedFlightProfile;

            int operationId = TileEffectSession.ReadProjectileOperationId(Projectile);
            int mutationId = TileEffectSession.ReadProjectileMutationId(Projectile);
            if (TileExecutorSystem.TryGetArrowFlightProfile(operationId, mutationId, out ArrowFlightProfile profile))
                return profile;

            return TileExecutor.ComputeArrowFlightProfileFromClientConfig();
        }

        private void ApplyFlightProfile(in ArrowFlightProfile profile)
        {
            _timeoutTicks = profile.TimeoutTicks;
            _maneuverTicks = profile.ManeuverTicks;
            _minSpeed = profile.MinSpeed;
            _maxSpeed = profile.MaxSpeed;
        }

    }
}
