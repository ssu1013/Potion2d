using System;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// プレイヤーの設定（音量・マウス感度・視点の揺れ・画質）。PlayerPrefs に保存される。
    /// 毎フレーム読まれる値もあるので、最初に一度読み込んでメモリに持っておく。
    /// </summary>
    public static class GameSettings
    {
        private const string BgmKey = "pf_bgm_volume";
        private const string SeKey = "pf_se_volume";
        private const string MouseKey = "pf_mouse_sensitivity";
        private const string BobKey = "pf_view_bob";
        private const string QualityKey = "pf_graphics_quality";

        /// <summary>画質の段階（0=軽量 / 1=標準 / 2=高）</summary>
        public const int QualityLow = 0;
        public const int QualityStandard = 1;
        public const int QualityHigh = 2;
        public static readonly string[] QualityNames = { "軽量", "標準", "高" };

        private static bool loaded;
        private static float bgm;
        private static float se;
        private static float mouse;
        private static bool bob;
        private static int quality;

        /// <summary>設定が変わったとき</summary>
        public static event Action Changed;

        public static float BgmVolume
        {
            get { Load(); return bgm; }
            set { Load(); bgm = Mathf.Clamp01(value); PlayerPrefs.SetFloat(BgmKey, bgm); Notify(); }
        }

        public static float SeVolume
        {
            get { Load(); return se; }
            set { Load(); se = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SeKey, se); Notify(); }
        }

        /// <summary>マウス感度（0.2〜5）</summary>
        public static float MouseSensitivity
        {
            get { Load(); return mouse; }
            set { Load(); mouse = Mathf.Clamp(value, 0.2f, 5f); PlayerPrefs.SetFloat(MouseKey, mouse); Notify(); }
        }

        /// <summary>歩くときに視点を上下に揺らすか（酔いやすい人のため、初期値はオフ）</summary>
        public static bool ViewBob
        {
            get { Load(); return bob; }
            set { Load(); bob = value; PlayerPrefs.SetInt(BobKey, bob ? 1 : 0); Notify(); }
        }

        /// <summary>画質（0=軽量 / 1=標準 / 2=高）</summary>
        public static int GraphicsQuality
        {
            get { Load(); return quality; }
            set { Load(); quality = Mathf.Clamp(value, QualityLow, QualityHigh); PlayerPrefs.SetInt(QualityKey, quality); Notify(); }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            bgm = PlayerPrefs.GetFloat(BgmKey, 0.7f);
            se = PlayerPrefs.GetFloat(SeKey, 0.8f);
            mouse = Mathf.Clamp(PlayerPrefs.GetFloat(MouseKey, 2f), 0.2f, 5f);
            bob = PlayerPrefs.GetInt(BobKey, 0) != 0;
            quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualityStandard), QualityLow, QualityHigh);
        }

        /// <summary>ディスクに書き出す（スライダーを動かすたびに書かないよう、設定画面を閉じるときに呼ぶ）</summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        private static void Notify()
        {
            if (Changed != null) Changed();
        }

        // エディタで「ドメインの再読み込みなし」で再生したときも、前回の値が残らないようにする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            Changed = null;
        }
    }
}
