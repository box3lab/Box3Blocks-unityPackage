using UnityEngine;

namespace Box3Blocks
{
    /// <summary>
    /// 方块对象工厂：负责解析放置所需数据并创建方块 GameObject。
    /// <para/>
    /// Factory for resolving placement data and creating runtime block objects.
    /// </summary>
    internal static class Box3BlocksRuntimeBlockFactory
    {
        /// <summary>
        /// 放置阶段使用的聚合数据：
        /// 目录条目 + 共享网格 + 目标材质。
        /// <para/>
        /// Aggregated placement data:
        /// catalog entry + shared mesh + target material.
        /// </summary>
        internal readonly struct PlacementData
        {
            public readonly Box3BlocksRuntimeCatalog.Entry entry;
            public readonly Mesh mesh;
            public readonly Material material;

            public PlacementData(Box3BlocksRuntimeCatalog.Entry entry, Mesh mesh, Material material)
            {
                this.entry = entry;
                this.mesh = mesh;
                this.material = material;
            }
        }

        /// <summary>
        /// 根据 blockId 解析放置数据。
        /// <para/>
        /// Resolve placement data for a block id.
        /// </summary>
        /// <param name="catalog">运行时目录 / Runtime catalog.</param>
        /// <param name="blockId">方块 ID / Block id.</param>
        /// <param name="data">解析结果 / Resolved placement data.</param>
        /// <returns>解析成功返回 true / Returns true when resolved.</returns>
        public static bool TryResolve(Box3BlocksRuntimeCatalog catalog, string blockId, out PlacementData data)
        {
            data = default;
            if (catalog == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            if (!catalog.TryGetEntry(blockId, out Box3BlocksRuntimeCatalog.Entry entry) || entry == null)
            {
                return false;
            }

            Mesh mesh = catalog.CubeMesh;
            Material material = entry.transparent ? catalog.TransparentMaterial : catalog.OpaqueMaterial;
            if (mesh == null || material == null)
            {
                return false;
            }

            data = new PlacementData(entry, mesh, material);
            return true;
        }

        /// <summary>
        /// 创建方块对象并应用渲染与碰撞配置。
        /// <para/>
        /// Create block object and apply renderer/collider configuration.
        /// </summary>
        /// <param name="parent">父节点 / Parent transform.</param>
        /// <param name="blockId">方块 ID / Block id.</param>
        /// <param name="position">网格坐标 / Grid position.</param>
        /// <param name="rotationQuarter">旋转步进 / Rotation steps (90° each).</param>
        /// <param name="data">放置数据 / Placement data.</param>
        /// <returns>创建成功返回对象，否则返回 null / Created object, or null when failed.</returns>
        public static GameObject Create(
            Transform parent,
            string blockId,
            Vector3Int position,
            int rotationQuarter,
            PlacementData data,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            if (parent == null || data.mesh == null || data.material == null || data.entry == null)
            {
                return null;
            }

            GameObject go = new GameObject($"{blockId}_{position.x}_{position.y}_{position.z}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, (rotationQuarter & 3) * 90f, 0f);

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = data.mesh;

            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            int subMeshCount = Mathf.Max(1, data.mesh.subMeshCount);
            Material[] mats = new Material[subMeshCount];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = data.material;
            }
            meshRenderer.sharedMaterials = mats;

            Box3BlocksRuntimeRenderApplier.ApplyFaceMainTexSt(meshRenderer, data.entry.faceMainTexSt);
            Box3BlocksRuntimeRenderApplier.ApplyEmission(meshRenderer, data.entry.emitsLight ? data.entry.lightColor : Color.black);
            bool hasAnimation = Box3BlocksRuntimeRenderApplier.ConfigureFaceAnimations(meshRenderer, data.entry);

            ApplyColliderMode(go, data.mesh, colliderMode);

            Box3BlocksPlacedBlock marker = go.AddComponent<Box3BlocksPlacedBlock>();
            marker.BlockId = blockId;
            marker.HasAnimation = hasAnimation;

            return go;
        }

        private static void ApplyColliderMode(GameObject go, Mesh mesh, Box3ColliderMode colliderMode)
        {
            if (go == null || colliderMode == Box3ColliderMode.None)
            {
                return;
            }

            if (colliderMode == Box3ColliderMode.TopOnly)
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.49f, 0f);
                box.size = new Vector3(1f, 0.02f, 1f);
                return;
            }

            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }
    }
}
