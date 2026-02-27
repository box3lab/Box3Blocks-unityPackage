using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    internal static class BlockAssetFactory
    {
        private static readonly string GeneratedRoot = "Assets/BlockWorldGenerated";
        private static readonly string MeshFolder = "Assets/BlockWorldGenerated/Meshes";
        private static readonly string MaterialFolder = "Assets/BlockWorldGenerated/Materials";
        private static readonly string MeshAssetPath = "Assets/BlockWorldGenerated/Meshes/BlockCube.asset";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };

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

        public static Material[] GetFaceMaterials(Dictionary<string, string> sideTexturePaths, bool transparent)
        {
            EnsureFolders();

            Material[] materials = new Material[SideOrder.Length];
            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                if (!sideTexturePaths.TryGetValue(side, out string texturePath))
                {
                    texturePath = GetFallbackTexturePath(sideTexturePaths);
                }

                materials[i] = GetOrCreateMaterial(texturePath, transparent);
            }

            return materials;
        }

        private static string GetFallbackTexturePath(Dictionary<string, string> sideTexturePaths)
        {
            foreach (string side in SideOrder)
            {
                if (sideTexturePaths.TryGetValue(side, out string path))
                {
                    return path;
                }
            }

            return null;
        }

        private static Material GetOrCreateMaterial(string texturePath, bool transparent)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            EnsureCrispTextureImport(texturePath, transparent);

            string fileName = Path.GetFileNameWithoutExtension(texturePath);
            string variant = transparent ? "trans" : "opaque";
            string materialAssetPath = $"{MaterialFolder}/{fileName}_{variant}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
            if (material != null)
            {
                RefreshTextureSampling(material.mainTexture as Texture2D);
                return material;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                RefreshTextureSampling(texture);
            }

            Shader shader = transparent ? Shader.Find("Unlit/Transparent") : Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = fileName,
                mainTexture = texture
            };

            if (transparent)
            {
                SetupTransparentMaterial(material);
            }

            AssetDatabase.CreateAsset(material, materialAssetPath);
            return material;
        }

        private static void EnsureCrispTextureImport(string texturePath, bool transparent)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.streamingMipmaps)
            {
                importer.streamingMipmaps = false;
                changed = true;
            }

            if (importer.anisoLevel != 0)
            {
                importer.anisoLevel = 0;
                changed = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }

            if (transparent && !importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static void RefreshTextureSampling(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.filterMode = FilterMode.Point;
            texture.anisoLevel = 0;
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
        }

        private static Mesh BuildCubeMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "BlockCube"
            };

            Vector3[] vertices = new Vector3[24]
            {
                // Back
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                // Bottom
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                // Front
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                // Left
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                // Right
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                // Top
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
    }
}
