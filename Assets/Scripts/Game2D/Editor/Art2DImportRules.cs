using UnityEditor;

namespace PotionFrontier.Game2D.EditorTools
{
    /// <summary>
    /// Assets/Art2D の下に置いた画像を、はじめて取り込むときだけ 2D 用の設定にそろえる
    /// （スプライト・100 ピクセル = 1 ワールド単位・ミップマップなし・最大 4096）。
    /// 一度取り込んだ画像の設定は変えない（自分で変えた設定はそのまま残る）。
    /// 同じ名前の PNG で上書きすれば、設定も Prefab とのつながりもそのままで絵だけ替わる。
    /// </summary>
    public class Art2DImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/Art2D/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) || !assetImporter.importSettingsMissing) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
        }
    }
}
