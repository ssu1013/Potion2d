using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>容器や小物の断面データ（単位：メートル。x = 半径, y = 高さ）</summary>
    public static class VesselShapes
    {
        /// <summary>背の高い瓶（ベース液体用）</summary>
        public static readonly Vector2[] TallBottle =
        {
            new Vector2(0f, 0f), new Vector2(0.055f, 0f), new Vector2(0.055f, 0f),
            new Vector2(0.06f, 0.008f), new Vector2(0.06f, 0.16f), new Vector2(0.05f, 0.195f),
            new Vector2(0.025f, 0.215f), new Vector2(0.021f, 0.225f), new Vector2(0.021f, 0.262f),
            new Vector2(0.025f, 0.27f),
        };
        public const float TallBottleLiquidHeight = 0.17f;
        public const float TallBottleTop = 0.27f;

        /// <summary>丸底フラスコ（触媒・完成ポーション用）</summary>
        public static readonly Vector2[] RoundFlask = BuildRoundFlask();
        public const float RoundFlaskLiquidHeight = 0.085f;
        public const float RoundFlaskTop = 0.19f;

        /// <summary>広口の壺（主素材用）</summary>
        public static readonly Vector2[] Jar =
        {
            new Vector2(0f, 0f), new Vector2(0.07f, 0f), new Vector2(0.07f, 0f),
            new Vector2(0.076f, 0.01f), new Vector2(0.079f, 0.12f), new Vector2(0.072f, 0.145f),
            new Vector2(0.062f, 0.152f), new Vector2(0.062f, 0.17f),
        };
        public const float JarContentHeight = 0.12f;
        public const float JarTop = 0.17f;

        /// <summary>三角フラスコ（調合用）</summary>
        public static readonly Vector2[] Erlenmeyer =
        {
            new Vector2(0f, 0f), new Vector2(0.14f, 0f), new Vector2(0.14f, 0f),
            new Vector2(0.146f, 0.01f), new Vector2(0.14f, 0.035f), new Vector2(0.062f, 0.215f),
            new Vector2(0.046f, 0.245f), new Vector2(0.045f, 0.33f), new Vector2(0.054f, 0.336f),
            new Vector2(0.054f, 0.346f),
        };
        public const float ErlenmeyerMaxLiquidHeight = 0.235f;
        public const float ErlenmeyerTop = 0.346f;

        /// <summary>大鍋</summary>
        public static readonly Vector2[] Cauldron =
        {
            new Vector2(0f, 0.03f), new Vector2(0.1f, 0.03f), new Vector2(0.17f, 0.07f),
            new Vector2(0.21f, 0.14f), new Vector2(0.218f, 0.2f), new Vector2(0.2f, 0.26f),
            new Vector2(0.19f, 0.275f), new Vector2(0.21f, 0.285f), new Vector2(0.21f, 0.3f),
        };

        /// <summary>樽（上面も閉じている）</summary>
        public static readonly Vector2[] Barrel =
        {
            new Vector2(0f, 0f), new Vector2(0.25f, 0f), new Vector2(0.25f, 0f),
            new Vector2(0.27f, 0.1f), new Vector2(0.29f, 0.35f), new Vector2(0.27f, 0.6f),
            new Vector2(0.25f, 0.7f), new Vector2(0.25f, 0.7f), new Vector2(0f, 0.7f),
        };

        /// <summary>乳鉢（内側も作る）</summary>
        public static readonly Vector2[] Mortar =
        {
            new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.05f, 0f),
            new Vector2(0.075f, 0.035f), new Vector2(0.08f, 0.065f), new Vector2(0.072f, 0.072f),
            new Vector2(0.065f, 0.06f), new Vector2(0.045f, 0.03f), new Vector2(0f, 0.022f),
        };

        /// <summary>麻袋（ころんとした形）</summary>
        public static readonly Vector2[] Sack =
        {
            new Vector2(0f, 0f), new Vector2(0.15f, 0.01f), new Vector2(0.21f, 0.08f),
            new Vector2(0.22f, 0.18f), new Vector2(0.17f, 0.3f), new Vector2(0.07f, 0.36f),
            new Vector2(0.05f, 0.38f), new Vector2(0.08f, 0.43f), new Vector2(0f, 0.44f),
        };

        private static Vector2[] BuildRoundFlask()
        {
            var points = new List<Vector2>();
            const float radius = 0.065f;
            const float centerY = 0.07f;
            for (int k = 0; k <= 10; k++)
            {
                float deg = -90f + k * 14f; // -90°〜+50°
                float rad = deg * Mathf.Deg2Rad;
                float x = Mathf.Cos(rad) * radius;
                if (k == 0) x = 0f;
                points.Add(new Vector2(x, centerY + Mathf.Sin(rad) * radius));
            }
            points.Add(new Vector2(0.022f, 0.135f));
            points.Add(new Vector2(0.02f, 0.14f));
            points.Add(new Vector2(0.02f, 0.185f));
            points.Add(new Vector2(0.024f, 0.19f));
            return points.ToArray();
        }
    }
}
