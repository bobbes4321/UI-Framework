using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Rename semantics behind the Design System window's Typography tab (Phase 2.2 of
    /// design-system-cohesion-plan.md): <see cref="TypographyTab.TryRenameTextStyle"/> mutates the
    /// style in place (no remove-then-re-add) and refuses a rename that would collide with an
    /// existing style name.
    /// </summary>
    public class TypographyTabTests
    {
        [Test]
        public void TryRenameTextStyle_RenamesInPlace_KeepingTheSameEntry()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetTextStyle(new TextStyle { name = "Body", size = 24f });
                TextStyle style = theme.GetTextStyle("Body");

                bool renamed = TypographyTab.TryRenameTextStyle(theme, style, "BodyLarge");

                Assert.IsTrue(renamed);
                Assert.AreEqual(1, theme.TextStyles.Count, "rename must not add a second entry");
                Assert.IsNull(theme.GetTextStyle("Body"), "old name must no longer resolve");
                Assert.IsNotNull(theme.GetTextStyle("BodyLarge"));
                Assert.AreEqual(24f, theme.GetTextStyle("BodyLarge").size, "rename must preserve other fields");
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TryRenameTextStyle_RejectsCollisionWithExistingName()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetTextStyle(new TextStyle { name = "Body" });
                theme.SetTextStyle(new TextStyle { name = "Title" });
                TextStyle body = theme.GetTextStyle("Body");

                bool renamed = TypographyTab.TryRenameTextStyle(theme, body, "Title");

                Assert.IsFalse(renamed, "renaming onto an existing style name must be refused");
                Assert.AreEqual("Body", body.name, "the style's name must be left unchanged");
                Assert.AreEqual(2, theme.TextStyles.Count);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TryRenameTextStyle_NoOpOnBlankOrUnchangedName()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                theme.SetTextStyle(new TextStyle { name = "Body" });
                TextStyle body = theme.GetTextStyle("Body");

                Assert.IsFalse(TypographyTab.TryRenameTextStyle(theme, body, "  "));
                Assert.IsFalse(TypographyTab.TryRenameTextStyle(theme, body, "Body"));
                Assert.AreEqual("Body", body.name);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }
    }
}
