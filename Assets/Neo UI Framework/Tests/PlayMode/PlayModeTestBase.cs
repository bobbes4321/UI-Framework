using System.Collections;
using System.Collections.Generic;
using Neo.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary> Shared scene scaffolding + cleanup for play-mode tests. </summary>
    public abstract class PlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        protected Canvas canvas;

        [SetUp]
        public virtual void SetUp()
        {
            var canvasGo = Track(new GameObject("TestCanvas", typeof(Canvas), typeof(GraphicRaycaster)));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public virtual void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.Destroy(go);
            _spawned.Clear();

            GameObject popupsCanvas = GameObject.Find("PopupsCanvas");
            if (popupsCanvas != null) Object.Destroy(popupsCanvas);
        }

        protected GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        protected GameObject CreateUIObject(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : canvas.transform, worldPositionStays: false);
            if (parent == null) Track(go);
            return go;
        }

        /// <summary> Waits until the condition is true (or the timeout elapses, failing the test). </summary>
        protected static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds = 5f, string description = "condition")
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"Timed out after {timeoutSeconds}s waiting for: {description}");
                yield return null;
            }
        }

        protected static IEnumerator WaitSeconds(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
