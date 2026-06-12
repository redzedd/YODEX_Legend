#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GAS.Editor
{
    /// <summary>
    /// 一鍵建立 AoE 範本資源
    ///   - 圓環貼圖(地面 Decal)
    ///   - 牆面 pattern 貼圖(光壁紋路)
    ///   - URP Decal Material
    ///   - 光壁 Material(GAS/AoEIndicatorWall shader)
    ///   - 管狀網格(Tube Mesh)
    ///   - Prefab 模板(含 IndicatorContainer / DecalIndicator / LightWall / EffectGroup)
    /// 選單:Tools / GAS / 建立 AoE Prefab 模板
    /// 產出位置:Assets/Script/GAS/Data/AoE/Generated/
    /// </summary>
    public static class AoEAssetCreator
    {
        private const string OUTPUT_FOLDER = "Assets/Script/GAS/Data/AoE/Generated";
        private const string RING_TEX_NAME = "AoE_Ring";
        private const string DECAL_MAT_NAME = "AoE_DefaultDecal";
        private const string WALL_TEX_NAME = "AoE_WallPattern";
        private const string WALL_MAT_NAME = "AoE_WallMaterial";
        private const string TUBE_MESH_NAME = "AoE_TubeMesh";
        private const string PREFAB_NAME = "AoE_Template";
        private const string WALL_SHADER_NAME = "GAS/AoEIndicatorWall";

        [MenuItem("Tools/GAS/建立 AoE Prefab 模板")]
        public static void CreateAoETemplate()
        {
            EnsureFolder(OUTPUT_FOLDER);

            // === 1. 圓環貼圖(地面 Decal 用) ===
            string ringTexPath = $"{OUTPUT_FOLDER}/{RING_TEX_NAME}.png";
            CreateRingTexture(ringTexPath);
            AssetDatabase.ImportAsset(ringTexPath, ImportAssetOptions.ForceUpdate);
            ConfigureTextureImporter(ringTexPath, TextureWrapMode.Clamp);
            Texture2D ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ringTexPath);

            // === 2. 光壁 pattern 貼圖(垂直紋路) ===
            string wallTexPath = $"{OUTPUT_FOLDER}/{WALL_TEX_NAME}.png";
            CreateWallPatternTexture(wallTexPath);
            AssetDatabase.ImportAsset(wallTexPath, ImportAssetOptions.ForceUpdate);
            ConfigureTextureImporter(wallTexPath, TextureWrapMode.Repeat);
            Texture2D wallTex = AssetDatabase.LoadAssetAtPath<Texture2D>(wallTexPath);

            // === 3. URP Decal Material ===
            Shader decalShader = ResolveDecalShader();
            if (decalShader == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到 URP Decal Shader",
                    "請確認:\n" +
                    "1. Project 已使用 URP\n" +
                    "2. URP Renderer Asset 已加入 Decal Renderer Feature\n" +
                    "預設 shader 路徑應為 'Shader Graphs/Decal'", "OK");
                return;
            }
            Material decalMat = LoadOrCreateMaterial($"{OUTPUT_FOLDER}/{DECAL_MAT_NAME}.mat", decalShader);
            ApplyDecalMaterialDefaults(decalMat, ringTex);
            EditorUtility.SetDirty(decalMat);

            // === 4. 光壁 Material(自訂 shader) ===
            Shader wallShader = Shader.Find(WALL_SHADER_NAME);
            if (wallShader == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到 AoE Wall Shader",
                    $"請確認 '{WALL_SHADER_NAME}' shader 存在於 Project 中。\n" +
                    "預設路徑:Assets/Script/GAS/Shaders/AoEIndicatorWall.shader", "OK");
                return;
            }
            Material wallMat = LoadOrCreateMaterial($"{OUTPUT_FOLDER}/{WALL_MAT_NAME}.mat", wallShader);
            ApplyWallMaterialDefaults(wallMat, wallTex);
            EditorUtility.SetDirty(wallMat);

            // === 5. 管狀網格(底部 pivot,單位高度,半徑 1) ===
            string tubeMeshPath = $"{OUTPUT_FOLDER}/{TUBE_MESH_NAME}.asset";
            Mesh tubeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(tubeMeshPath);
            if (tubeMesh == null)
            {
                tubeMesh = CreateTubeMesh(48, 1f, 1f);
                AssetDatabase.CreateAsset(tubeMesh, tubeMeshPath);
            }
            else
            {
                // 重新建構幾何,確保是最新的形狀
                Mesh refreshed = CreateTubeMesh(48, 1f, 1f);
                tubeMesh.Clear();
                tubeMesh.vertices = refreshed.vertices;
                tubeMesh.uv = refreshed.uv;
                tubeMesh.triangles = refreshed.triangles;
                tubeMesh.RecalculateNormals();
                tubeMesh.RecalculateBounds();
                Object.DestroyImmediate(refreshed);
                EditorUtility.SetDirty(tubeMesh);
            }

            // === 6. Prefab 模板 ===
            string prefabPath = $"{OUTPUT_FOLDER}/{PREFAB_NAME}.prefab";
            GameObject root = new(PREFAB_NAME);
            try
            {
                AoEBehaviour aoe = root.AddComponent<AoEBehaviour>();

                // IndicatorContainer:包住 Decal + LightWall,作為 _indicatorRoot
                GameObject indicatorContainer = new("IndicatorContainer");
                indicatorContainer.transform.SetParent(root.transform, false);

                // DecalIndicator(rotated 90 X 朝下投影)
                GameObject decalChild = new("DecalIndicator");
                decalChild.transform.SetParent(indicatorContainer.transform, false);
                decalChild.transform.localPosition = new Vector3(0f, 1f, 0f);
                decalChild.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                DecalProjector projector = decalChild.AddComponent<DecalProjector>();
                projector.material = decalMat;
                projector.size = new Vector3(10f, 10f, 3f);
                projector.pivot = Vector3.zero;

                // LightWall(管狀網格,底部 pivot 向上延伸)
                GameObject wallChild = new("LightWall");
                wallChild.transform.SetParent(indicatorContainer.transform, false);
                wallChild.transform.localPosition = Vector3.zero;
                wallChild.transform.localRotation = Quaternion.identity;
                // 半徑 5 / 高度 4(動畫時 scale.y 從 0 → 4 升起;Radius 改變時 SyncDecalSize 不會動 LightWall,需另行處理)
                wallChild.transform.localScale = new Vector3(5f, 4f, 5f);
                MeshFilter wallMf = wallChild.AddComponent<MeshFilter>();
                wallMf.sharedMesh = tubeMesh;
                MeshRenderer wallMr = wallChild.AddComponent<MeshRenderer>();
                wallMr.sharedMaterial = wallMat;
                wallMr.shadowCastingMode = ShadowCastingMode.Off;
                wallMr.receiveShadows = false;
                wallMr.lightProbeUsage = LightProbeUsage.Off;
                wallMr.reflectionProbeUsage = ReflectionProbeUsage.Off;

                // 指示器動畫器 — 掛在 IndicatorContainer
                AoEIndicatorAnimator animator = indicatorContainer.AddComponent<AoEIndicatorAnimator>();
                var animatorSo = new SerializedObject(animator);
                animatorSo.FindProperty("_decal").objectReferenceValue = projector;
                animatorSo.FindProperty("_wallTransform").objectReferenceValue = wallChild.transform;
                animatorSo.FindProperty("_wallRenderer").objectReferenceValue = wallMr;
                animatorSo.ApplyModifiedPropertiesWithoutUndo();

                // EffectGroup
                GameObject effectChild = new("EffectGroup");
                effectChild.transform.SetParent(root.transform, false);
                effectChild.SetActive(false);

                // 套用 AoEBehaviour 預設欄位 + _indicatorRoot/_effectRoot/_indicatorWallTransform 引用
                var so = new SerializedObject(aoe);
                so.FindProperty("_radius").floatValue = 5f;
                so.FindProperty("_tickMode").enumValueIndex = (int)AoETickMode.OneShot;
                so.FindProperty("_effectLifetime").floatValue = 2f;
                so.FindProperty("_indicatorRoot").objectReferenceValue = indicatorContainer;
                so.FindProperty("_effectRoot").objectReferenceValue = effectChild;
                so.FindProperty("_indicatorWallTransform").objectReferenceValue = wallChild.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = savedPrefab;
            EditorGUIUtility.PingObject(savedPrefab);
            Debug.Log($"[AoEAssetCreator] 已建立 AoE 模板 → {prefabPath}\n" +
                     "結構:Root / IndicatorContainer (DecalIndicator + LightWall + AoEIndicatorAnimator) / EffectGroup\n" +
                     "可直接拖到 RangedAttackData.AoEPrefab 使用,或複製/修改作為新攻擊樣式");
        }

        private static Material LoadOrCreateMaterial(string matPath, Shader shader)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static Shader ResolveDecalShader()
        {
            string[] candidates =
            {
                "Shader Graphs/Decal",
                "Universal Render Pipeline/Decal",
                "Decal"
            };
            foreach (var name in candidates)
            {
                Shader s = Shader.Find(name);
                if (s != null) return s;
            }
            return null;
        }

        private static void ApplyDecalMaterialDefaults(Material mat, Texture2D ringTex)
        {
            // Base tint(0~1 範圍)+ HDR emission(>1 觸發 URP Bloom 後處理產生光暈)
            Color tintColor = new(1f, 0.55f, 0.12f, 1f);
            Color emissionColor = new(2.0f, 1.0f, 0.25f, 1f); // HDR — 強度 2,需 URP Bloom 啟用才看到發光

            // Shader Graph 命名(URP 14+ Decal Shader Graph 預設)
            if (mat.HasProperty("Base_Map")) mat.SetTexture("Base_Map", ringTex);
            if (mat.HasProperty("Tint")) mat.SetColor("Tint", tintColor);
            if (mat.HasProperty("Emission_Map")) mat.SetTexture("Emission_Map", ringTex);
            if (mat.HasProperty("Emission_Color")) mat.SetColor("Emission_Color", emissionColor);

            // 傳統命名(舊版 URP Decal / Legacy)
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", ringTex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tintColor);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", ringTex);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);

            // 啟用 emission keyword(若 shader 支援)
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        private static void ApplyWallMaterialDefaults(Material mat, Texture2D wallTex)
        {
            mat.SetTexture("_MainTex", wallTex);
            mat.SetColor("_BaseColor", new Color(1f, 0.8f, 0.2f, 1f));
            mat.SetVector("_MainTex_Tiling", new Vector4(2f, 1.5f, 0f, 0f));
            mat.SetFloat("_ScrollSpeed", 0.4f);
            mat.SetFloat("_FadeTopPower", 1.5f);
            mat.SetFloat("_BottomGlowSize", 0.08f);
            mat.SetFloat("_BottomGlowIntensity", 8f);
            mat.SetFloat("_Intensity", 2.5f);
            mat.SetFloat("_AlphaIntensity", 1.2f);
            mat.SetFloat("_FadeMultiplier", 1f);
            mat.SetFloat("_DistortionAmount", 0.04f);
            mat.SetFloat("_DistortionFrequency", 6f);
            mat.SetFloat("_DistortionSpeed", 1.8f);
            mat.renderQueue = 3050;
        }

        /// <summary>
        /// 程式生成圓環貼圖 — 雙環設計:外圈實線 + 中心淡淡填色
        /// </summary>
        private static void CreateRingTexture(string path)
        {
            const int SIZE = 512;
            Texture2D tex = new(SIZE, SIZE, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[SIZE * SIZE];

            float center = (SIZE - 1) * 0.5f;
            float outerRadius = SIZE * 0.48f;
            float innerRadius = outerRadius * 0.82f;
            float edgeSoft = SIZE * 0.012f;

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float outerAlpha = Mathf.Clamp01((outerRadius - dist) / edgeSoft);
                    float innerAlpha = Mathf.Clamp01((dist - innerRadius) / edgeSoft);
                    float ring = outerAlpha * innerAlpha;

                    float fillNorm = Mathf.Clamp01(dist / innerRadius);
                    float fill = (1f - fillNorm) * 0.15f;

                    float alpha = Mathf.Clamp01(Mathf.Max(ring, fill));
                    pixels[y * SIZE + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// 程式生成光壁 pattern 紋理 — 多 octave Perlin 噪聲,X 與 Y 都 tile-able
        /// 使用 4-corner blend 方法:在 (u, v)、(u-1, v)、(u, v-1)、(u-1, v-1) 四點採樣後依 uv 線性混合,
        /// 數學上保證 u=0/1、v=0/1 時值相同 → 無接縫。
        /// 高度比寬度大(2:1),頻率設成水平多/垂直少 → 視覺呈現「拉長的垂直能量條紋」
        /// </summary>
        private static void CreateWallPatternTexture(string path)
        {
            const int W = 256;
            const int H = 512;
            Texture2D tex = new(W, H, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[W * H];

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W;
                    float v = (float)y / H;

                    // 多層 tile-able Perlin — 水平頻率高、垂直頻率低,給垂直條紋感
                    float n1 = TileablePerlin(u, v, 4f, 2f, 0);
                    float n2 = TileablePerlin(u, v, 8f, 4f, 100);
                    float n3 = TileablePerlin(u, v, 16f, 8f, 200);

                    float noise = n1 * 0.55f + n2 * 0.30f + n3 * 0.15f;

                    // 對比強化(SmoothStep 把中間色拉開為亮/暗)— 條紋更明顯
                    noise = Mathf.SmoothStep(0.30f, 0.82f, noise);

                    pixels[y * W + x] = new Color(noise, noise, noise, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// Tile-able Perlin noise — 4 corner 採樣 + uv-based 線性混合
        /// 確保 u=0 與 u=1、v=0 與 v=1 結果相同(無接縫 wrap)
        /// </summary>
        private static float TileablePerlin(float u, float v, float scaleX, float scaleY, int seed)
        {
            float so = seed * 0.073f;
            float n00 = Mathf.PerlinNoise(u * scaleX + so, v * scaleY + so);
            float n10 = Mathf.PerlinNoise((u - 1f) * scaleX + so, v * scaleY + so);
            float n01 = Mathf.PerlinNoise(u * scaleX + so, (v - 1f) * scaleY + so);
            float n11 = Mathf.PerlinNoise((u - 1f) * scaleX + so, (v - 1f) * scaleY + so);
            return Mathf.Lerp(
                Mathf.Lerp(n00, n10, u),
                Mathf.Lerp(n01, n11, u),
                v);
        }

        /// <summary>
        /// 程式生成開口管狀網格(底部 pivot,高度向 +Y 延伸)
        /// 無上下蓋,法線朝外,UV: u 環繞 [0,1] / v 上下 [0,1]
        /// </summary>
        private static Mesh CreateTubeMesh(int segments, float radius, float height)
        {
            // 多一組頂點接合 UV.x = 0 與 = 1
            int ringVerts = segments + 1;
            Vector3[] verts = new Vector3[ringVerts * 2];
            Vector2[] uvs = new Vector2[ringVerts * 2];
            int[] tris = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = t * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                int vBottom = i * 2;
                int vTop = i * 2 + 1;
                verts[vBottom] = new Vector3(x, 0f, z);
                verts[vTop] = new Vector3(x, height, z);
                uvs[vBottom] = new Vector2(t, 0f);
                uvs[vTop] = new Vector2(t, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int idx = i * 6;
                int bl = i * 2;       // bottom-left
                int tl = i * 2 + 1;   // top-left
                int br = i * 2 + 2;   // bottom-right
                int tr = i * 2 + 3;   // top-right
                // Triangle 1
                tris[idx + 0] = bl;
                tris[idx + 1] = tl;
                tris[idx + 2] = br;
                // Triangle 2
                tris[idx + 3] = tl;
                tris[idx + 4] = tr;
                tris[idx + 5] = br;
            }

            Mesh mesh = new()
            {
                name = TUBE_MESH_NAME,
                vertices = verts,
                uv = uvs,
                triangles = tris
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ConfigureTextureImporter(string path, TextureWrapMode wrap)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
