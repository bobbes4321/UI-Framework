using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime no-silent-failure sweep (audit A3 / remediation Task 1.2), play-mode half:
    /// <see cref="NeoSignalParamBinding"/> subscriptions are gated on <c>Application.isPlaying</c>, so
    /// the unknown-param warning needs a live signal dispatch. See the EditMode counterpart of this
    /// file for the <see cref="UIView"/> category-miss and <see cref="AnimationPresetDatabase"/> preset
    /// -miss warnings.
    /// </summary>
    public class NoSilentFailurePlayModeTests : PlayModeTestBase
    {
        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            Signals.ClearAll();
        }

        [UnityTest]
        public IEnumerator SignalParamBinding_UnknownParam_WarnsOnceDespiteTwoSends()
        {
            GameObject go = CreateUIObject("BoundGlow");
            go.AddComponent<NeoShape>();
            go.AddComponent<NeoGlowPulse>();
            var binding = go.AddComponent<NeoSignalParamBinding>();
            binding.Bindings.Add(new NeoSignalParamBinding.ParamBinding
            {
                category = "Effects",
                signalName = "Softness",
                param = "notARealParam",
                min = 0f,
                max = 1f
            });
            // AddComponent already ran OnEnable (against the empty list built at construction time);
            // re-cycle it now that a binding exists so it actually subscribes.
            binding.enabled = false;
            binding.enabled = true;
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("does not recognize param 'notARealParam'"));
            Signals.Send("Effects", "Softness", 0.25f);
            yield return null;
            // A second send with a matching param must NOT log again — the warning fires once per binding.
            Signals.Send("Effects", "Softness", 0.75f);
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }
    }
}
