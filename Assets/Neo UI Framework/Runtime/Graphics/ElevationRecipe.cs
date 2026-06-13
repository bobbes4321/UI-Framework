using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// The standardized drop-shadow recipe behind <see cref="ShapeStyle.elevation"/> levels 1-3
    /// (extracted from the original Card recipe, which is level 2). The widget factory builds the
    /// shadow sibling from these numbers so every elevated surface in the project agrees.
    /// </summary>
    public static class ElevationRecipe
    {
        public readonly struct Level
        {
            /// <summary> How far the shadow rect grows past the surface on all sides (px). </summary>
            public readonly float spread;
            /// <summary> Downward displacement of the shadow rect (px). </summary>
            public readonly float drop;
            /// <summary> NeoShape edge softness (px). </summary>
            public readonly float softness;
            /// <summary> Multiplied onto the Shadow token's alpha. </summary>
            public readonly float alphaScale;

            public Level(float spread, float drop, float softness, float alphaScale)
            {
                this.spread = spread;
                this.drop = drop;
                this.softness = softness;
                this.alphaScale = alphaScale;
            }

            /// <summary> Stretch-rect offsets for the shadow sibling. </summary>
            public Vector2 OffsetMin => new Vector2(-spread, -(spread + drop));
            public Vector2 OffsetMax => new Vector2(spread, spread - drop);
        }

        private static readonly Level[] Levels =
        {
            new Level(0f, 0f, 0f, 0f),
            new Level(3f, 2f, 10f, 0.7f),
            new Level(6f, 4f, 18f, 1f),   // the original Card shadow
            new Level(10f, 6f, 28f, 1.2f)
        };

        public const int MaxLevel = 3;

        public static Level Get(int level) => Levels[Mathf.Clamp(level, 0, MaxLevel)];
    }
}
