using System.Collections.Generic;
using UnityEngine;

namespace PotionFrontier.Game
{
    /// <summary>
    /// 回転体メッシュ（ろくろで作るような形）を作る。
    /// 断面の輪郭（x = 半径, y = 高さ）を Y 軸まわりに回して、瓶・フラスコ・壺・樽などを作る。
    /// 同じ点を2回続けて書くと、その位置で角が立つ（法線が分かれる）。
    /// </summary>
    public static class LatheMesh
    {
        public static Mesh Create(IList<Vector2> profile, int segments, string name)
        {
            var mesh = new Mesh { name = name };
            Build(mesh, profile, segments);
            return mesh;
        }

        public static void Build(Mesh mesh, IList<Vector2> profile, int segments)
        {
            mesh.Clear();
            int rings = profile.Count;
            if (rings < 2 || segments < 3) return;

            int columns = segments + 1;
            var vertices = new Vector3[rings * columns];
            var normals = new Vector3[rings * columns];
            var uvs = new Vector2[rings * columns];
            var triangles = new int[(rings - 1) * segments * 6];

            // 断面の各点の法線（前後の辺の外向き法線の平均。長さ0の辺は無視）
            var profileNormals = new Vector2[rings];
            var lengths = new float[rings];
            float totalLength = 0f;
            for (int i = 0; i < rings; i++)
            {
                Vector2 sum = Vector2.zero;
                if (i > 0)
                {
                    Vector2 d = profile[i] - profile[i - 1];
                    if (d.sqrMagnitude > 1e-10f) sum += new Vector2(d.y, -d.x).normalized;
                    totalLength += d.magnitude;
                }
                if (i < rings - 1)
                {
                    Vector2 d = profile[i + 1] - profile[i];
                    if (d.sqrMagnitude > 1e-10f) sum += new Vector2(d.y, -d.x).normalized;
                }
                profileNormals[i] = sum.sqrMagnitude > 1e-10f ? sum.normalized : Vector2.right;
                lengths[i] = totalLength;
            }

            for (int i = 0; i < rings; i++)
            {
                float v = totalLength > 0f ? lengths[i] / totalLength : 0f;
                for (int j = 0; j < columns; j++)
                {
                    float angle = (float)j / segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    int index = i * columns + j;
                    Vector2 p = profile[i];
                    Vector2 n = profileNormals[i];
                    vertices[index] = new Vector3(p.x * cos, p.y, p.x * sin);
                    normals[index] = new Vector3(n.x * cos, n.y, n.x * sin);
                    uvs[index] = new Vector2((float)j / segments, v);
                }
            }

            int t = 0;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int a = i * columns + j;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = c; triangles[t++] = d; triangles[t++] = b;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 容器の断面から「中の液体」の断面を作る。height までで切り取り、上面を閉じる。
        /// 断面は y が下から上へ単調に増えている前提。
        /// </summary>
        public static List<Vector2> LiquidProfile(IList<Vector2> container, float height, float radiusScale, float bottomOffset)
        {
            var result = new List<Vector2>();
            float top = height + bottomOffset;

            for (int i = 0; i < container.Count; i++)
            {
                Vector2 p = container[i];
                float y = p.y + bottomOffset;
                if (y <= top)
                {
                    result.Add(new Vector2(p.x * radiusScale, y));
                    continue;
                }

                if (i > 0)
                {
                    Vector2 prev = container[i - 1];
                    float prevY = prev.y + bottomOffset;
                    if (prevY < top)
                    {
                        float k = (top - prevY) / (y - prevY);
                        result.Add(new Vector2(Mathf.Lerp(prev.x, p.x, k) * radiusScale, top));
                    }
                }
                break;
            }

            if (result.Count == 0) return result;

            Vector2 last = result[result.Count - 1];
            if (last.x > 0f)
            {
                result.Add(last);                         // 角を立てる
                result.Add(new Vector2(0f, last.y));      // 上面を閉じる
            }
            return result;
        }
    }
}
