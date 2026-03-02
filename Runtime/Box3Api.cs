using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Box3Blocks
{
    /// <summary>
    /// Box3 运行时 API。
    /// <para/>
    /// Runtime Box3 API.
    /// </summary>
    public static class Box3Api
    {
        private static Box3BlocksRuntimeCatalog _defaultRuntimeCatalog;
        private static bool _missingCatalogWarningLogged;

        /// <summary>
        /// 在指定格子坐标尝试放置一个方块。
        /// <para/>
        /// Try placing a block at a grid position.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">目标方块 ID。 / Target block id.</param>
        /// <param name="position">目标网格坐标。 / Target grid position.</param>
        /// <param name="replaceExisting">目标位置已有方块时是否替换。 / Whether to replace existing block at target position.</param>
        /// <param name="rotationQuarter">放置旋转（90° 步进）。 / Placement rotation in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用默认设置。 / Whether to spawn realtime point light; null uses default setting.</param>
        /// <param name="colliderMode">碰撞体模式；null 表示使用服务默认模式。 / Collider mode; null uses service default mode.</param>
        /// <returns>放置成功返回 true。 / Returns true on success.</returns>
        public static bool TryPlaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode? colliderMode = null)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service)
                && service.TryPlaceBlockAt(blockId, position, replaceExisting, rotationQuarter, spawnRealtimeLight, colliderMode);
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
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用默认设置。 / Whether to spawn realtime point light; null uses default setting.</param>
        /// <param name="colliderMode">碰撞体模式；null 表示使用服务默认模式。 / Collider mode; null uses service default mode.</param>
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
            Box3ColliderMode? colliderMode = null)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service)
                && service.TryPlaceBlockOnTop(blockId, x, z, baseY, replaceExisting, rotationQuarter, spawnRealtimeLight, colliderMode);
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
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用默认设置。 / Whether to spawn realtime point light; null uses default setting.</param>
        /// <param name="colliderMode">碰撞体模式；null 表示使用服务默认模式。 / Collider mode; null uses service default mode.</param>
        /// <returns>成功放置数量。 / Number of placed blocks.</returns>
        public static int PlaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode? colliderMode = null)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service)
                ? service.PlaceBlocksInBounds(blockId, minInclusive, maxInclusive, replaceExisting, rotationQuarter, spawnRealtimeLight, colliderMode)
                : 0;
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
            return TryGetService(root, out Box3BlocksRuntimeService service) && service.EraseBlockAt(position);
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
            return TryGetService(root, out Box3BlocksRuntimeService service)
                ? service.EraseBlocksInBounds(minInclusive, maxInclusive)
                : 0;
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
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用默认设置。 / Whether to spawn realtime point light; null uses default setting.</param>
        /// <param name="colliderMode">碰撞体模式；null 表示使用服务默认模式。 / Collider mode; null uses service default mode.</param>
        /// <returns>替换成功返回 true。 / Returns true on success.</returns>
        public static bool ReplaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode? colliderMode = null)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service)
                && service.ReplaceBlockAt(blockId, position, rotationQuarter, spawnRealtimeLight, colliderMode);
        }

        /// <summary>
        /// 在包围盒范围内批量替换方块。
        /// <para/>
        /// Batch replace blocks in inclusive bounds.
        /// </summary>
        /// <param name="root">方块根节点。 / Block root transform.</param>
        /// <param name="blockId">替换后的方块 ID。 / New block id.</param>
        /// <param name="minInclusive">最小角点（包含）。 / Inclusive min corner.</param>
        /// <param name="maxInclusive">最大角点（包含）。 / Inclusive max corner.</param>
        /// <param name="rotationQuarter">替换后的旋转（90° 步进）。 / Rotation after replace in quarter turns.</param>
        /// <param name="spawnRealtimeLight">是否生成实时点光源；null 表示使用默认设置。 / Whether to spawn realtime point light; null uses default setting.</param>
        /// <param name="colliderMode">碰撞体模式；null 表示使用服务默认模式。 / Collider mode; null uses service default mode.</param>
        /// <returns>成功替换数量。 / Number of replaced blocks.</returns>
        public static int ReplaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null,
            Box3ColliderMode? colliderMode = null)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service)
                ? service.ReplaceBlocksInBounds(blockId, minInclusive, maxInclusive, rotationQuarter, spawnRealtimeLight, colliderMode)
                : 0;
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
        public static bool RotateBlockAt(Transform root, Vector3Int position, Box3QuarterTurn stepQuarter = Box3QuarterTurn.R90)
        {
            return TryGetService(root, out Box3BlocksRuntimeService service) && service.RotateBlockAt(position, stepQuarter);
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
            return TryGetService(root, out Box3BlocksRuntimeService service)
                ? service.RotateBlocksInBounds(minInclusive, maxInclusive, stepQuarter)
                : 0;
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
            blockId = null;
            return TryGetService(root, out Box3BlocksRuntimeService service) && service.TryGetBlockIdAt(position, out blockId);
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
            return TryGetService(root, out Box3BlocksRuntimeService service) && service.ExistsAt(position);
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
            return TryGetService(root, out Box3BlocksRuntimeService service)
                ? service.GetTopY(x, z, fallbackY)
                : fallbackY;
        }

        /// <summary>
        /// 获取当前可放置的全部方块 ID 列表。
        /// <para/>
        /// Get all available block ids.
        /// </summary>
        /// <returns>只读 ID 列表。 / Read-only list of block ids.</returns>
        public static IReadOnlyList<string> GetAvailableBlockIds()
        {
            Box3BlocksRuntimeService service = FindAnyService();
            return service != null ? service.GetAvailableBlockIds() : Array.Empty<string>();
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
            Box3BlocksRuntimeService service = FindAnyService();
            return service != null && service.IsTransparent(blockId);
        }

        /// <summary>
        /// 预热并检查运行时生成资源是否可用。
        /// <para/>
        /// Warm up and verify runtime generated assets are available.
        /// </summary>
        /// <returns>关键资源可用时返回 true。 / Returns true when required assets are available.</returns>
        public static bool PrepareGeneratedAssets()
        {
            Box3BlocksRuntimeService service = FindAnyService();
            if (service == null)
            {
                return false;
            }

            EnsureCatalogInjected(service);
            return service != null && service.Catalog != null && service.Catalog.CubeMesh != null;
        }

        /// <summary>
        /// 设置默认运行时目录资源（可选）。
        /// 如果已设置，后续 API 在 service 缺少 catalog 时会自动注入该资源。
        /// <para/>
        /// Set default runtime catalog (optional).
        /// When set, API calls auto-inject this catalog if service has no catalog.
        /// </summary>
        /// <param name="catalog">默认目录资源。 / Default catalog asset.</param>
        public static void SetDefaultRuntimeCatalog(Box3BlocksRuntimeCatalog catalog)
        {
            _defaultRuntimeCatalog = catalog;
        }

        /// <summary>
        /// 获取默认运行时目录资源（可为空）。
        /// <para/>
        /// Get default runtime catalog (can be null).
        /// </summary>
        /// <returns>默认目录资源。 / Default catalog asset.</returns>
        public static Box3BlocksRuntimeCatalog GetDefaultRuntimeCatalog()
        {
            return _defaultRuntimeCatalog;
        }

        /// <summary>
        /// 设置发光方块默认是否生成点光源。
        /// <para/>
        /// Set default flag for spawning realtime point lights on emissive blocks.
        /// </summary>
        /// <param name="enabled">默认开关。 / Default switch.</param>
        public static void SetSpawnRealtimeLightForEmissive(bool enabled)
        {
            Box3BlocksRuntimeService service = FindAnyService();
            if (service != null)
            {
                service.SetSpawnRealtimeLightForEmissive(enabled);
            }
        }

        /// <summary>
        /// 获取发光方块默认点光源开关状态。
        /// <para/>
        /// Get default point-light spawn flag for emissive blocks.
        /// </summary>
        /// <returns>当前默认状态。 / Current default state.</returns>
        public static bool GetSpawnRealtimeLightForEmissive()
        {
            Box3BlocksRuntimeService service = FindAnyService();
            return service != null && service.GetSpawnRealtimeLightForEmissive();
        }

        /// <summary>
        /// 设置默认碰撞体生成模式（用于未显式传入 colliderMode 的放置/替换 API）。
        /// <para/>
        /// Set default collider generation mode (used when placement/replace APIs do not pass colliderMode).
        /// </summary>
        /// <param name="mode">默认碰撞体模式。 / Default collider mode.</param>
        public static void SetDefaultColliderMode(Box3ColliderMode mode)
        {
            Box3BlocksRuntimeService service = FindAnyService();
            if (service != null)
            {
                service.SetDefaultColliderMode(mode);
            }
        }

        /// <summary>
        /// 获取默认碰撞体生成模式。
        /// <para/>
        /// Get default collider generation mode.
        /// </summary>
        /// <returns>默认碰撞体模式。 / Default collider mode.</returns>
        public static Box3ColliderMode GetDefaultColliderMode()
        {
            Box3BlocksRuntimeService service = FindAnyService();
            return service != null ? service.GetDefaultColliderMode() : Box3ColliderMode.Full;
        }

        private static bool TryGetService(Transform root, out Box3BlocksRuntimeService service)
        {
            service = null;
            if (root == null)
            {
                return false;
            }

            service = root.GetComponent<Box3BlocksRuntimeService>();
            if (service == null)
            {
                service = root.gameObject.AddComponent<Box3BlocksRuntimeService>();
            }

            service.Root = root;
            EnsureCatalogInjected(service);
            return service != null;
        }

        private static Box3BlocksRuntimeService FindAnyService()
        {
            return UnityEngine.Object.FindAnyObjectByType<Box3BlocksRuntimeService>();
        }

        private static void EnsureCatalogInjected(Box3BlocksRuntimeService service)
        {
            if (service == null || service.Catalog != null)
            {
                return;
            }

            Box3BlocksRuntimeCatalog resolved = ResolveRuntimeCatalog();
            if (resolved != null)
            {
                service.Catalog = resolved;
                _missingCatalogWarningLogged = false;
            }
            else if (!_missingCatalogWarningLogged)
            {
                _missingCatalogWarningLogged = true;
                Debug.LogWarning(
                    "[Box3] Runtime catalog not found. " +
                    "Assign Box3BlocksRuntimeCatalog in scene and call Box3Api.SetDefaultRuntimeCatalog(catalog), " +
                    "or ensure a catalog asset is loaded at runtime.");
            }
        }

        private static Box3BlocksRuntimeCatalog ResolveRuntimeCatalog()
        {
            if (_defaultRuntimeCatalog != null)
            {
                return _defaultRuntimeCatalog;
            }

            Box3BlocksRuntimeCatalog[] loaded = Resources.FindObjectsOfTypeAll<Box3BlocksRuntimeCatalog>();
            if (loaded != null && loaded.Length > 0 && loaded[0] != null)
            {
                _defaultRuntimeCatalog = loaded[0];
                return _defaultRuntimeCatalog;
            }

#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets("t:Box3BlocksRuntimeCatalog");
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Box3BlocksRuntimeCatalog asset = AssetDatabase.LoadAssetAtPath<Box3BlocksRuntimeCatalog>(path);
                if (asset != null)
                {
                    _defaultRuntimeCatalog = asset;
                    return _defaultRuntimeCatalog;
                }
            }
#endif

            return null;
        }
    }
}
