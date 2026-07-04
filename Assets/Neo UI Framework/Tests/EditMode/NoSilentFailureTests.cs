using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Runtime no-silent-failure sweep (audit A3 / remediation Task 1.2): string-addressed lookups
    /// that match nothing must warn instead of quietly no-oping. Covers the category-command miss on
    /// <see cref="UIView"/> and the missing-preset miss on <see cref="AnimationPresetDatabase"/>. The
    /// <see cref="NeoSignalParamBinding"/> unknown-param warning needs play-mode signal dispatch — see
    /// the PlayMode counterpart of this file.
    /// </summary>
    public class NoSilentFailureTests
    {
        [TearDown]
        public void TearDown()
        {
            Signals.ClearAll();
        }

        [Test]
        public void ShowCategory_NoRegisteredView_Warns()
        {
            // UIView.OnEnable is not ExecuteAlways, so no view ever registers in edit mode — the
            // registry is guaranteed empty here, which is exactly the "typo'd/absent category" case
            // the zero-match warning exists for.
            LogAssert.Expect(LogType.Warning, new Regex("no registered view matches category 'NoSuchMenu'"));
            UIView.ShowCategory("NoSuchMenu", instant: true);
        }

        [Test]
        public void HideCategory_NoRegisteredView_Warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no registered view matches category 'NoSuchMenu'"));
            UIView.HideCategory("NoSuchMenu", instant: true);
        }

        [Test]
        public void AnimationPresetDatabase_Get_MissingName_Warns()
        {
            var database = ScriptableObject.CreateInstance<AnimationPresetDatabase>();
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("no preset named 'NoSuchPreset'"));
                Assert.IsNull(database.Get("NoSuchPreset"));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void AnimationPresetDatabase_Contains_MissingName_DoesNotWarn()
        {
            // Contains is a probing existence check — a miss is expected usage, not a bug, so it must
            // stay silent (unlike Get, which is a lookup-or-fail API).
            var database = ScriptableObject.CreateInstance<AnimationPresetDatabase>();
            try
            {
                Assert.IsFalse(database.Contains("NoSuchPreset"));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }
    }
}
