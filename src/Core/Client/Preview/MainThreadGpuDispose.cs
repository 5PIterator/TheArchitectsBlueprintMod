using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace TheArchitectsBlueprintMod.Core.Preview
{
    /// <summary>
    /// Queues FNA/XNA graphics resource disposal onto the game main thread.
    /// </summary>
    internal static class MainThreadGpuDispose
    {
        private static readonly List<GraphicsResource> Pending = new();
        private static bool _flushQueued;

        /// <summary>Schedules a GPU resource for disposal on the main thread.</summary>
        public static void Dispose(GraphicsResource? resource)
        {
            // --- Collect the resource, then ensure a single main-thread flush is queued.

            if (resource is null || resource.IsDisposed)
                return;

            // Dedicated servers never own a graphics device.
            if (Main.dedServ)
                return;

            lock (Pending)
            {
                Pending.Add(resource);
                if (_flushQueued)
                    return;

                _flushQueued = true;
            }

            Main.QueueMainThreadAction(Flush);
        }

        /// <summary>Disposes every pending GPU resource; must run on the main thread.</summary>
        public static void Flush()
        {
            // --- Drain the queue under lock, then dispose outside so Dispose cannot re-enter the lock.

            List<GraphicsResource> batch;
            lock (Pending)
            {
                _flushQueued = false;
                if (Pending.Count == 0)
                    return;

                batch = new List<GraphicsResource>(Pending);
                Pending.Clear();
            }

            foreach (GraphicsResource resource in batch)
            {
                if (!resource.IsDisposed)
                    resource.Dispose();
            }
        }
    }
}
