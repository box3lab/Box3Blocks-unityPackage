using UnityEngine;
using UnityEngine.Rendering;

namespace BlockWorldMVP.Editor
{
    internal static class OpaqueBlockMaterialConfigurator
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
                    material.SetTexture("_EmissionMap", BlockAssetFactory.GetAtlasEmissionTexture());
                }
            }

            Texture2D bump = BlockAssetFactory.GetAtlasBumpTexture();
            if (bump != null)
            {
                material.SetTexture("_BumpMap", bump);
                material.SetFloat("_BumpScale", 0.1f);
                material.EnableKeyword("_NORMALMAP");
                material.DisableKeyword("_PARALLAXMAP");
            }

            Texture2D metallic = BlockAssetFactory.GetAtlasMaterialTexture();
            if (metallic != null)
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.SetFloat("_Metallic", 0.2f);
                material.SetFloat("_Glossiness", 0.5f);
                material.EnableKeyword("_METALLICGLOSSMAP");
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
