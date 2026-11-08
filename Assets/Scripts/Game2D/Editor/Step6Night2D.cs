using System.Collections.Generic;
using System.IO;
using System.Text;
using PotionFrontier.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Step6：夜の空気感（ソロキャンプの夜のように、一人で静かに楽しむ）。
    ///   ・照明：部屋全体の光（GlobalLight）を深い夜の暗さに。月明かりは青く控えめに。
    ///           手元の灯り（FocusLight）・ランプ・加熱台は温かいオレンジにして、ゆっくりゆらぐ → 手元の液体と湯気だけがポワッと照らされる
    ///   ・音：BGM を小さく、部屋の環境音を少し大きく。ランプの火が静かに燃える音（NightSoundscape）をシーンに置く
    /// 「前の決まった値のまま」の所だけ変える（自分で Inspector で調整した値は、そのまま残す）。何度実行してもよい。
    /// </summary>
    public static class Step6Night2D
    {
        private const string Menu = "Tools/Potion Frontier 2D/";
        private static readonly string[] Scenes = { "Assets/Scenes/Atelier2D.unity", "Assets/Scenes/Brew2D.unity" };
        private const string PrefabDir = "Assets/Art2D/Brewing/Prefabs/";
        public const string SoundscapeName = "NightSoundscape（夜の環境音）";

        // ---- 夜の照明（前の値 → 新しい値）----
        private static readonly Color OldGlobalColor = new Color(1f, 0.84f, 0.66f, 1f);
        private static readonly Color NightGlobalColor = new Color(0.52f, 0.58f, 0.86f, 1f);   // 月夜の青い暗さ
        private const float OldGlobal = 0.32f, NightGlobal = 0.1f;
        private static readonly Color OldMoonColor = new Color(0.55f, 0.68f, 1f, 1f);
        private static readonly Color NightMoonColor = new Color(0.48f, 0.6f, 1f, 1f);
        private const float MoonScale = 0.7f;                                                     // 月明かりは控えめに
        private static readonly Color OldFocusColor = new Color(1f, 0.92f, 0.78f, 1f);
        private static readonly Color WarmFocusColor = new Color(1f, 0.7f, 0.42f, 1f);             // 手元のランタンの色
        private const float OldFocus = 0.65f, WarmFocus = 0.55f, OldFocusRadius = 4.6f, WarmFocusRadius = 5.6f;
        private static readonly Color OldLampColor = new Color(1f, 0.74f, 0.42f, 1f);
        private static readonly Color WarmLampColor = new Color(1f, 0.6f, 0.28f, 1f);
        private const float OldLamp = 1.1f, WarmLamp = 1.35f;
        private const float OldHeaterRadius = 3.4f, WarmHeaterRadius = 4.6f;                      // 下から照らして、湯気まで届く
        private const float OldHeaterMax = 2.2f, WarmHeaterMax = 2.8f;
        private const float OldBgm = 0.55f, NightBgm = 0.18f;                                     // BGM より環境音
        private const float OldAmbient = 0.35f, NightAmbient = 0.55f;

        [MenuItem(Menu + "Step6：夜の空気感（照明・環境音）", priority = 17)]
        private static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Step6", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string before = EditorSceneManager.GetActiveScene().path;
            var log = new StringBuilder();
            var kept = new List<string>();

            int prefabChanges = TunePrefabs(kept);
            log.AppendLine($"・Prefab（ランプ・加熱台・部屋の月明かり）：{prefabChanges} か所を夜の色に");
            foreach (string path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int n = TuneScene(kept, out bool addedSound);
                if (n > 0 || addedSound)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                log.AppendLine($"・{Path.GetFileNameWithoutExtension(path)}：{n} か所を夜の色・音に{(addedSound ? "／夜の環境音を置いた" : "")}");
            }
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(before) && File.Exists(before)) EditorSceneManager.OpenScene(before);

            if (kept.Count > 0)
            {
                log.AppendLine();
                log.AppendLine("自分で調整してあったので、そのままにした所：");
                foreach (string k in kept) log.AppendLine("  " + k);
            }
            log.AppendLine();
            log.AppendLine("明るさ・色は、それぞれの Light 2D の Inspector で好みに合わせて変えられます（このメニューは、前の決まった値の所だけを変えます）。");
            Debug.Log("[Step6] 夜の空気感\n" + log);
            EditorUtility.DisplayDialog("Step6：夜の空気感", log.ToString(), "OK");
        }

        // ------------------------------------------------------------------

        private static int TunePrefabs(List<string> kept)
        {
            int changes = 0;
            changes += EditPrefab(PrefabDir + "Lamp.prefab", root =>
            {
                int c = 0;
                foreach (Light2D l in root.GetComponentsInChildren<Light2D>(true))
                    if (l.name.Contains("LampLight")) c += Tune(l, OldLamp, WarmLamp, OldLampColor, WarmLampColor, -1f, -1f, "Lamp / LampLight", kept);
                return c;
            });
            changes += EditPrefab(PrefabDir + "Heater.prefab", root =>
            {
                int c = 0;
                foreach (Light2D l in root.GetComponentsInChildren<Light2D>(true))
                {
                    if (!l.name.Contains("HeaterLight")) continue;
                    if (Near(l.pointLightOuterRadius, OldHeaterRadius))
                    {
                        l.pointLightOuterRadius = WarmHeaterRadius;
                        c++;
                    }
                    else if (!Near(l.pointLightOuterRadius, WarmHeaterRadius)) kept.Add("Heater / HeaterLight の範囲");
                }
                foreach (HeaterKnob2D knob in root.GetComponentsInChildren<HeaterKnob2D>(true))
                    c += SetFloat(knob, "lightMax", OldHeaterMax, WarmHeaterMax, "Heater / 加熱台の光の強さ", kept);
                return c;
            });
            changes += EditPrefab(PrefabDir + "Room.prefab", root =>
            {
                int c = 0;
                foreach (Light2D l in root.GetComponentsInChildren<Light2D>(true)) c += TuneMoon(l, kept);
                return c;
            });
            return changes;
        }

        private static int TuneScene(List<string> kept, out bool addedSound)
        {
            int c = 0;
            foreach (Light2D l in Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(l) && !l.name.Contains("GlobalLight") && !l.name.Contains("FocusLight") && !l.name.Contains("MoonLight")) continue;
                if (l.lightType == Light2D.LightType.Global || l.name.Contains("GlobalLight"))
                    c += Tune(l, OldGlobal, NightGlobal, OldGlobalColor, NightGlobalColor, -1f, -1f, "GlobalLight（部屋全体の明るさ）", kept);
                else if (l.name.Contains("FocusLight"))
                {
                    c += Tune(l, OldFocus, WarmFocus, OldFocusColor, WarmFocusColor, OldFocusRadius, WarmFocusRadius, "FocusLight（調合台を照らす灯り）", kept);
                    if (l.GetComponent<LightFlicker2D>() == null)
                    {
                        // 手元の灯りも、ランタンのようにゆっくりゆらぐ（揺れは小さく）
                        var f = Undo.AddComponent<LightFlicker2D>(l.gameObject);
                        var so = new SerializedObject(f);
                        so.FindProperty("intensityAmount").floatValue = 0.08f;
                        so.FindProperty("radiusAmount").floatValue = 0.03f;
                        so.FindProperty("speed").floatValue = 1.6f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        c++;
                    }
                }
                else c += TuneMoon(l, kept);
            }
            foreach (AudioManager am in Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                c += SetFloat(am, "bgmBase", OldBgm, NightBgm, "AudioManager の BGM の大きさ", kept);
                c += SetFloat(am, "ambientBase", OldAmbient, NightAmbient, "AudioManager の環境音の大きさ", kept);
            }
            addedSound = false;
            if (Object.FindFirstObjectByType<NightSoundscape2D>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject(SoundscapeName);
                go.AddComponent<NightSoundscape2D>();
                Undo.RegisterCreatedObjectUndo(go, "夜の環境音");
                addedSound = true;
            }
            return c;
        }

        private static int TuneMoon(Light2D l, List<string> kept)
        {
            if (!l.name.Contains("MoonLight")) return 0;
            if (!SameColor(l.color, OldMoonColor)) return 0; // もう夜の色、または自分で変えてある
            l.color = NightMoonColor;
            l.intensity *= MoonScale;
            EditorUtility.SetDirty(l);
            return 1;
        }

        /// <summary>前の値のままなら新しい値に（明るさ・色・範囲。範囲が -1 なら範囲は変えない）</summary>
        private static int Tune(Light2D l, float oldI, float newI, Color oldC, Color newC, float oldR, float newR, string label, List<string> kept)
        {
            int c = 0;
            Undo.RecordObject(l, "夜の空気感");
            if (Near(l.intensity, oldI)) { l.intensity = newI; c++; }
            else if (!Near(l.intensity, newI)) kept.Add(label + " の明るさ");
            if (SameColor(l.color, oldC)) { l.color = newC; c++; }
            else if (!SameColor(l.color, newC)) kept.Add(label + " の色");
            if (oldR > 0f)
            {
                if (Near(l.pointLightOuterRadius, oldR)) { l.pointLightOuterRadius = newR; c++; }
                else if (!Near(l.pointLightOuterRadius, newR)) kept.Add(label + " の範囲");
            }
            if (c > 0) EditorUtility.SetDirty(l);
            return c;
        }

        private static int SetFloat(Object target, string field, float oldValue, float newValue, string label, List<string> kept)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return 0;
            if (Near(p.floatValue, oldValue))
            {
                p.floatValue = newValue;
                so.ApplyModifiedPropertiesWithoutUndo();
                return 1;
            }
            if (!Near(p.floatValue, newValue)) kept.Add(label);
            return 0;
        }

        private static int EditPrefab(string path, System.Func<GameObject, int> edit)
        {
            if (!File.Exists(path)) return 0;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = edit(root);
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                return n;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.005f;

        private static bool SameColor(Color a, Color b) => Near(a.r, b.r) && Near(a.g, b.g) && Near(a.b, b.b);
    }
}
