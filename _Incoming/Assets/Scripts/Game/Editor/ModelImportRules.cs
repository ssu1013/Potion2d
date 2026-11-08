using UnityEditor;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// Assets/Art/Models に入れた 3D モデル（.fbx など）の読み込み設定を、ゲーム向けにそろえる。
    /// 初めて読み込むときだけ設定する（あとからインスペクターで変えた設定は上書きしない）。
    ///   ・カメラ・ライトは読み込まない（Blender の書き出しに入っていても無視）
    ///   ・Blender の座標の向き（Z が上）を Unity の向き（Y が上）に直して焼き込む（回転 -90° の親ができない）
    ///   ・Characters フォルダの中はキャラクター（アニメーション付き・Generic）、それ以外は動かない小物として読み込む
    /// </summary>
    public class ModelImportRules : AssetPostprocessor
    {
        private const string Root = "Assets/Art/Models/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            if (!assetImporter.importSettingsMissing) return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            bool character = assetPath.Contains("/Characters/");
            importer.animationType = character ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.importAnimation = character;
        }
    }
}
