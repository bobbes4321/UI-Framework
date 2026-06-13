using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Play-mode tick source: a hidden singleton MonoBehaviour that pushes unscaled delta time
    /// into <see cref="UITick"/> every frame, with an optional FPS cap.
    /// Created on demand the first time something registers with the tick service in play mode.
    /// </summary>
    [AddComponentMenu("")]
    public class RuntimeHeartbeat : MonoBehaviour
    {
        private static RuntimeHeartbeat s_instance;

        /// <summary> Maximum tick rate. 0 or negative = uncapped (tick every frame). </summary>
        public static int fpsCap = 0;

        private float _accumulated;

        public static RuntimeHeartbeat instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                if (!Application.isPlaying) return null;
                var go = new GameObject("[AlterEyes.UI Heartbeat]");
                go.hideFlags = HideFlags.HideInHierarchy;
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<RuntimeHeartbeat>();
                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UITick.OnFirstRegistered -= EnsureExists;
            UITick.OnFirstRegistered += EnsureExists;
            EnsureExists();
        }

        private static void EnsureExists()
        {
            if (Application.isPlaying) _ = instance;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (fpsCap > 0)
            {
                _accumulated += dt;
                float interval = 1f / fpsCap;
                if (_accumulated < interval) return;
                dt = _accumulated;
                _accumulated = 0f;
            }
            UITick.Tick(dt);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}
