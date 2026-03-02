using System;
using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks
{
    /// <summary>
    /// Core backend contract.
    /// 定义核心流程与具体执行层之间的最小接口契约。
    /// </summary>
    public interface IBox3BlocksCoreBackend
    {
        /// <summary>查找坐标上的方块对象 / Find block object at a grid position.</summary>
        GameObject FindBlockAt(Transform root, Vector3Int position);
        /// <summary>执行放置 / Execute placement.</summary>
        bool TryPlaceBlock(Transform root, string blockId, Vector3Int position, int rotationQuarter, bool? spawnRealtimeLightOverride = null);
        /// <summary>执行删除 / Execute erase.</summary>
        bool EraseBlock(Transform root, Vector3Int position, GameObject existing);
        /// <summary>执行旋转 / Execute rotation.</summary>
        bool RotateBlock(Transform root, Vector3Int position, GameObject existing, int stepQuarter);
        /// <summary>读取 blockId / Read block id.</summary>
        bool TryGetBlockId(GameObject blockObject, out string blockId);
        /// <summary>枚举占用坐标 / Enumerate occupied positions.</summary>
        IEnumerable<Vector3Int> EnumerateOccupiedPositions(Transform root);
    }

    /// <summary>
    /// 方块核心流程（与具体渲染/对象管理解耦）。
    /// 提供统一的放置、删除、替换、旋转、查询算法。
    /// <para/>
    /// Core block operation workflow (decoupled from rendering/object lifecycle).
    /// Provides unified algorithms for place/erase/replace/rotate/query.
    /// </summary>
    public static class Box3BlocksCore
    {
        /// <summary>
        /// 放置方块（可选替换已有方块）。
        /// <para/>
        /// Place block at target position, with optional replacement.
        /// </summary>
        public static bool TryPlaceBlockAt(
            IBox3BlocksCoreBackend backend,
            Transform root,
            string blockId,
            Vector3Int position,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLightOverride = null)
        {
            if (backend == null || root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            GameObject existing = backend.FindBlockAt(root, position);
            if (existing != null)
            {
                if (!replaceExisting || !backend.EraseBlock(root, position, existing))
                {
                    return false;
                }
            }

            return backend.TryPlaceBlock(root, blockId, position, rotationQuarter, spawnRealtimeLightOverride);
        }

        /// <summary>
        /// 在 (x,z) 列顶部放置方块。
        /// <para/>
        /// Place block on top of column (x,z).
        /// </summary>
        public static bool TryPlaceBlockOnTop(
            IBox3BlocksCoreBackend backend,
            Transform root,
            string blockId,
            int x,
            int z,
            int baseY = 0,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLightOverride = null)
        {
            if (backend == null || root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            int topY = int.MinValue;
            foreach (Vector3Int p in backend.EnumerateOccupiedPositions(root))
            {
                if (p.x == x && p.z == z && p.y > topY)
                {
                    topY = p.y;
                }
            }

            int targetY = topY == int.MinValue ? baseY : topY + 1;
            return TryPlaceBlockAt(
                backend,
                root,
                blockId,
                new Vector3Int(x, targetY, z),
                replaceExisting,
                rotationQuarter,
                spawnRealtimeLightOverride);
        }

        /// <summary>
        /// 在包围盒范围内批量放置。
        /// <para/>
        /// Batch place in inclusive bounds.
        /// </summary>
        public static int PlaceBlocksInBounds(
            IBox3BlocksCoreBackend backend,
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLightOverride = null)
        {
            if (backend == null || root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return 0;
            }

            return CountInBounds(minInclusive, maxInclusive, p =>
                TryPlaceBlockAt(backend, root, blockId, p, replaceExisting, rotationQuarter, spawnRealtimeLightOverride));
        }

        /// <summary>
        /// 删除单个方块。
        /// <para/>
        /// Erase a single block.
        /// </summary>
        public static bool EraseBlockAt(IBox3BlocksCoreBackend backend, Transform root, Vector3Int position)
        {
            if (backend == null || root == null)
            {
                return false;
            }

            GameObject existing = backend.FindBlockAt(root, position);
            return existing != null && backend.EraseBlock(root, position, existing);
        }

        /// <summary>
        /// 包围盒内批量删除。
        /// <para/>
        /// Batch erase in inclusive bounds.
        /// </summary>
        public static int EraseBlocksInBounds(IBox3BlocksCoreBackend backend, Transform root, Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            if (backend == null || root == null)
            {
                return 0;
            }

            return CountInBounds(minInclusive, maxInclusive, p => EraseBlockAt(backend, root, p));
        }

        /// <summary>
        /// 替换单个方块。
        /// <para/>
        /// Replace a single block.
        /// </summary>
        public static bool ReplaceBlockAt(
            IBox3BlocksCoreBackend backend,
            Transform root,
            string blockId,
            Vector3Int position,
            int rotationQuarter = 0,
            bool? spawnRealtimeLightOverride = null)
        {
            if (backend == null || root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            GameObject existing = backend.FindBlockAt(root, position);
            if (existing == null)
            {
                return false;
            }

            if (backend.TryGetBlockId(existing, out string existingId)
                && string.Equals(existingId, blockId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return backend.EraseBlock(root, position, existing)
                && backend.TryPlaceBlock(root, blockId, position, rotationQuarter, spawnRealtimeLightOverride);
        }

        /// <summary>
        /// 包围盒内批量替换。
        /// <para/>
        /// Batch replace in inclusive bounds.
        /// </summary>
        public static int ReplaceBlocksInBounds(
            IBox3BlocksCoreBackend backend,
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            int rotationQuarter = 0,
            bool? spawnRealtimeLightOverride = null)
        {
            if (backend == null || root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return 0;
            }

            return CountInBounds(minInclusive, maxInclusive, p =>
                ReplaceBlockAt(backend, root, blockId, p, rotationQuarter, spawnRealtimeLightOverride));
        }

        /// <summary>
        /// 旋转单个方块。
        /// <para/>
        /// Rotate a single block.
        /// </summary>
        public static bool RotateBlockAt(IBox3BlocksCoreBackend backend, Transform root, Vector3Int position, int stepQuarter = 1)
        {
            if (backend == null || root == null)
            {
                return false;
            }

            int normalized = NormalizeQuarterStep(stepQuarter);
            if (normalized == 0)
            {
                return false;
            }

            GameObject existing = backend.FindBlockAt(root, position);
            return existing != null && backend.RotateBlock(root, position, existing, normalized);
        }

        /// <summary>
        /// 包围盒内批量旋转。
        /// <para/>
        /// Batch rotate in inclusive bounds.
        /// </summary>
        public static int RotateBlocksInBounds(
            IBox3BlocksCoreBackend backend,
            Transform root,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            int stepQuarter = 1)
        {
            if (backend == null || root == null)
            {
                return 0;
            }

            return CountInBounds(minInclusive, maxInclusive, p => RotateBlockAt(backend, root, p, stepQuarter));
        }

        /// <summary>
        /// 查询坐标上的 blockId。
        /// <para/>
        /// Query block id at position.
        /// </summary>
        public static bool TryGetBlockIdAt(IBox3BlocksCoreBackend backend, Transform root, Vector3Int position, out string blockId)
        {
            blockId = null;
            if (backend == null || root == null)
            {
                return false;
            }

            GameObject existing = backend.FindBlockAt(root, position);
            return existing != null && backend.TryGetBlockId(existing, out blockId) && !string.IsNullOrWhiteSpace(blockId);
        }

        /// <summary>
        /// 判断坐标上是否存在方块。
        /// <para/>
        /// Check whether a block exists at position.
        /// </summary>
        public static bool ExistsAt(IBox3BlocksCoreBackend backend, Transform root, Vector3Int position)
        {
            return backend != null && root != null && backend.FindBlockAt(root, position) != null;
        }

        /// <summary>
        /// 获取 (x,z) 列的最高 Y。
        /// <para/>
        /// Get top Y value in column (x,z).
        /// </summary>
        public static int GetTopY(IBox3BlocksCoreBackend backend, Transform root, int x, int z, int fallbackY = 0)
        {
            if (backend == null || root == null)
            {
                return fallbackY;
            }

            int topY = int.MinValue;
            foreach (Vector3Int p in backend.EnumerateOccupiedPositions(root))
            {
                if (p.x == x && p.z == z && p.y > topY)
                {
                    topY = p.y;
                }
            }

            return topY == int.MinValue ? fallbackY : topY;
        }

        /// <summary>
        /// 对包围盒坐标执行统一遍历并统计成功次数。
        /// <para/>
        /// Iterate inclusive bounds and count successful operations.
        /// </summary>
        private static int CountInBounds(Vector3Int a, Vector3Int b, Func<Vector3Int, bool> op)
        {
            int minX = Mathf.Min(a.x, b.x);
            int minY = Mathf.Min(a.y, b.y);
            int minZ = Mathf.Min(a.z, b.z);
            int maxX = Mathf.Max(a.x, b.x);
            int maxY = Mathf.Max(a.y, b.y);
            int maxZ = Mathf.Max(a.z, b.z);

            int count = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (op(new Vector3Int(x, y, z)))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 归一化 90° 步进到 [0,3]。
        /// <para/>
        /// Normalize quarter-step rotation into [0,3].
        /// </summary>
        private static int NormalizeQuarterStep(int stepQuarter)
        {
            int normalized = stepQuarter % 4;
            if (normalized < 0)
            {
                normalized += 4;
            }

            return normalized;
        }
    }
}
