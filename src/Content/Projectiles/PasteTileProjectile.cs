using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using TheArchitectsBlueprintMod.Core;

namespace TheArchitectsBlueprintMod.Content.Projectiles
{
    internal sealed class PasteTileProjectile : ModProjectile
    {
        private bool _impactCompleted;

        public override string Texture => "Terraria/Images/Projectile_1";

        public void SetVisualItem(int itemType)
        {
            Projectile.damage = itemType;
            Projectile.netUpdate = true;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.timeLeft = 180;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.netImportant = true;
        }

        public override void OnSpawn(IEntitySource source)
        {
            _impactCompleted = false;
        }

        public override void AI()
        {
            if (!TryGetDestination(out Vector2 destination))
                return;

            Vector2 toDestination = destination - Projectile.Center;
            float distance = toDestination.Length();
            if (distance > 0.1f)
            {
                Vector2 dir = toDestination / distance;
                float speed = MathHelper.Clamp(distance * 0.15f, 4f, 20f);
                Projectile.velocity = dir * speed;
            }
            else
            {
                Projectile.velocity = Vector2.Zero;
            }

            if (distance <= 6f)
            {
                TryCompleteImpact();
                Projectile.Kill();
            }
        }

        public override void OnKill(int timeLeft)
        {
            // Projectile slots are reused quickly during bulk clears; complete store/place if killed early.
            TryCompleteImpact();
        }

        private bool TryGetDestination(out Vector2 destination)
        {
            int operationId = (int)Projectile.ai[1];
            int mutationId = (int)Projectile.ai[2];
            if (TileExecutorSystem.TryGetProjectileDestination(operationId, mutationId, out destination))
                return true;

            destination = new Vector2(Projectile.localAI[0], Projectile.localAI[1]);
            return destination != Vector2.Zero;
        }

        private void TryCompleteImpact()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || _impactCompleted)
                return;

            int operationId = (int)Projectile.ai[1];
            int mutationId = (int)Projectile.ai[2];
            if (TileExecutorSystem.TryCompleteProjectileImpact(operationId, mutationId))
                _impactCompleted = true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = Projectile.damage;
            if (itemType <= 0 || itemType >= ItemLoader.ItemCount)
                return true;

            Texture2D texture = TextureAssets.Item[itemType].Value;
            Vector2 origin = texture.Size() * 0.5f;
            Main.EntitySpriteDraw(
                texture,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                0f,
                origin,
                1f,
                SpriteEffects.None);
            return false;
        }
    }
}
