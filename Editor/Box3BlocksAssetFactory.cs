using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Box3Blocks.Editor
{
    internal static class Box3BlocksAssetFactory
    {
        internal sealed class FaceRenderData
        {
            public Material[] materials;
            public Vector4[] faceMainTexSt;
        }

        private const string BlockTextureFolder = "Packages/com.box3lab.box3/Editor/SourceAssets/block";
        private const string BlockSpecPath = "Packages/com.box3lab.box3/Editor/SourceAssets/block-spec.json";
        private const string BumpTextureFolder = "Packages/com.box3lab.box3/Editor/SourceAssets/bump";
        private const string MaterialTextureFolder = "Packages/com.box3lab.box3/Editor/SourceAssets/material";
        private static readonly string GeneratedRoot = "Assets/Box3";
        private static readonly string MeshFolder = "Assets/Box3/Meshes";
        private static readonly string MaterialFolder = "Assets/Box3/Materials";
        private static readonly string AtlasFolder = "Assets/Box3/Textures/Atlases";
        private static readonly string MeshAssetPath = "Assets/Box3/Meshes/BlockCube.asset";
        private static readonly string AtlasTexturePath = "Assets/Box3/Textures/Atlases/T_Block_Color_Atlas.asset";
        private static readonly string AtlasBumpTexturePath = "Assets/Box3/Textures/Atlases/T_Block_Bump_Atlas.asset";
        private static readonly string AtlasMaterialTexturePath = "Assets/Box3/Textures/Atlases/T_Block_Metallic_Atlas.asset";
        private static readonly string AtlasEmissionTexturePath = "Assets/Box3/Textures/Atlases/T_Block_Emission_Atlas.asset";
        private static readonly string AtlasTransparentMaterialPath = "Assets/Box3/Materials/M_Block_Transparent.mat";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex("^(.*)_(back|bottom|front|left|right|top)\\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, Rect> AtlasUvByTexturePath = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D _atlasTexture;
        private static Texture2D _atlasBumpTexture;
        private static Texture2D _atlasMaterialTexture;
        private static Texture2D _atlasEmissionTexture;
        private static Material _atlasTransparentMaterial;
        private static bool _atlasReady;
        private static HashSet<string> _emissiveBlockIds;

        public static void InvalidateCaches()
        {
            _atlasReady = false;
            _atlasTexture = null;
            _atlasBumpTexture = null;
            _atlasMaterialTexture = null;
            _atlasEmissionTexture = null;
            _atlasTransparentMaterial = null;
            _emissiveBlockIds = null;
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

        public static Texture2D GetAtlasEmissionTexture()
        {
            if (!EnsureAtlasResources())
            {
                return null;
            }

            return _atlasEmissionTexture;
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

            int maxSize = Mathf.Clamp(
                Mathf.Min(SystemInfo.maxTextureSize, Box3BlocksAtlasQualitySettings.GetMaxSize()),
                128,
                8192);
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
            _atlasEmissionTexture = BuildEmissionAtlasTexture(_atlasTexture, texturePaths);
            PersistAtlasEmissionTexture(_atlasEmissionTexture);
            _atlasTransparentMaterial = GetOrCreateTransparentAtlasMaterial(AtlasTransparentMaterialPath, _atlasTexture);

            const string legacyOpaqueMaterialPath = "Assets/Box3/Atlases/WorldBuilderAtlas_Opaque.mat";
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
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasEmissionTexturePath) != null)
            {
                AssetDatabase.ImportAsset(AtlasEmissionTexturePath, ImportAssetOptions.ForceUpdate);
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

            string expectedName = GetAssetFileNameWithoutExtension(AtlasTexturePath);
            generatedTexture.name = expectedName;
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generatedTexture, AtlasTexturePath);
                atlasTexture = generatedTexture;
            }
            else
            {
                EditorUtility.CopySerialized(generatedTexture, existing);
                existing.name = expectedName;
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

            string expectedName = GetAssetFileNameWithoutExtension(AtlasBumpTexturePath);
            bumpTexture.name = expectedName;
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasBumpTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(bumpTexture, AtlasBumpTexturePath);
                ConfigureBumpAtlasImporter(AtlasBumpTexturePath);
                return;
            }

            EditorUtility.CopySerialized(bumpTexture, existing);
            existing.name = expectedName;
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

            string expectedName = GetAssetFileNameWithoutExtension(AtlasMaterialTexturePath);
            materialTexture.name = expectedName;
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasMaterialTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(materialTexture, AtlasMaterialTexturePath);
                ConfigureMaterialAtlasImporter(AtlasMaterialTexturePath);
                return;
            }

            EditorUtility.CopySerialized(materialTexture, existing);
            existing.name = expectedName;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(materialTexture);
            ConfigureMaterialAtlasImporter(AtlasMaterialTexturePath);
        }

        private static void PersistAtlasEmissionTexture(Texture2D emissionTexture)
        {
            if (emissionTexture == null)
            {
                return;
            }

            string expectedName = GetAssetFileNameWithoutExtension(AtlasEmissionTexturePath);
            emissionTexture.name = expectedName;
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasEmissionTexturePath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(emissionTexture, AtlasEmissionTexturePath);
                ConfigureEmissionAtlasImporter(AtlasEmissionTexturePath);
                return;
            }

            EditorUtility.CopySerialized(emissionTexture, existing);
            existing.name = expectedName;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(emissionTexture);
            ConfigureEmissionAtlasImporter(AtlasEmissionTexturePath);
        }

        private static string GetAssetFileNameWithoutExtension(string assetPath)
        {
            return Path.GetFileNameWithoutExtension(assetPath);
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

        private static Texture2D BuildEmissionAtlasTexture(Texture2D colorAtlas, List<string> colorTexturePaths)
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

            Texture2D emissionAtlas = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "WorldBuilderAtlas_Emission",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            Color[] clear = new Color[width * height];
            for (int i = 0; i < clear.Length; i++)
            {
                clear[i] = Color.black;
            }
            emissionAtlas.SetPixels(clear);

            for (int i = 0; i < colorTexturePaths.Count; i++)
            {
                string colorPath = colorTexturePaths[i];
                if (!AtlasUvByTexturePath.TryGetValue(colorPath, out Rect uvRect))
                {
                    continue;
                }

                if (!IsEmissionTexturePath(colorPath))
                {
                    continue;
                }

                Texture2D srcTex = LoadReadableTextureFromAsset(colorPath);
                if (srcTex == null)
                {
                    continue;
                }

                int x = Mathf.RoundToInt(uvRect.x * width);
                int y = Mathf.RoundToInt(uvRect.y * height);
                int w = Mathf.RoundToInt(uvRect.width * width);
                int h = Mathf.RoundToInt(uvRect.height * height);
                if (w <= 0 || h <= 0)
                {
                    UnityEngine.Object.DestroyImmediate(srcTex);
                    continue;
                }

                if (srcTex.width == w && srcTex.height == h)
                {
                    emissionAtlas.SetPixels(x, y, w, h, srcTex.GetPixels());
                }
                else
                {
                    Color[] src = srcTex.GetPixels();
                    Color[] scaled = new Color[w * h];
                    for (int yy = 0; yy < h; yy++)
                    {
                        int srcY = Mathf.Clamp(Mathf.RoundToInt((yy / (float)h) * (srcTex.height - 1)), 0, srcTex.height - 1);
                        for (int xx = 0; xx < w; xx++)
                        {
                            int srcX = Mathf.Clamp(Mathf.RoundToInt((xx / (float)w) * (srcTex.width - 1)), 0, srcTex.width - 1);
                            scaled[(yy * w) + xx] = src[(srcY * srcTex.width) + srcX];
                        }
                    }
                    emissionAtlas.SetPixels(x, y, w, h, scaled);
                }

                UnityEngine.Object.DestroyImmediate(srcTex);
            }

            emissionAtlas.Apply();
            return emissionAtlas;
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

        private static void ConfigureEmissionAtlasImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
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

        private static bool IsEmissionTexturePath(string colorPath)
        {
            if (string.IsNullOrWhiteSpace(colorPath))
            {
                return false;
            }

            string fileName = Path.GetFileNameWithoutExtension(colorPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            int split = fileName.LastIndexOf('_');
            string id = split > 0 ? fileName.Substring(0, split) : fileName;
            EnsureEmissiveBlockIdCache();
            if (_emissiveBlockIds != null && _emissiveBlockIds.Contains(id))
            {
                return true;
            }

            return Box3BlocksIdRules.IsEmissiveKeyword(id);
        }

        private static void EnsureEmissiveBlockIdCache()
        {
            if (_emissiveBlockIds != null)
            {
                return;
            }

            _emissiveBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (!File.Exists(absPath))
            {
                return;
            }

            string json = File.ReadAllText(absPath);
            Dictionary<string, string> objects = ExtractTopLevelObjectValues(json);
            foreach (KeyValuePair<string, string> pair in objects)
            {
                string name = pair.Key;
                string body = pair.Value;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                bool emits = HasMeaningfulEmission(body)
                    || ReadBoolField(body, "emissive")
                    || Regex.IsMatch(body, "\"glow\"\\s*:\\s*(true|1)", RegexOptions.IgnoreCase)
                    || Box3BlocksIdRules.IsEmissiveKeyword(name);

                if (emits)
                {
                    _emissiveBlockIds.Add(name);
                }
            }
        }

        private static bool HasMeaningfulEmission(string body)
        {
            Match m = Regex.Match(body, "\"emissive\"\\s*:\\s*\\[(?<value>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return false;
            }

            string[] parts = m.Groups["value"].Value.Split(',');
            float sum = 0f;
            for (int i = 0; i < Mathf.Min(3, parts.Length); i++)
            {
                sum += Mathf.Abs(ParseFloatSafe(parts[i], 0f));
            }

            return sum > 0.001f;
        }

        private static bool ReadBoolField(string text, string fieldName)
        {
            return Box3BlocksJsonLite.ReadBoolField(text, fieldName);
        }

        private static float ParseFloatSafe(string text, float fallback)
        {
            return Box3BlocksJsonLite.ParseFloatSafe(text, fallback);
        }

        private static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
        {
            return Box3BlocksJsonLite.ExtractTopLevelObjectValues(json);
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
            EnsureFolderPath(GeneratedRoot);
            EnsureFolderPath(MeshFolder);
            EnsureFolderPath(MaterialFolder);
            EnsureFolderPath(AtlasFolder);

        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            string normalized = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] parts = normalized.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                return;
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
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
