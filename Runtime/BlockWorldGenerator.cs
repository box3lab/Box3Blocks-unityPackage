using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockWorldMVP
{
    public class BlockWorldGenerator : MonoBehaviour
    {
        [Serializable]
        private class BlockArrayWrapper
        {
            public BlockData[] blocks;
        }

        [Header("Config")]
        [SerializeField]
        private BlockCatalog catalog;

        [SerializeField]
        private TextAsset blocksJson;

        [SerializeField]
        private Transform root;

        [SerializeField]
        private bool clearBeforeBuild = true;

        public void BuildFromInspectorJson()
        {
            if (blocksJson == null)
            {
                Debug.LogError("[BlockWorldMVP] blocksJson is not assigned.");
                return;
            }

            if (!TryParseBlocks(blocksJson.text, out BlockData[] blocks))
            {
                Debug.LogError("[BlockWorldMVP] Failed to parse JSON. Expected {\"blocks\":[...]}.");
                return;
            }

            Build(blocks);
        }

        public void Build(IReadOnlyList<BlockData> blocks)
        {
            if (catalog == null)
            {
                Debug.LogError("[BlockWorldMVP] catalog is not assigned.");
                return;
            }

            EnsureRoot();

            if (clearBeforeBuild)
            {
                ClearGenerated();
            }

            int created = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                BlockData b = blocks[i];
                if (!catalog.TryGetPrefab(b.id, out GameObject prefab))
                {
                    Debug.LogWarning($"[BlockWorldMVP] Missing block id in catalog: {b.id}");
                    continue;
                }

                Vector3 pos = new Vector3(b.x, b.y, b.z);
                Instantiate(prefab, pos, Quaternion.identity, root);
                created++;
            }

            Debug.Log($"[BlockWorldMVP] Build complete. Created {created} blocks.");
        }

        public void ClearGenerated()
        {
            EnsureRoot();

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void EnsureRoot()
        {
            if (root != null)
            {
                return;
            }

            root = transform;
        }

        private static bool TryParseBlocks(string json, out BlockData[] blocks)
        {
            blocks = Array.Empty<BlockData>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                BlockArrayWrapper wrapper = JsonUtility.FromJson<BlockArrayWrapper>(json);
                if (wrapper == null || wrapper.blocks == null)
                {
                    return false;
                }

                blocks = wrapper.blocks;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
