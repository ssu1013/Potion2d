using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 画質の段階（軽量 / 標準 / 高）を実際の描画設定に反映する。
    ///   軽量 … アンチエイリアスなし・影は近くだけ・画面の仕上げ（ブルーム）なし・飾りの演出なし・60fps上限
    ///   標準 … アンチエイリアス2倍・やわらかい影・ブルームあり・垂直同期
    ///   高   … アンチエイリアス4倍・影を遠くまで高解像度で・ランタンの影もあり
    /// エディタで再生したときは、終了時に元のプロジェクト設定へ戻す（設定ファイルを書き換えないため）。
    /// </summary>
    public static class GraphicsQuality
    {
        private static int applied = -1;

#if UNITY_EDITOR
        private static bool saved;
        private static int savedAA;
        private static ShadowQuality savedShadows;
        private static ShadowResolution savedResolution;
        private static float savedDistance;
        private static int savedCascades;
        private static int savedPixelLights;
        private static int savedVSync;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            applied = -1;
            GameSettings.Changed -= OnSettingsChanged;
            GameSettings.Changed += OnSettingsChanged;
#if UNITY_EDITOR
            Application.quitting -= RestoreEditorSettings;
            Application.quitting += RestoreEditorSettings;
#endif
            Apply(GameSettings.GraphicsQuality);
        }

        private static void OnSettingsChanged()
        {
            if (GameSettings.GraphicsQuality != applied) Apply(GameSettings.GraphicsQuality);
        }

        public static void Apply(int level)
        {
#if UNITY_EDITOR
            if (!saved)
            {
                saved = true;
                savedAA = QualitySettings.antiAliasing;
                savedShadows = QualitySettings.shadows;
                savedResolution = QualitySettings.shadowResolution;
                savedDistance = QualitySettings.shadowDistance;
                savedCascades = QualitySettings.shadowCascades;
                savedPixelLights = QualitySettings.pixelLightCount;
                savedVSync = QualitySettings.vSyncCount;
            }
#endif
            applied = level;
            switch (level)
            {
                case GameSettings.QualityLow:
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowResolution = ShadowResolution.Low;
                    QualitySettings.shadowDistance = 9f;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.pixelLightCount = 3;
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = 60;
                    break;
                case GameSettings.QualityHigh:
                    QualitySettings.antiAliasing = 4;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.High;
                    QualitySettings.shadowDistance = 20f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.pixelLightCount = 6;
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = -1;
                    break;
                default:
                    QualitySettings.antiAliasing = 2;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.shadowDistance = 13f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.pixelLightCount = 4;
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = -1;
                    break;
            }

            bool fancy = level >= GameSettings.QualityStandard;
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.targetTexture != null) continue; // 会話の顔アップ用などは対象外
                cam.allowHDR = fancy;
                cam.allowMSAA = level != GameSettings.QualityLow;
                var post = cam.GetComponent<AtelierPostFX>();
                if (post != null)
                {
                    post.enabled = fancy;
                    post.SetBloomIterations(level == GameSettings.QualityHigh ? 5 : 4);
                }
            }
            QualityFeature.ApplyAll(level);
        }

#if UNITY_EDITOR
        private static void RestoreEditorSettings()
        {
            if (!saved) return;
            saved = false;
            QualitySettings.antiAliasing = savedAA;
            QualitySettings.shadows = savedShadows;
            QualitySettings.shadowResolution = savedResolution;
            QualitySettings.shadowDistance = savedDistance;
            QualitySettings.shadowCascades = savedCascades;
            QualitySettings.pixelLightCount = savedPixelLights;
            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = -1;
        }
#endif
    }
}
