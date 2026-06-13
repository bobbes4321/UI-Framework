using System;
using System.Collections.Generic;

namespace AlterEyes.UI
{
    /// <summary>
    /// Object pool for tweens — playback allocates nothing; tweens are recycled by exact type.
    /// </summary>
    public static class TweenPool
    {
        private static readonly Dictionary<Type, Stack<Tween>> Pools = new Dictionary<Type, Stack<Tween>>();

        public static int CountPooled<T>() where T : Tween =>
            Pools.TryGetValue(typeof(T), out Stack<Tween> stack) ? stack.Count : 0;

        public static T Get<T>() where T : Tween, new()
        {
            if (Pools.TryGetValue(typeof(T), out Stack<Tween> stack) && stack.Count > 0)
            {
                var pooled = (T)stack.Pop();
                pooled.MarkIdle();
                return pooled;
            }
            return new T();
        }

        /// <summary> Stops, resets and returns a tween to the pool. </summary>
        public static void Release(Tween tween)
        {
            if (tween == null || tween.isPooled) return;
            tween.Reset();
            tween.MarkPooled();
            Type type = tween.GetType();
            if (!Pools.TryGetValue(type, out Stack<Tween> stack))
            {
                stack = new Stack<Tween>();
                Pools[type] = stack;
            }
            stack.Push(tween);
        }

        public static void Clear() => Pools.Clear();
    }
}
