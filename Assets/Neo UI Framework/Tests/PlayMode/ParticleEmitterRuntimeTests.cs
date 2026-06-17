using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime regressions for <see cref="NeoParticleEmitter"/>:
    ///   (1) particles spawn into a layout-ignored child, so an emitter riding a control inside a
    ///       HorizontalLayoutGroup never perturbs the host's layout (the "label jumps as particles
    ///       spawn" bug);
    ///   (2) a drag coefficient out of the documented [0,1] range no longer freezes velocity to zero
    ///       on the first frame (the "single dot at the spawn point" bug).
    /// Behavior tests, not screenshots — per the project's runtime-robustness rules.
    /// </summary>
    public class ParticleEmitterRuntimeTests : PlayModeTestBase
    {
        // Builds a host laid out by a HorizontalLayoutGroup, with a label child and an emitter child,
        // mirroring how the factory wires an emitter onto a button row.
        private (RectTransform label, NeoParticleEmitter emitter) BuildLaidOutHost(float drag)
        {
            GameObject host = CreateUIObject("Host");
            var group = host.AddComponent<HorizontalLayoutGroup>();
            group.childControlWidth = true;
            group.childForceExpandWidth = true;

            GameObject labelGo = CreateUIObject("Label", host.transform);
            labelGo.AddComponent<LayoutElement>().preferredWidth = 120f;
            var label = (RectTransform)labelGo.transform;

            GameObject emitterGo = CreateUIObject("Emitter", host.transform);
            var emitter = emitterGo.AddComponent<NeoParticleEmitter>();
            emitter.BurstCount = 24;
            if (drag > 0f) emitter.AddModule(new DragModule(drag));
            return (label, emitter);
        }

        // The emitter parents every particle to a single "Particles" child it lazily creates.
        private static RectTransform FindParticleContainer(NeoParticleEmitter emitter)
        {
            Transform t = emitter.transform.Find("Particles");
            return t != null ? (RectTransform)t : null;
        }

        [UnityTest]
        public IEnumerator Burst_DoesNotPerturbHostLayout()
        {
            (RectTransform label, NeoParticleEmitter emitter) = BuildLaidOutHost(drag: 0f);
            yield return null; // let the layout group settle

            Vector2 labelBefore = label.anchoredPosition;
            Vector2 labelSizeBefore = label.rect.size;

            emitter.Burst();
            yield return null;
            yield return null; // a couple of frames of simulation

            Assert.AreEqual(labelBefore, label.anchoredPosition,
                "spawning particles must not move the sibling label — the emitter's particles must be layout-ignored");
            Assert.AreEqual(labelSizeBefore, label.rect.size,
                "spawning particles must not resize the host's laid-out children");
            Assert.Greater(emitter.ActiveCount, 0, "the burst should have produced live particles");
        }

        [UnityTest]
        public IEnumerator Particles_SpawnIntoLayoutIgnoredContainer()
        {
            (_, NeoParticleEmitter emitter) = BuildLaidOutHost(drag: 0f);
            emitter.Burst();
            yield return null;

            RectTransform container = FindParticleContainer(emitter);
            Assert.NotNull(container, "the emitter must create a dedicated 'Particles' child to host particles");

            var le = container.GetComponent<LayoutElement>();
            Assert.NotNull(le, "the particle container needs a LayoutElement");
            Assert.IsTrue(le.ignoreLayout, "the particle container must set ignoreLayout so a parent layout group never positions it");

            Assert.Greater(container.childCount, 0, "particles must parent to the container, not to the emitter transform");
        }

        [UnityTest]
        public IEnumerator HighDrag_DoesNotInstantlyFreezeVelocity()
        {
            // drag = 2.0 is out of the documented [0,1] range. Before the clamp fix, (1 - drag) went
            // negative, Max(0,..) = 0, factor = 0 → velocity zeroed frame one and every particle
            // stacked at the spawn point. After the fix, particles must still travel outward.
            (_, NeoParticleEmitter emitter) = BuildLaidOutHost(drag: 2.0f);
            emitter.Burst();
            // Accumulate a slice of real simulation time rather than a fixed frame count: batchmode
            // per-frame deltaTime is tiny, so a few frames barely move the particles. 0.1s lets the
            // (heavily damped) particles still travel measurably; they live 0.6-1.1s so all stay alive.
            yield return new WaitForSeconds(0.1f);

            RectTransform container = FindParticleContainer(emitter);
            Assert.NotNull(container);

            float maxDist = 0f;
            int active = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                if (!child.gameObject.activeSelf) continue; // pooled-and-retired instances stay inactive
                active++;
                maxDist = Mathf.Max(maxDist, ((RectTransform)child).anchoredPosition.magnitude);
            }

            Assert.Greater(active, 0, "particles should still be alive a few frames in");
            Assert.Greater(maxDist, 1f,
                "with the drag clamp, particles must spread away from the spawn origin instead of freezing into a single dot");
        }

        [UnityTest]
        public IEnumerator Burst_Spreads_ParticlesToVariedPositions()
        {
            // Default 360° spread + 300-600 speed: particles should land at clearly different places,
            // not all on top of each other.
            GameObject emitterGo = CreateUIObject("Emitter");
            var emitter = emitterGo.AddComponent<NeoParticleEmitter>();
            emitter.BurstCount = 24;
            emitter.Burst();
            // Accumulate real simulation time, not a fixed frame count: per-frame deltaTime is tiny in
            // batchmode, so two frames leave the 300-600 u/s particles barely separated (real spread, but
            // below the threshold). 0.1s of game time lets them travel far enough to measure reliably,
            // independent of frame rate; particles live 0.6-1.1s so all remain alive.
            yield return new WaitForSeconds(0.1f);

            RectTransform container = FindParticleContainer(emitter);
            Assert.NotNull(container);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            int active = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                active++;
                Vector2 pos = ((RectTransform)child).anchoredPosition;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }

            Assert.Greater(active, 1, "need several live particles to measure spread");
            Vector2 extent = max - min;
            Assert.Greater(extent.x + extent.y, 10f,
                "a burst should scatter particles across a range of positions, not stack them at one point");
        }
    }
}
