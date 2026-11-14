using System;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>フレームレートの上限（ノート PC でファンが回らないよう、初期値は 60）</summary>
    public enum FrameRateMode
    {
        /// <summary>60fps（初期値）。リフレッシュレートが 60 の倍数のモニター（120・240Hz など）では、垂直同期のままちょうど 60 にする</summary>
        Standard = 0,
        /// <summary>モニターに合わせる（垂直同期。144Hz なら 144fps。なめらかだが重い）</summary>
        Monitor = 1,
        /// <summary>30fps（バッテリーで遊ぶとき）</summary>
        Battery = 2,
    }

    /// <summary>
    /// プレイヤーの設定（音量・フレームレート・裏に回ったときの音）。PlayerPrefs に保存される。
    /// 画面モード・ウィンドウの大きさは ScreenModes（Unity が自分で覚えている）。
    /// 毎フレーム読まれる値もあるので、最初に一度読み込んでメモリに持っておく。壊れた値（NaN・範囲外）は初期値に戻す。
    /// 音量：マスター（全体）× 種類ごと（BGM／環境音＝部屋・ランプの火・お湯のコトコト／効果音＝操作の音と手ごたえ）
    /// </summary>
    public static class GameSettings
    {
        private const string MasterKey = "pf_master_volume";
        private const string BgmKey = "pf_bgm_volume";
        private const string AmbientKey = "pf_ambient_volume";
        private const string SeKey = "pf_se_volume";
        private const string FrameRateKey = "pf_frame_rate";
        private const string BackgroundMuteKey = "pf_mute_in_background";

        public const float DefaultMaster = 1f;
        public const float DefaultBgm = 0.7f;
        public const float DefaultSe = 0.8f;

        public static readonly string[] FrameRateNames = { "60 fps", "モニターに合わせる", "30 fps" };

        private static bool loaded;
        private static float master;
        private static float bgm;
        private static float ambient;
        private static float se;
        private static FrameRateMode frameRate;
        private static bool muteInBackground;

        /// <summary>設定が変わったとき</summary>
        public static event Action Changed;

        /// <summary>全体の音量（0〜1。AudioListener の音量になる）</summary>
        public static float MasterVolume
        {
            get { Load(); return master; }
            set { Load(); master = Volume(value, master); PlayerPrefs.SetFloat(MasterKey, master); Notify(); }
        }

        public static float BgmVolume
        {
            get { Load(); return bgm; }
            set { Load(); bgm = Volume(value, bgm); PlayerPrefs.SetFloat(BgmKey, bgm); Notify(); }
        }

        /// <summary>環境音（部屋の音・ランプの火・お湯のコトコト・ガラスが触れ合う音）</summary>
        public static float AmbientVolume
        {
            get { Load(); return ambient; }
            set { Load(); ambient = Volume(value, ambient); PlayerPrefs.SetFloat(AmbientKey, ambient); Notify(); }
        }

        /// <summary>効果音（素材を取る・注ぐ・かき混ぜる・火・瓶詰め・UI）</summary>
        public static float SeVolume
        {
            get { Load(); return se; }
            set { Load(); se = Volume(value, se); PlayerPrefs.SetFloat(SeKey, se); Notify(); }
        }

        public static FrameRateMode FrameRate
        {
            get { Load(); return frameRate; }
            set
            {
                Load();
                frameRate = Enum.IsDefined(typeof(FrameRateMode), value) ? value : FrameRateMode.Standard;
                PlayerPrefs.SetInt(FrameRateKey, (int)frameRate);
                ApplyFrameRate();
                Notify();
            }
        }

        /// <summary>ウィンドウが裏に回ったとき（Alt+Tab・最小化）に、音を止めるか（初期値は「鳴らす」：環境音を聞きながら作業できる）</summary>
        public static bool MuteInBackground
        {
            get { Load(); return muteInBackground; }
            set { Load(); muteInBackground = value; PlayerPrefs.SetInt(BackgroundMuteKey, value ? 1 : 0); Notify(); }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = ReadVolume(MasterKey, DefaultMaster);
            bgm = ReadVolume(BgmKey, DefaultBgm);
            se = ReadVolume(SeKey, DefaultSe);
            // 環境音の音量は、前は効果音と一緒だった。はじめて読むときは効果音の音量を引き継ぐ
            ambient = ReadVolume(AmbientKey, se);
            int fr = PlayerPrefs.GetInt(FrameRateKey, (int)FrameRateMode.Standard);
            frameRate = Enum.IsDefined(typeof(FrameRateMode), fr) ? (FrameRateMode)fr : FrameRateMode.Standard;
            muteInBackground = PlayerPrefs.GetInt(BackgroundMuteKey, 0) != 0;
        }

        private static float ReadVolume(string key, float fallback)
        {
            float v = PlayerPrefs.GetFloat(key, fallback);
            return float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp01(v);
        }

        private static float Volume(float value, float current)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? current : Mathf.Clamp01(value);
        }

        /// <summary>ディスクに書き出す（スライダーを動かすたびに書かないよう、設定画面を閉じるとき・裏に回ったとき・終了するときに呼ぶ）</summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>
        /// フレームレートの上限を反映する。60fps のときは、モニターのリフレッシュレートが 60 の倍数なら垂直同期の間引き（画面の裂け目が出ない）、
        /// そうでなければ垂直同期を切って 60fps で止める
        /// </summary>
        public static void ApplyFrameRate()
        {
            switch (FrameRate)
            {
                case FrameRateMode.Monitor:
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = -1;
                    break;
                case FrameRateMode.Battery:
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = 30;
                    break;
                default:
                    double hz = Screen.currentResolution.refreshRateRatio.value;
                    int divider = hz > 1.0 ? (int)Math.Round(hz / 60.0) : 0;
                    if (divider >= 1 && divider <= 4 && Math.Abs(hz / divider - 60.0) <= 2.5)
                    {
                        QualitySettings.vSyncCount = divider;
                        Application.targetFrameRate = -1;
                    }
                    else
                    {
                        QualitySettings.vSyncCount = 0;
                        Application.targetFrameRate = 60;
                    }
                    break;
            }
        }

        private static void Notify()
        {
            if (Changed != null) Changed();
        }

        /// <summary>起動したら、すぐにフレームレートの上限を入れる（タイトルの間から軽く動くように）</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnBoot()
        {
            ApplyFrameRate();
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
