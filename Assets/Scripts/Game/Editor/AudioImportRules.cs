using UnityEditor;
using UnityEngine;

namespace PotionFrontier.Game.EditorTools
{
    /// <summary>
    /// 差し替え用の音（Assets/CustomAssets/Sounds）の取り込み設定を自動で決める。同じ名前の WAV で上書きしたときも同じ設定になる。
    ///   BGM・環境音 … ストリーミング再生（メモリ節約）
    ///   効果音      … 読み込み時に展開（すぐ鳴る）・モノラル化
    /// </summary>
    public class AudioImportRules : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/CustomAssets/Sounds/") && !assetPath.StartsWith("Assets/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            if (assetPath.Contains("/BGM/"))
            {
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.forceToMono = false;
            }
            else if (assetPath.Contains("/Ambient/"))
            {
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
                importer.forceToMono = true;
            }
            else
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.8f;
                importer.forceToMono = true;
            }
            importer.loadInBackground = assetPath.Contains("/BGM/");
            importer.defaultSampleSettings = settings;
        }
    }
}
