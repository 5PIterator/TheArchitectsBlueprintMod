using Terraria;
using Terraria.ModLoader;

namespace ArchitectsStencilMod.Debug
{
    /// <summary>
    /// Runs each frame to process debug stepper input (step/cancel).
    /// </summary>
    internal sealed class DebugStepperSystem : ModSystem
    {
        public override void PostUpdateInput()
        {
            Player player = Main.myPlayer >= 0 && Main.myPlayer < Main.maxPlayers
                ? Main.player[Main.myPlayer]
                : null;
            DebugStepper.DebugStickEquipped = DebugStepper.IsHoldingDebugStick(player);
            DebugStepper.Update();
        }
    }
}
