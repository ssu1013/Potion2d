// ① URP（Universal Render Pipeline）をパッケージマネージャーで入れる。
// URP が入ると、このメニューは消えて「② 2D URP 環境をセットアップ」が出る。
#if !POTION_HAS_URP
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Potion2D.Setup
{
    static class URPInstaller
    {
        const string PackageName = "com.unity.render-pipelines.universal";
        static AddRequest request;

        [MenuItem("Tools/Potion Frontier 2D/① URP をインストール", priority = 1)]
        static void Install()
        {
            if (request != null) return;
            // バージョンを指定しないと、このエディタ（2022.3）に合うおすすめの版が入る
            request = Client.Add(PackageName);
            EditorApplication.update += Watch;
            Debug.Log("[2D URP] URP をインストールしています…（終わるとスクリプトの再読み込みが走ります）");
        }

        static void Watch()
        {
            if (request == null || !request.IsCompleted) return;
            if (request.Status == StatusCode.Success)
                Debug.Log($"[2D URP] URP {request.Result.version} を入れました。メニューに「② 2D URP 環境をセットアップ」が出たら実行してください。");
            else
                Debug.LogError($"[2D URP] URP を入れられませんでした：{request.Error?.message}");
            EditorApplication.update -= Watch;
            request = null;
        }
    }
}
#endif
