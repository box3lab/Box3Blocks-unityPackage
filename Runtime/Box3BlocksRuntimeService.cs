using UnityEngine;
using System;
using System.Collections.Generic;

namespace Box3Blocks
{
    /// <summary>
    /// 运行时方块服务门面（Facade）。
    /// 对外提供简洁的放置/删除/旋转 API，并将底层实现委托给 <see cref="Box3BlocksRuntimeBackend"/>。
    /// <para/>
    /// Runtime facade for block operations.
    /// Exposes a small public API and delegates backend behavior to <see cref="Box3BlocksRuntimeBackend"/>.
    /// </summary>
    public sealed class Box3BlocksRuntimeService : MonoBehaviour
    {
        [SerializeField]
        private Transform root;

        [SerializeField]
        private Box3BlocksRuntimeCatalog catalog;

        private Box3BlocksRuntimeBackend _backend;
        private bool _spawnRealtimeLightForEmissive = true;

        /// <summary>
        /// 方块根节点，所有运行时生成方块都会挂在该节点下。
        /// 若未设置则回退为当前组件所在对象的 Transform。
        /// <para/>
        /// Root transform that stores spawned blocks.
        /// Falls back to this component's transform when not assigned.
        /// </summary>
        public Transform Root
        {
            get => root != null ? root : transform;
            set => root = value;
        }

        /// <summary>
        /// 运行时目录数据（由编辑器构建）。
        /// 目录必须包含：共享网格、材质、每个 blockId 的 UV/动画描述。
        /// <para/>
        /// Runtime catalog generated from editor pipeline.
        /// Must include shared mesh/material assets and per-block UV/animation mappings.
        /// </summary>
        public Box3BlocksRuntimeCatalog Catalog
        {
            get => catalog;
            set
            {
                catalog = value;
                EnsureBackend();
                _backend.Catalog = catalog;
            }
        }

        /// <summary>
        /// 在指定网格坐标放置方块。
        /// <para/>
        /// Place a block at the given grid position.
        /// </summary>
        /// <param name="blockId">方块 ID / Block id defined in <see cref="Catalog"/>.</param>
        /// <param name="position">网格坐标 / Grid position.</param>
        /// <param name="replaceExisting">已有方块时是否替换 / Whether to replace existing block.</param>
        /// <param name="rotationQuarter">旋转步进（单位 90°）/ Rotation steps in 90-degree increments.</param>
        /// <returns>成功返回 true / Returns true when placement succeeds.</returns>
        /// <remarks>
        /// 该方法内部复用 <see cref="Box3BlocksCore.TryPlaceBlockAt"/>，
        /// 以保证运行时和编辑器侧共用同一套核心规则。
        /// <para/>
        /// Internally reuses <see cref="Box3BlocksCore.TryPlaceBlockAt"/> so runtime/editor share the same core rules.
        /// </remarks>
        public bool TryPlaceBlockAt(
            string blockId,
            Vector3Int position,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLight = null)
        {
            EnsureBackend();
            bool? light = spawnRealtimeLight ?? _spawnRealtimeLightForEmissive;
            return Box3BlocksCore.TryPlaceBlockAt(_backend, Root, blockId, position, replaceExisting, rotationQuarter, light);
        }

        /// <summary>
        /// 在指定网格坐标放置方块（使用枚举旋转参数）。
        /// <para/>
        /// Place a block at the given grid position (enum rotation overload).
        /// </summary>
        /// <param name="blockId">方块 ID / Block id defined in <see cref="Catalog"/>.</param>
        /// <param name="position">网格坐标 / Grid position.</param>
        /// <param name="replaceExisting">已有方块时是否替换 / Whether to replace existing block.</param>
        /// <param name="rotationQuarter">
        /// 旋转步进枚举 / Rotation enum:
        /// <c>R0 = 0°</c>, <c>R90 = 90°</c>, <c>R180 = 180°</c>, <c>R270 = 270°</c>.
        /// </param>
        /// <returns>成功返回 true / Returns true when placement succeeds.</returns>
        public bool TryPlaceBlockAt(string blockId, Vector3Int position, bool replaceExisting, Box3QuarterTurn rotationQuarter)
        {
            return TryPlaceBlockAt(blockId, position, replaceExisting, (int)rotationQuarter);
        }

        /// <summary>
        /// 删除指定网格坐标上的方块。
        /// <para/>
        /// Erase a block at the given grid position.
        /// </summary>
        /// <param name="position">目标网格坐标 / Target grid position.</param>
        /// <returns>删除成功返回 true / Returns true when erase succeeds.</returns>
        /// <remarks>
        /// 如果目标位置不存在方块，返回 false。
        /// <para/>
        /// Returns false when there is no block at the target position.
        /// </remarks>
        public bool EraseBlockAt(Vector3Int position)
        {
            EnsureBackend();
            return Box3BlocksCore.EraseBlockAt(_backend, Root, position);
        }

        /// <summary>
        /// 旋转指定网格坐标上的方块。
        /// <para/>
        /// Rotate a block at the given grid position.
        /// </summary>
        /// <param name="position">目标网格坐标 / Target grid position.</param>
        /// <param name="stepQuarter">旋转步进（单位 90°）/ Rotation steps in 90-degree increments.</param>
        /// <returns>旋转成功返回 true / Returns true when rotation succeeds.</returns>
        public bool RotateBlockAt(Vector3Int position, int stepQuarter = 1)
        {
            EnsureBackend();
            return Box3BlocksCore.RotateBlockAt(_backend, Root, position, stepQuarter);
        }

        /// <summary>
        /// 在指定列 (x,z) 顶部放置方块。
        /// <para/>
        /// Place block on top of column (x,z).
        /// </summary>
        public bool TryPlaceBlockOnTop(
            string blockId,
            int x,
            int z,
            int baseY = 0,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLight = null)
        {
            EnsureBackend();
            bool? light = spawnRealtimeLight ?? _spawnRealtimeLightForEmissive;
            return Box3BlocksCore.TryPlaceBlockOnTop(_backend, Root, blockId, x, z, baseY, replaceExisting, rotationQuarter, light);
        }

        /// <summary>
        /// 批量放置方块。
        /// <para/>
        /// Batch place blocks in inclusive bounds.
        /// </summary>
        public int PlaceBlocksInBounds(
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting = true,
            int rotationQuarter = 0,
            bool? spawnRealtimeLight = null)
        {
            EnsureBackend();
            bool? light = spawnRealtimeLight ?? _spawnRealtimeLightForEmissive;
            return Box3BlocksCore.PlaceBlocksInBounds(_backend, Root, blockId, minInclusive, maxInclusive, replaceExisting, rotationQuarter, light);
        }

        /// <summary>
        /// 批量删除方块。
        /// <para/>
        /// Batch erase blocks in inclusive bounds.
        /// </summary>
        public int EraseBlocksInBounds(Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            EnsureBackend();
            return Box3BlocksCore.EraseBlocksInBounds(_backend, Root, minInclusive, maxInclusive);
        }

        /// <summary>
        /// 替换单个方块。
        /// <para/>
        /// Replace one block.
        /// </summary>
        public bool ReplaceBlockAt(
            string blockId,
            Vector3Int position,
            int rotationQuarter = 0,
            bool? spawnRealtimeLight = null)
        {
            EnsureBackend();
            bool? light = spawnRealtimeLight ?? _spawnRealtimeLightForEmissive;
            return Box3BlocksCore.ReplaceBlockAt(_backend, Root, blockId, position, rotationQuarter, light);
        }

        /// <summary>
        /// 批量替换方块。
        /// <para/>
        /// Batch replace blocks in inclusive bounds.
        /// </summary>
        public int ReplaceBlocksInBounds(
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            int rotationQuarter = 0,
            bool? spawnRealtimeLight = null)
        {
            EnsureBackend();
            bool? light = spawnRealtimeLight ?? _spawnRealtimeLightForEmissive;
            return Box3BlocksCore.ReplaceBlocksInBounds(_backend, Root, blockId, minInclusive, maxInclusive, rotationQuarter, light);
        }

        /// <summary>
        /// 批量旋转方块。
        /// <para/>
        /// Batch rotate blocks in inclusive bounds.
        /// </summary>
        public int RotateBlocksInBounds(Vector3Int minInclusive, Vector3Int maxInclusive, int stepQuarter = 1)
        {
            EnsureBackend();
            return Box3BlocksCore.RotateBlocksInBounds(_backend, Root, minInclusive, maxInclusive, stepQuarter);
        }

        /// <summary>
        /// 查询坐标上的方块 ID。
        /// <para/>
        /// Query block id at position.
        /// </summary>
        public bool TryGetBlockIdAt(Vector3Int position, out string blockId)
        {
            EnsureBackend();
            return Box3BlocksCore.TryGetBlockIdAt(_backend, Root, position, out blockId);
        }

        /// <summary>
        /// 判断坐标是否存在方块。
        /// <para/>
        /// Check whether a block exists at position.
        /// </summary>
        public bool ExistsAt(Vector3Int position)
        {
            EnsureBackend();
            return Box3BlocksCore.ExistsAt(_backend, Root, position);
        }

        /// <summary>
        /// 获取列 (x,z) 的顶部 Y。
        /// <para/>
        /// Get top Y in column (x,z).
        /// </summary>
        public int GetTopY(int x, int z, int fallbackY = 0)
        {
            EnsureBackend();
            return Box3BlocksCore.GetTopY(_backend, Root, x, z, fallbackY);
        }

        /// <summary>
        /// 获取目录内所有可用方块 ID。
        /// <para/>
        /// Get all available block ids from catalog.
        /// </summary>
        public IReadOnlyList<string> GetAvailableBlockIds()
        {
            if (catalog == null || catalog.Entries == null)
            {
                return Array.Empty<string>();
            }

            List<string> ids = new List<string>(catalog.Entries.Count);
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                Box3BlocksRuntimeCatalog.Entry e = catalog.Entries[i];
                if (e != null && !string.IsNullOrWhiteSpace(e.blockId))
                {
                    ids.Add(e.blockId);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        /// <summary>
        /// 判断方块 ID 是否为透明方块。
        /// <para/>
        /// Check whether block id is transparent.
        /// </summary>
        public bool IsTransparent(string blockId)
        {
            return catalog != null
                && !string.IsNullOrWhiteSpace(blockId)
                && catalog.TryGetEntry(blockId, out Box3BlocksRuntimeCatalog.Entry e)
                && e != null
                && e.transparent;
        }

        /// <summary>
        /// 设置发光方块默认是否生成实时点光源。
        /// <para/>
        /// Set default realtime-light spawn flag for emissive blocks.
        /// </summary>
        public void SetSpawnRealtimeLightForEmissive(bool enabled)
        {
            _spawnRealtimeLightForEmissive = enabled;
        }

        /// <summary>
        /// 获取发光方块默认实时点光源开关状态。
        /// <para/>
        /// Get default realtime-light spawn flag for emissive blocks.
        /// </summary>
        public bool GetSpawnRealtimeLightForEmissive()
        {
            return _spawnRealtimeLightForEmissive;
        }

        /// <summary>
        /// 放置方块（枚举旋转参数重载）。
        /// <para/>
        /// Enum-rotation overload for placement.
        /// </summary>
        public bool TryPlaceBlockAt(
            string blockId,
            Vector3Int position,
            bool replaceExisting,
            Box3QuarterTurn rotationQuarter,
            bool? spawnRealtimeLight = null)
        {
            return TryPlaceBlockAt(blockId, position, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 在列顶部放置方块（枚举旋转参数重载）。
        /// <para/>
        /// Enum-rotation overload for top placement.
        /// </summary>
        public bool TryPlaceBlockOnTop(
            string blockId,
            int x,
            int z,
            int baseY,
            bool replaceExisting,
            Box3QuarterTurn rotationQuarter,
            bool? spawnRealtimeLight = null)
        {
            return TryPlaceBlockOnTop(blockId, x, z, baseY, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 批量放置（枚举旋转参数重载）。
        /// <para/>
        /// Enum-rotation overload for batch placement.
        /// </summary>
        public int PlaceBlocksInBounds(
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting,
            Box3QuarterTurn rotationQuarter,
            bool? spawnRealtimeLight = null)
        {
            return PlaceBlocksInBounds(blockId, minInclusive, maxInclusive, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 替换方块（枚举旋转参数重载）。
        /// <para/>
        /// Enum-rotation overload for replace.
        /// </summary>
        public bool ReplaceBlockAt(
            string blockId,
            Vector3Int position,
            Box3QuarterTurn rotationQuarter,
            bool? spawnRealtimeLight = null)
        {
            return ReplaceBlockAt(blockId, position, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 批量替换（枚举旋转参数重载）。
        /// <para/>
        /// Enum-rotation overload for batch replace.
        /// </summary>
        public int ReplaceBlocksInBounds(
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            Box3QuarterTurn rotationQuarter,
            bool? spawnRealtimeLight = null)
        {
            return ReplaceBlocksInBounds(blockId, minInclusive, maxInclusive, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 旋转方块（枚举参数重载）。
        /// <para/>
        /// Enum-step overload for rotate.
        /// </summary>
        public bool RotateBlockAt(Vector3Int position, Box3QuarterTurn stepQuarter = Box3QuarterTurn.R90)
        {
            return RotateBlockAt(position, (int)stepQuarter);
        }

        /// <summary>
        /// 批量旋转（枚举参数重载）。
        /// <para/>
        /// Enum-step overload for batch rotate.
        /// </summary>
        public int RotateBlocksInBounds(Vector3Int minInclusive, Vector3Int maxInclusive, Box3QuarterTurn stepQuarter)
        {
            return RotateBlocksInBounds(minInclusive, maxInclusive, (int)stepQuarter);
        }

        private void Awake()
        {
            EnsureBackend();
        }

        private void OnValidate()
        {
            EnsureBackend();
            _backend.Catalog = catalog;
        }

        /// <summary>
        /// 确保后端实例存在，并同步目录引用。
        /// <para/>
        /// Ensure backend instance exists and sync catalog reference.
        /// </summary>
        private void EnsureBackend()
        {
            if (_backend == null)
            {
                _backend = new Box3BlocksRuntimeBackend();
            }

            _backend.Catalog = catalog;
        }
    }
}
