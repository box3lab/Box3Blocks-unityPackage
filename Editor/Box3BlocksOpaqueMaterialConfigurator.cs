using UnityEngine;
using UnityEngine.Rendering;

namespace Box3Blocks.Editor
{
    internal static class Box3BlocksOpaqueMaterialConfigurator
    {
        public static void Apply(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", Color.white);
                if (material.HasProperty("_EmissionMap"))
                {
                    material.SetTexture("_EmissionMap", Box3BlocksAssetFactory.GetAtlasEmissionTexture());
                }
            }

            Texture2D bump = Box3BlocksAssetFactory.GetAtlasBumpTexture();
            if (bump != null)
            {
                material.SetTexture("_BumpMap", bump);
                material.SetFloat("_BumpScale", 0.1f);
                material.EnableKeyword("_NORMALMAP");
                material.DisableKeyword("_PARALLAXMAP");
            }

            Texture2D metallic = Box3BlocksAssetFactory.GetAtlasMaterialTexture();
            if (metallic != null)
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.SetFloat("_Metallic", 0.2f);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.15f);
            }

            if (material.HasProperty("_GlossMapScale"))
            {
                material.SetFloat("_GlossMapScale", 0.15f);
            }

            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
        }
    }
}
