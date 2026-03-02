using System;
using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks
{
    /// <summary>
    /// 运行时后端实现：实现 <see cref="IBox3BlocksCoreBackend"/> 以承接核心流程回调。
    /// <para/>
    /// Runtime backend implementation for <see cref="IBox3BlocksCoreBackend"/>,
    /// used by <see cref="Box3BlocksCore"/> as the concrete execution layer.
    /// </summary>
    internal sealed class Box3BlocksRuntimeBackend : IBox3BlocksCoreBackend
    {
        /// <summary>
        /// 当前使用的运行时目录数据。
        /// <para/>
        /// Active runtime catalog used to resolve block rendering data.
        /// </summary>
        public Box3BlocksRuntimeCatalog Catalog { get; set; }

        /// <summary>
        /// 查找指定网格坐标上的方块对象。
        /// <para/>
        /// Find block object at a grid position under the given root.
        /// </summary>
        public GameObject FindBlockAt(Transform root, Vector3Int position)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.GetComponent<Box3BlocksPlacedBlock>() == null)
                {
                    continue;
                }

                if (Vector3Int.RoundToInt(child.position) == position)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 创建并放置方块对象。
        /// <para/>
        /// Create and place a block object.
        /// </summary>
        public bool TryPlaceBlock(Transform root, string blockId, Vector3Int position, int rotationQuarter, bool? spawnRealtimeLightOverride = null)
        {
            _ = spawnRealtimeLightOverride;
            if (!Box3BlocksRuntimeBlockFactory.TryResolve(Catalog, blockId, out Box3BlocksRuntimeBlockFactory.PlacementData data))
            {
                return false;
            }

            return Box3BlocksRuntimeBlockFactory.Create(root, blockId, position, rotationQuarter, data) != null;
        }

        /// <summary>
        /// 删除指定方块对象。
        /// <para/>
        /// Destroy an existing block object.
        /// </summary>
        public bool EraseBlock(Transform root, Vector3Int position, GameObject existing)
        {
            _ = root;
            _ = position;
            if (existing == null)
            {
                return false;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(existing);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }

            return true;
        }

        /// <summary>
        /// 按 90° 步进旋转方块对象。
        /// <para/>
        /// Rotate an existing block object in 90-degree steps.
        /// </summary>
        public bool RotateBlock(Transform root, Vector3Int position, GameObject existing, int stepQuarter)
        {
            _ = root;
            _ = position;
            if (existing == null || stepQuarter == 0)
            {
                return false;
            }

            existing.transform.Rotate(0f, stepQuarter * 90f, 0f, Space.World);
            return true;
        }

        /// <summary>
        /// 从方块对象上读取 blockId。
        /// <para/>
        /// Read block id from block marker component.
        /// </summary>
        public bool TryGetBlockId(GameObject blockObject, out string blockId)
        {
            blockId = null;
            if (blockObject == null)
            {
                return false;
            }

            Box3BlocksPlacedBlock marker = blockObject.GetComponent<Box3BlocksPlacedBlock>();
            if (marker == null || string.IsNullOrWhiteSpace(marker.BlockId))
            {
                return false;
            }

            blockId = marker.BlockId;
            return true;
        }

        /// <summary>
        /// 枚举当前根节点下的所有已占用网格坐标。
        /// <para/>
        /// Enumerate occupied grid positions under the root.
        /// </summary>
        public IEnumerable<Vector3Int> EnumerateOccupiedPositions(Transform root)
        {
            if (root == null)
            {
                yield break;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || child.GetComponent<Box3BlocksPlacedBlock>() == null)
                {
                    continue;
                }

                yield return Vector3Int.RoundToInt(child.position);
            }
        }
    }
}
