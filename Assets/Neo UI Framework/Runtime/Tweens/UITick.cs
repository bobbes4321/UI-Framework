using System.Collections.Generic;

namespace Neo.UI
{
    /// <summary> Anything that wants to be driven by the UI tick service (tweens, transitions, timers). </summary>
    public interface ITickable
    {
        /// <param name="deltaTime"> Unscaled seconds since the previous tick. </param>
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Central tick service. Tick sources push time into it:
    /// <see cref="RuntimeHeartbeat"/> in play mode, the editor heartbeat (editor assembly) in edit mode,
    /// and tests tick it manually. Registered tickables may register/unregister freely during a tick.
    /// </summary>
    public static class UITick
    {
        private static readonly List<ITickable> Tickables = new List<ITickable>(64);
        private static readonly List<ITickable> Snapshot = new List<ITickable>(64);

        /// <summary> Raised after every tick (used by the editor to repaint scene views while previewing). </summary>
        public static event System.Action OnTick;

        public static int count => Tickables.Count;

        public static bool IsRegistered(ITickable tickable) => Tickables.Contains(tickable);

        public static void Register(ITickable tickable)
        {
            if (tickable == null || Tickables.Contains(tickable)) return;
            Tickables.Add(tickable);
            OnFirstRegistered?.Invoke();
        }

        public static void Unregister(ITickable tickable)
        {
            Tickables.Remove(tickable);
        }

        /// <summary> Raised when a tickable registers — lets tick sources lazily start. </summary>
        public static event System.Action OnFirstRegistered;

        public static void Tick(float deltaTime)
        {
            if (Tickables.Count == 0)
            {
                OnTick?.Invoke();
                return;
            }

            Snapshot.Clear();
            Snapshot.AddRange(Tickables);
            for (int i = 0; i < Snapshot.Count; i++)
            {
                ITickable t = Snapshot[i];
                // skip tickables removed mid-tick
                if (!Tickables.Contains(t)) continue;
                t.Tick(deltaTime);
            }
            OnTick?.Invoke();
        }

        /// <summary> Removes every registered tickable (test isolation). </summary>
        public static void Clear() => Tickables.Clear();
    }
}
