using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks.Editor
{
    /// <summary>
    /// Box3 编辑时 API。
    /// <para/>
    /// Public Editor API for Box3 block operations.
    /// </summary>
    public static class Box3Api
    {
        /// <summary>
        /// 在指定格子坐标尝试放置一个方块。
        /// <para/>
        /// Try placing a block at a grid position.
        /// </summary>
        /// <param name="root">方块根节点（生成对象会挂在该节点下）。 / Block root transform (spawned blocks become its children).</param>
        /// <param name="blockId">目标方块 ID。 / Target block id.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <param name="replaceExisting">目标位置已有方块时是否替换。 / Whether to replace existing block at target position.</param>
        /// <param name="rotationQuarter">放置旋转（90° 步进）。 / Placement rotation in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用全局设置。 / Whether to spawn realtime point light; null uses global setting.</param>
        /// <param name="colliderMode">碰撞体模式。 / Collider mode.</param>
        /// <returns>放置成功返回 true。 / Returns true on success.</returns>
        public static bool TryPlaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            return Box3BlocksBuilderWindow.TryPlaceBlockAtApi(root, blockId, position, replaceExisting, (int)rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 在指定列 (x,z) 的顶部放置一个方块。
        /// <para/>
        /// Try placing a block on top of column (x,z).
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">目标方块 ID。 / Target block id.</param>
        /// <param name="x">列 X 坐标。 / Column X.</param>
        /// <param name="z">列 Z 坐标。 / Column Z.</param>
        /// <param name="baseY">空列时使用的起始 Y。 / Base Y used when the column is empty.</param>
        /// <param name="replaceExisting">最终落点已有方块时是否替换。 / Whether to replace existing block at final position.</param>
        /// <param name="rotationQuarter">放置旋转（90° 步进）。 / Placement rotation in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用全局设置。 / Whether to spawn realtime point light; null uses global setting.</param>
        /// <param name="colliderMode">碰撞体模式。 / Collider mode.</param>
        /// <returns>放置成功返回 true。 / Returns true on success.</returns>
        public static bool TryPlaceBlockOnTop(
            Transform root,
            string blockId,
            int x,
            int z,
            int baseY = 0,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            return Box3BlocksBuilderWindow.TryPlaceBlockOnTopApi(root, blockId, x, z, baseY, replaceExisting, (int)rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 在包围盒范围内批量放置方块。
        /// <para/>
        /// Batch place blocks in inclusive bounds.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">目标方块 ID。 / Target block id.</param>
        /// <param name="minInclusive">最小角点（包含）。 / Inclusive min corner.</param>
        /// <param name="maxInclusive">最大角点（包含）。 / Inclusive max corner.</param>
        /// <param name="replaceExisting">范围内已有方块时是否替换。 / Whether to replace existing blocks in bounds.</param>
        /// <param name="rotationQuarter">放置旋转（90° 步进）。 / Placement rotation in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用全局设置。 / Whether to spawn realtime point light; null uses global setting.</param>
        /// <param name="colliderMode">碰撞体模式。 / Collider mode.</param>
        /// <returns>成功放置数量。 / Number of placed blocks.</returns>
        public static int PlaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            return Box3BlocksBuilderWindow.PlaceBlocksInBoundsApi(root, blockId, minInclusive, maxInclusive, replaceExisting, (int)rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 删除指定坐标上的方块。
        /// <para/>
        /// Erase block at a position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <returns>删除成功返回 true。 / Returns true on success.</returns>
        public static bool EraseBlockAt(Transform root, Vector3Int position)
        {
            return Box3BlocksBuilderWindow.EraseBlockAtApi(root, position);
        }

        /// <summary>
        /// 删除包围盒范围内的方块。
        /// <para/>
        /// Batch erase blocks in inclusive bounds.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="minInclusive">最小角点（包含）。 / Inclusive min corner.</param>
        /// <param name="maxInclusive">最大角点（包含）。 / Inclusive max corner.</param>
        /// <returns>成功删除数量。 / Number of erased blocks.</returns>
        public static int EraseBlocksInBounds(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            return Box3BlocksBuilderWindow.EraseBlocksInBoundsApi(root, minInclusive, maxInclusive);
        }

        /// <summary>
        /// 将指定坐标上的方块替换为目标方块。
        /// <para/>
        /// Replace block at a position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">替换后的方块 ID。 / New block id.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <param name="rotationQuarter">替换后的旋转（90° 步进）。 / Rotation after replace in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用全局设置。 / Whether to spawn realtime point light; null uses global setting.</param>
        /// <param name="colliderMode">碰撞体模式。 / Collider mode.</param>
        /// <returns>替换成功返回 true。 / Returns true on success.</returns>
        public static bool ReplaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            return Box3BlocksBuilderWindow.ReplaceBlockAtApi(root, blockId, position, (int)rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 在包围盒范围内替换方块。
        /// <para/>
        /// Batch replace blocks in inclusive bounds.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">替换后的方块 ID。 / New block id.</param>
        /// <param name="minInclusive">最小角点（包含）。 / Inclusive min corner.</param>
        /// <param name="maxInclusive">最大角点（包含）。 / Inclusive max corner.</param>
        /// <param name="rotationQuarter">替换后的旋转（90° 步进）。 / Rotation after replace in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用全局设置。 / Whether to spawn realtime point light; null uses global setting.</param>
        /// <param name="colliderMode">碰撞体模式。 / Collider mode.</param>
        /// <returns>成功替换数量。 / Number of replaced blocks.</returns>
        public static int ReplaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode colliderMode = Box3ColliderMode.Full)
        {
            return Box3BlocksBuilderWindow.ReplaceBlocksInBoundsApi(root, blockId, minInclusive, maxInclusive, (int)rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 旋转指定坐标上的方块。
        /// <para/>
        /// Rotate a block at a position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <param name="stepQuarter">旋转步进（90° 单位）。 / Rotation step in quarter turns.</param>
        /// <returns>旋转成功返回 true。 / Returns true on success.</returns>
        public static bool RotateBlockAt(
            Transform root,
            Vector3Int position,
            Box3QuarterTurn stepQuarter = Box3QuarterTurn.R90)
        {
            return Box3BlocksBuilderWindow.RotateBlockAtApi(root, position, (int)stepQuarter);
        }

        /// <summary>
        /// 旋转包围盒范围内的方块。
        /// <para/>
        /// Batch rotate blocks in inclusive bounds.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="minInclusive">最小角点（包含）。 / Inclusive min corner.</param>
        /// <param name="maxInclusive">最大角点（包含）。 / Inclusive max corner.</param>
        /// <param name="stepQuarter">旋转步进（90° 单位）。 / Rotation step in quarter turns.</param>
        /// <returns>成功旋转数量。 / Number of rotated blocks.</returns>
        public static int RotateBlocksInBounds(
            Transform root,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            Box3QuarterTurn stepQuarter = Box3QuarterTurn.R90)
        {
            return Box3BlocksBuilderWindow.RotateBlocksInBoundsApi(root, minInclusive, maxInclusive, (int)stepQuarter);
        }

        /// <summary>
        /// 查询指定坐标上的方块 ID。
        /// <para/>
        /// Query block id at a position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <param name="blockId">输出方块 ID。 / Output block id.</param>
        /// <returns>命中方块返回 true。 / Returns true when a block exists at position.</returns>
        public static bool TryGetBlockIdAt(Transform root, Vector3Int position, out string blockId)
        {
            return Box3BlocksBuilderWindow.TryGetBlockIdAtApi(root, position, out blockId);
        }

        /// <summary>
        /// 判断指定坐标是否存在方块。
        /// <para/>
        /// Check whether a block exists at position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <returns>存在返回 true。 / Returns true when exists.</returns>
        public static bool ExistsAt(Transform root, Vector3Int position)
        {
            return Box3BlocksBuilderWindow.ExistsAtApi(root, position);
        }

        /// <summary>
        /// 获取指定列 (x,z) 的顶部 Y。
        /// <para/>
        /// Get top Y value for a column (x,z).
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="x">列 X 坐标。 / Column X.</param>
        /// <param name="z">列 Z 坐标。 / Column Z.</param>
        /// <param name="fallbackY">空列时返回值。 / Value returned when column is empty.</param>
        /// <returns>顶部 Y。 / Top Y value.</returns>
        public static int GetTopY(Transform root, int x, int z, int fallbackY = 0)
        {
            return Box3BlocksBuilderWindow.GetTopYApi(root, x, z, fallbackY);
        }

        /// <summary>
        /// 获取当前可放置的全部方块 ID 列表。
        /// <para/>
        /// Get all available block ids.
        /// </summary>
        /// <returns>只读 ID 列表。 / Read-only list of block ids.</returns>
        public static IReadOnlyList<string> GetAvailableBlockIds()
        {
            return Box3BlocksBuilderWindow.GetAvailableBlockIdsApi();
        }

        /// <summary>
        /// 判断方块 ID 是否属于透明方块。
        /// <para/>
        /// Check whether a block id is transparent.
        /// </summary>
        /// <param name="blockId">方块 ID。 / Block id.</param>
        /// <returns>透明返回 true。 / Returns true if transparent.</returns>
        public static bool IsTransparent(string blockId)
        {
            return Box3BlocksBuilderWindow.IsTransparentApi(blockId);
        }

        /// <summary>
        /// 预热并生成 Box3 资源（网格、图集、材质）。
        /// <para/>
        /// Warm up and generate Box3 assets (mesh, atlases, materials).
        /// </summary>
        /// <returns>全部关键资源可用时返回 true。 / Returns true when required assets are available.</returns>
        public static bool PrepareGeneratedAssets()
        {
            Box3BlocksBuilderWindow.ReloadLibraryApi();
            Box3BlocksAssetFactory.InvalidateCaches();
            Mesh mesh = Box3BlocksAssetFactory.GetOrCreateCubeMesh();
            Material atlasMaterial = Box3BlocksAssetFactory.GetAtlasMaterial();
            Texture2D bump = Box3BlocksAssetFactory.GetAtlasBumpTexture();
            Texture2D metallic = Box3BlocksAssetFactory.GetAtlasMaterialTexture();
            Texture2D emission = Box3BlocksAssetFactory.GetAtlasEmissionTexture();
            bool coreReady = mesh != null && atlasMaterial != null;
            if (!coreReady)
            {
                Debug.LogError(
                    $"[Box3] PrepareGeneratedAssets failed. " +
                    $"mesh={(mesh != null)}, atlasMaterial={(atlasMaterial != null)}, " +
                    $"bump={(bump != null)}, metallic={(metallic != null)}, emission={(emission != null)}");
                return false;
            }

            if (bump == null || metallic == null || emission == null)
            {
                Debug.LogWarning(
                    $"[Box3] PrepareGeneratedAssets: optional atlas textures missing. " +
                    $"bump={(bump != null)}, metallic={(metallic != null)}, emission={(emission != null)}. " +
                    $"Core placement resources are ready.");
            }

            return true;
        }

        /// <summary>
        /// 设置发光方块默认是否生成点光源。
        /// <para/>
        /// Set default flag for spawning realtime point lights on emissive blocks.
        /// </summary>
        /// <param name="enabled">默认开关。 / Default switch.</param>
        public static void SetSpawnRealtimeLightForEmissive(bool enabled)
        {
            Box3BlocksBuilderWindow.SetSpawnRealtimeLightForEmissiveApi(enabled);
        }

        /// <summary>
        /// 获取发光方块默认点光源开关状态。
        /// <para/>
        /// Get default point-light spawn flag for emissive blocks.
        /// </summary>
        /// <returns>当前默认状态。 / Current default state.</returns>
        public static bool GetSpawnRealtimeLightForEmissive()
        {
            return Box3BlocksBuilderWindow.GetSpawnRealtimeLightForEmissiveApi();
        }
    }
}
