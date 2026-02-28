using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    internal static class BlockAssetFactory
    {
        internal sealed class FaceRenderData
        {
            public Material[] materials;
            public Vector4[] faceMainTexSt;
        }

        private const string BlockTextureFolder = "Packages/com.box3lab.box3/Assets/block";
        private const string BumpTextureFolder = "Packages/com.box3lab.box3/Assets/bump";
        private const string MaterialTextureFolder = "Packages/com.box3lab.box3/Assets/material";
        private static readonly string GeneratedRoot = "Assets/BlockWorldGenerated";
        private static readonly string MeshFolder = "Assets/BlockWorldGenerated/Meshes";
        private static readonly string MaterialFolder = "Assets/BlockWorldGenerated/Materials";
        private static readonly string AtlasFolder = "Assets/BlockWorldGenerated/Atlases";
        private static readonly string MeshAssetPath = "Assets/BlockWorldGenerated/Meshes/BlockCube.asset";
        private static readonly string AtlasTexturePath = "Assets/BlockWorldGenerated/Atlases/WorldBuilderAtlas.asset";
        private static readonly string AtlasBumpTexturePath = "Assets/BlockWorldGenerated/Atlases/WorldBuilderAtlas_Bump.asset";
        private static readonly string AtlasMaterialTexturePath = "Assets/BlockWorldGenerated/Atlases/WorldBuilderAtlas_Material.asset";
        private static readonly string AtlasTransparentMaterialPath = "Assets/BlockWorldGenerated/Materials/WorldBuilderAtlas_Transparent.mat";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex("^(.*)_(back|bottom|front|left|right|top)\\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, Rect> AtlasUvByTexturePath = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D _atlasTexture;
        private static Texture2D _atlasBumpTexture;
        private static Texture2D _atlasMaterialTexture;
        private static Material _atlasTransparentMaterial;
        private static bool _atlasReady;

        public static void InvalidateCaches()
        {
            _atlasReady = false;
            _atlasTexture = null;
            _atlasBumpTexture = null;
            _atlasMaterialTexture = null;
            _atlasTransparentMaterial = null;
            AtlasUvByTexturePath.Clear();
        }

        public static Mesh GetOrCreateCubeMesh()
        {
            EnsureFolders();
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (mesh != null)
            {
                return mesh;
            }

            mesh = BuildCubeMesh();
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        public static bool TryGetFaceRenderData(Dictionary<string, string> sideTexturePaths, out FaceRenderData data)
        {
            data = null;
            if (sideTexturePaths == null)
            {
                return false;
            }

            if (!EnsureAtlasResources())
            {
                return false;
            }

            Material shared = _atlasTransparentMaterial;
            if (shared == null)
            {
                return false;
            }

            string fallbackPath = GetFallbackTexturePath(sideTexturePaths);
            Material[] mats = new Material[SideOrder.Length];
            Vector4[] mainTexSt = new Vector4[SideOrder.Length];
            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                string texturePath = null;
                if (!sideTexturePaths.TryGetValue(side, out texturePath) || string.IsNullOrWhiteSpace(texturePath))
                {
                    texturePath = fallbackPath;
                }

                mats[i] = shared;
                if (!string.IsNullOrWhiteSpace(texturePath) && AtlasUvByTexturePath.TryGetValue(texturePath, out Rect uvRect))
                {
                    mainTexSt[i] = new Vector4(uvRect.width, uvRect.height, uvRect.x, uvRect.y);
                }
                else
                {
                    mainTexSt[i] = new Vector4(1f, 1f, 0f, 0f);
                }
            }

            data = new FaceRenderData
            {
                materials = mats,
                faceMainTexSt = mainTexSt
            };
            return true;
        }

        public static Material GetAtlasMaterial()
        {
            if (!EnsureAtlasResources())
            {
                return null;
            }

            return _atlasTransparentMaterial;
        }

        public static Texture2D GetAtlasBumpTexture()
        {
            if (!EnsureAtlasResources())
            {
                return null;
            }

            return _atlasBumpTexture;
        }

        public static Texture2D GetAtlasMaterialTexture()
        {
            if (!EnsureAtlasResources())
            {
                return null;
            }

            return _atlasMaterialTexture;
        }

        private static string GetFallbackTexturePath(Dictionary<string, string> sideTexturePaths)
        {
            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                if (sideTexturePaths.TryGetValue(side, out string path) && !string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static bool EnsureAtlasResources()
        {
            if (_atlasReady && _atlasTexture != null && _atlasTransparentMaterial != null)
            {
                return true;
            }

            RebuildAtlasResources();
            return _atlasTexture != null && _atlasTransparentMaterial != null;
        }

        private static void RebuildAtlasResources()
        {
            EnsureFolders();
            AtlasUvByTexturePath.Clear();
            _atlasReady = false;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlockTextureFolder });
            List<string> texturePaths = new List<string>(guids.Length);
            List<Texture2D> readableTextures = new List<Texture2D>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileName(assetPath);
                if (string.IsNullOrWhiteSpace(fileName) || !SideRegex.IsMatch(fileName))
                {
                    continue;
                }

                Texture2D readable = LoadReadableTextureFromAsset(assetPath);
                if (readable == null)
                {
                    continue;
                }

                texturePaths.Add(assetPath);
                readableTextures.Add(readable);
            }

            if (readableTextures.Count == 0)
            {
                CleanupReadableTextures(readableTextures);
                return;
            }

            Texture2D generated = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = "WorldBuilderAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            int maxSize = Mathf.Clamp(SystemInfo.maxTextureSize, 2048, 8192);
            Rect[] rects = generated.PackTextures(readableTextures.ToArray(), 0, maxSize, false);
            for (int i = 0; i < texturePaths.Count && i < rects.Length; i++)
            {
                AtlasUvByTexturePath[texturePaths[i]] = rects[i];
            }

            PersistAtlasTexture(generated, out _atlasTexture);
            _atlasBumpTexture = BuildBumpAtlasTexture(_atlasTexture, texturePaths);
            PersistAtlasBumpTexture(_atlasBumpTexture);
            _atlasMaterialTexture = BuildMaterialAtlasTexture(_atlasTexture, texturePaths);
            PersistAtlasMaterialTexture(_atlasMaterialTexture);
            _atlasTransparentMaterial = GetOrCreateTransparentAtlasMaterial(AtlasTransparentMaterialPath, _atlasTexture);

            const string legacyOpaqueMaterialPath = "Assets/BlockWorldGenerated/Atlases/WorldBuilderAtlas_Opaque.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(legacyOpaqueMaterialPath) != null)
            {
                AssetDatabase.DeleteAsset(legacyOpaqueMaterialPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AtlasTexturePath, ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasBumpTexturePath) != null)
            {
                AssetDatabase.ImportAsset(AtlasBumpTexturePath, ImportAssetOptions.ForceUpdate);
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasMaterialTexturePath) != null)
            {
                AssetDatabase.ImportAsset(AtlasMaterialTexturePath, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.ImportAsset(AtlasTransparentMaterialPath, ImportAssetOptions.ForceUpdate);

            CleanupReadableTextures(readableTextures);
            _atlasReady = _atlasTexture != null;
        }

        private static void PersistAtlasTexture(Texture2D generatedTexture, out Texture2D atlasTexture)
        {
            atlasTexture = null;
            if (generatedTexture == null)
            {
                return;
            }

            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generatedTexture, AtlasTexturePath);
                atlasTexture = generatedTexture;
            }
            else
            {
                EditorUtility.CopySerialized(generatedTexture, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(generatedTexture);
                atlasTexture = existing;
            }
        }

        private static void PersistAtlasBumpTexture(Texture2D bumpTexture)
        {
            if (bumpTexture == null)
            {
                return;
            }

            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasBumpTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(bumpTexture, AtlasBumpTexturePath);
                ConfigureBumpAtlasImporter(AtlasBumpTexturePath);
                return;
            }

            EditorUtility.CopySerialized(bumpTexture, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(bumpTexture);
            ConfigureBumpAtlasImporter(AtlasBumpTexturePath);
        }

        private static void PersistAtlasMaterialTexture(Texture2D materialTexture)
        {
            if (materialTexture == null)
            {
                return;
            }

            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasMaterialTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(materialTexture, AtlasMaterialTexturePath);
                ConfigureMaterialAtlasImporter(AtlasMaterialTexturePath);
                return;
            }

            EditorUtility.CopySerialized(materialTexture, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(materialTexture);
            ConfigureMaterialAtlasImporter(AtlasMaterialTexturePath);
        }

        private static Material GetOrCreateTransparentAtlasMaterial(string materialPath, Texture2D atlasTexture)
        {
            if (atlasTexture == null)
            {
                return null;
            }

            EnsureFolders();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Unlit/Transparent");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader)
                {
                    name = "WorldBuilderAtlas_Transparent"
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.mainTexture = atlasTexture;
            SetupTransparentMaterial(material);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D BuildBumpAtlasTexture(Texture2D colorAtlas, List<string> colorTexturePaths)
        {
            if (colorAtlas == null || colorTexturePaths == null)
            {
                return null;
            }

            int width = colorAtlas.width;
            int height = colorAtlas.height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            Texture2D bumpAtlas = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "WorldBuilderAtlas_Bump",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            Color[] clear = new Color[width * height];
            for (int i = 0; i < clear.Length; i++)
            {
                clear[i] = Color.black;
            }
            bumpAtlas.SetPixels(clear);

            for (int i = 0; i < colorTexturePaths.Count; i++)
            {
                string colorPath = colorTexturePaths[i];
                if (!AtlasUvByTexturePath.TryGetValue(colorPath, out Rect uvRect))
                {
                    continue;
                }

                string bumpPath = GetBumpTexturePath(colorPath);
                if (string.IsNullOrWhiteSpace(bumpPath) || !File.Exists(GetProjectAbsolutePath(bumpPath)))
                {
                    continue;
                }

                Texture2D bumpTex = LoadReadableTextureFromAsset(bumpPath);
                if (bumpTex == null)
                {
                    continue;
                }

                int x = Mathf.RoundToInt(uvRect.x * width);
                int y = Mathf.RoundToInt(uvRect.y * height);
                int w = Mathf.RoundToInt(uvRect.width * width);
                int h = Mathf.RoundToInt(uvRect.height * height);
                if (w <= 0 || h <= 0)
                {
                    UnityEngine.Object.DestroyImmediate(bumpTex);
                    continue;
                }

                if (bumpTex.width == w && bumpTex.height == h)
                {
                    bumpAtlas.SetPixels(x, y, w, h, bumpTex.GetPixels());
                }
                else
                {
                    Color[] src = bumpTex.GetPixels();
                    Color[] scaled = new Color[w * h];
                    for (int yy = 0; yy < h; yy++)
                    {
                        int srcY = Mathf.Clamp(Mathf.RoundToInt((yy / (float)h) * (bumpTex.height - 1)), 0, bumpTex.height - 1);
                        for (int xx = 0; xx < w; xx++)
                        {
                            int srcX = Mathf.Clamp(Mathf.RoundToInt((xx / (float)w) * (bumpTex.width - 1)), 0, bumpTex.width - 1);
                            scaled[(yy * w) + xx] = src[(srcY * bumpTex.width) + srcX];
                        }
                    }
                    bumpAtlas.SetPixels(x, y, w, h, scaled);
                }

                UnityEngine.Object.DestroyImmediate(bumpTex);
            }

            bumpAtlas.Apply();
            return bumpAtlas;
        }

        private static Texture2D BuildMaterialAtlasTexture(Texture2D colorAtlas, List<string> colorTexturePaths)
        {
            if (colorAtlas == null || colorTexturePaths == null)
            {
                return null;
            }

            int width = colorAtlas.width;
            int height = colorAtlas.height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            Texture2D materialAtlas = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "WorldBuilderAtlas_Material",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            Color[] clear = new Color[width * height];
            for (int i = 0; i < clear.Length; i++)
            {
                clear[i] = Color.black;
            }
            materialAtlas.SetPixels(clear);

            for (int i = 0; i < colorTexturePaths.Count; i++)
            {
                string colorPath = colorTexturePaths[i];
                if (!AtlasUvByTexturePath.TryGetValue(colorPath, out Rect uvRect))
                {
                    continue;
                }

                string materialPath = GetMaterialTexturePath(colorPath);
                if (string.IsNullOrWhiteSpace(materialPath) || !File.Exists(GetProjectAbsolutePath(materialPath)))
                {
                    continue;
                }

                Texture2D matTex = LoadReadableTextureFromAsset(materialPath);
                if (matTex == null)
                {
                    continue;
                }

                int x = Mathf.RoundToInt(uvRect.x * width);
                int y = Mathf.RoundToInt(uvRect.y * height);
                int w = Mathf.RoundToInt(uvRect.width * width);
                int h = Mathf.RoundToInt(uvRect.height * height);
                if (w <= 0 || h <= 0)
                {
                    UnityEngine.Object.DestroyImmediate(matTex);
                    continue;
                }

                if (matTex.width == w && matTex.height == h)
                {
                    materialAtlas.SetPixels(x, y, w, h, matTex.GetPixels());
                }
                else
                {
                    Color[] src = matTex.GetPixels();
                    Color[] scaled = new Color[w * h];
                    for (int yy = 0; yy < h; yy++)
                    {
                        int srcY = Mathf.Clamp(Mathf.RoundToInt((yy / (float)h) * (matTex.height - 1)), 0, matTex.height - 1);
                        for (int xx = 0; xx < w; xx++)
                        {
                            int srcX = Mathf.Clamp(Mathf.RoundToInt((xx / (float)w) * (matTex.width - 1)), 0, matTex.width - 1);
                            scaled[(yy * w) + xx] = src[(srcY * matTex.width) + srcX];
                        }
                    }
                    materialAtlas.SetPixels(x, y, w, h, scaled);
                }

                UnityEngine.Object.DestroyImmediate(matTex);
            }

            materialAtlas.Apply();
            return materialAtlas;
        }

        private static void ConfigureBumpAtlasImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureMaterialAtlasImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static string GetBumpTexturePath(string colorPath)
        {
            if (string.IsNullOrWhiteSpace(colorPath))
            {
                return null;
            }

            string fileName = Path.GetFileName(colorPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string direct = $"{BumpTextureFolder}/{fileName}";
            if (File.Exists(GetProjectAbsolutePath(direct)))
            {
                return direct;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".png";
            }

            string withIndex = $"{BumpTextureFolder}/{name}_0{ext}";
            if (File.Exists(GetProjectAbsolutePath(withIndex)))
            {
                return withIndex;
            }

            return null;
        }

        private static string GetMaterialTexturePath(string colorPath)
        {
            if (string.IsNullOrWhiteSpace(colorPath))
            {
                return null;
            }

            string fileName = Path.GetFileName(colorPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string direct = $"{MaterialTextureFolder}/{fileName}";
            if (File.Exists(GetProjectAbsolutePath(direct)))
            {
                return direct;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".png";
            }

            string withIndex = $"{MaterialTextureFolder}/{name}_0{ext}";
            if (File.Exists(GetProjectAbsolutePath(withIndex)))
            {
                return withIndex;
            }

            return null;
        }

        private static Texture2D LoadReadableTextureFromAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string absPath = GetProjectAbsolutePath(assetPath);
            if (!File.Exists(absPath))
            {
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(absPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };
                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }

                return texture;
            }
            catch
            {
                return null;
            }
        }

        private static void CleanupReadableTextures(List<Texture2D> textures)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D tex = textures[i];
                if (tex != null)
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        private static void SetupTransparentMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedRoot))
            {
                AssetDatabase.CreateFolder("Assets", "BlockWorldGenerated");
            }

            if (!AssetDatabase.IsValidFolder(MeshFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, "Meshes");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, "Materials");
            }

            if (!AssetDatabase.IsValidFolder(AtlasFolder))
            {
                AssetDatabase.CreateFolder(GeneratedRoot, "Atlases");
            }
        }

        private static Mesh BuildCubeMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "BlockCube"
            };

            Vector3[] vertices = new Vector3[24]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),

                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),

                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),

                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),

                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),

                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            };

            Vector2[] uvs = new Vector2[24]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.subMeshCount = 6;

            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.SetTriangles(new[] { 4, 6, 5, 4, 7, 6 }, 1);
            mesh.SetTriangles(new[] { 8, 10, 9, 8, 11, 10 }, 2);
            mesh.SetTriangles(new[] { 12, 14, 13, 12, 15, 14 }, 3);
            mesh.SetTriangles(new[] { 16, 18, 17, 16, 19, 18 }, 4);
            mesh.SetTriangles(new[] { 20, 22, 21, 20, 23, 22 }, 5);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string combined = Path.Combine(projectRoot, assetPath);
            return combined.Replace("\\", "/");
        }
    }
}
