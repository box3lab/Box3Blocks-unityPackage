using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks.Editor
{
    /// <summary>
    /// 表示 90 度步进旋转（绕 Y 轴）。
    /// </summary>
    public enum Box3QuarterTurn
    {
        /// <summary>不旋转（0°）。</summary>
        R0 = 0,

        /// <summary>顺时针旋转 90°。</summary>
        R90 = 1,

        /// <summary>旋转 180°。</summary>
        R180 = 2,

        /// <summary>顺时针旋转 270°（等价于逆时针 90°）。</summary>
        R270 = 3,
    }

    /// <summary>
    /// Box3方块对外API。
    /// </summary>
    public static class Box3Api
    {
        /// <summary>
        /// 在指定格子坐标尝试放置一个方块。
        /// </summary>
        /// <param name="root">方块根节点（所有生成方块会挂在该节点下）。不能为空。</param>
        /// <param name="blockId">目标方块 ID（建议来自 <c>GetAvailableBlockIds()</c>）。</param>
        /// <param name="position">目标世界格子坐标（整数网格坐标）。</param>
        /// <param name="replaceExisting">若目标位置已有方块，是否允许直接替换。</param>
        /// <param name="rotationQuarter">新放置方块的旋转（90 度步进）。</param>
        /// <param name="spawnRealtimeLight">
        /// 是否为发光方块生成点光源。传 <c>null</c> 表示使用当前工具全局设置；
        /// 传 <c>true</c>/<c>false</c> 表示强制本次放置行为。
        /// </param>
        /// <returns>放置成功返回 <c>true</c>；失败（如参数无效、blockId 不存在）返回 <c>false</c>。</returns>
        public static bool TryPlaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null)
        {
            return Box3BlocksBuilderWindow.TryPlaceBlockAtApi(root, blockId, position, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 在指定列 (x,z) 的顶部放置一个方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="blockId">目标方块 ID。</param>
        /// <param name="x">目标列的 X 坐标。</param>
        /// <param name="z">目标列的 Z 坐标。</param>
        /// <param name="baseY">当该列没有方块时使用的起始 Y。</param>
        /// <param name="replaceExisting">若最终落点已有方块，是否允许替换。</param>
        /// <param name="rotationQuarter">新放置方块的旋转（90 度步进）。</param>
        /// <param name="spawnRealtimeLight">
        /// 是否为发光方块生成点光源。传 <c>null</c> 表示使用当前工具全局设置；
        /// 传 <c>true</c>/<c>false</c> 表示强制本次放置行为。
        /// </param>
        /// <returns>放置成功返回 <c>true</c>。</returns>
        public static bool TryPlaceBlockOnTop(
            Transform root,
            string blockId,
            int x,
            int z,
            int baseY = 0,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null)
        {
            return Box3BlocksBuilderWindow.TryPlaceBlockOnTopApi(root, blockId, x, z, baseY, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 在包围盒范围内批量放置方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="blockId">目标方块 ID。</param>
        /// <param name="minInclusive">最小角点（包含）。</param>
        /// <param name="maxInclusive">最大角点（包含）。</param>
        /// <param name="replaceExisting">范围内已有方块时，是否允许替换。</param>
        /// <param name="rotationQuarter">批量放置时统一使用的旋转（90 度步进）。</param>
        /// <param name="spawnRealtimeLight">
        /// 是否为发光方块生成点光源。传 <c>null</c> 表示使用当前工具全局设置；
        /// 传 <c>true</c>/<c>false</c> 表示强制本次放置行为。
        /// </param>
        /// <returns>成功放置的方块数量。</returns>
        public static int PlaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            bool replaceExisting = true,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null)
        {
            return Box3BlocksBuilderWindow.PlaceBlocksInBoundsApi(root, blockId, minInclusive, maxInclusive, replaceExisting, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 删除指定坐标上的方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="position">目标格子坐标。</param>
        /// <returns>删除成功返回 <c>true</c>；该位置没有方块返回 <c>false</c>。</returns>
        public static bool EraseBlockAt(Transform root, Vector3Int position)
        {
            return Box3BlocksBuilderWindow.EraseBlockAtApi(root, position);
        }

        /// <summary>
        /// 删除包围盒范围内的方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="minInclusive">最小角点（包含）。</param>
        /// <param name="maxInclusive">最大角点（包含）。</param>
        /// <returns>成功删除的方块数量。</returns>
        public static int EraseBlocksInBounds(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            return Box3BlocksBuilderWindow.EraseBlocksInBoundsApi(root, minInclusive, maxInclusive);
        }

        /// <summary>
        /// 将指定坐标上的方块替换为目标方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="blockId">替换后的方块 ID。</param>
        /// <param name="position">目标格子坐标。</param>
        /// <param name="rotationQuarter">替换后方块旋转（90 度步进）。</param>
        /// <param name="spawnRealtimeLight">
        /// 是否为发光方块生成点光源。传 <c>null</c> 表示使用当前工具全局设置；
        /// 传 <c>true</c>/<c>false</c> 表示强制本次替换行为。
        /// </param>
        /// <returns>替换成功返回 <c>true</c>；目标位置无方块返回 <c>false</c>。</returns>
        public static bool ReplaceBlockAt(
            Transform root,
            string blockId,
            Vector3Int position,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null)
        {
            return Box3BlocksBuilderWindow.ReplaceBlockAtApi(root, blockId, position, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 在包围盒范围内替换方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="blockId">替换后的方块 ID。</param>
        /// <param name="minInclusive">最小角点（包含）。</param>
        /// <param name="maxInclusive">最大角点（包含）。</param>
        /// <param name="rotationQuarter">替换后方块旋转（90 度步进）。</param>
        /// <param name="spawnRealtimeLight">
        /// 是否为发光方块生成点光源。传 <c>null</c> 表示使用当前工具全局设置；
        /// 传 <c>true</c>/<c>false</c> 表示强制本次替换行为。
        /// </param>
        /// <returns>成功替换的方块数量。</returns>
        public static int ReplaceBlocksInBounds(
            Transform root,
            string blockId,
            Vector3Int minInclusive,
            Vector3Int maxInclusive,
            Box3QuarterTurn rotationQuarter = Box3QuarterTurn.R0,
            bool? spawnRealtimeLight = null)
        {
            return Box3BlocksBuilderWindow.ReplaceBlocksInBoundsApi(root, blockId, minInclusive, maxInclusive, (int)rotationQuarter, spawnRealtimeLight);
        }

        /// <summary>
        /// 旋转指定坐标上的方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="position">目标格子坐标。</param>
        /// <param name="stepQuarter">旋转步进（90 度单位）。默认顺时针 90°。</param>
        /// <returns>旋转成功返回 <c>true</c>；目标位置无方块返回 <c>false</c>。</returns>
        public static bool RotateBlockAt(
            Transform root,
            Vector3Int position,
            Box3QuarterTurn stepQuarter = Box3QuarterTurn.R90)
        {
            return Box3BlocksBuilderWindow.RotateBlockAtApi(root, position, (int)stepQuarter);
        }

        /// <summary>
        /// 旋转包围盒范围内的方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="minInclusive">最小角点（包含）。</param>
        /// <param name="maxInclusive">最大角点（包含）。</param>
        /// <param name="stepQuarter">旋转步进（90 度单位）。</param>
        /// <returns>成功旋转的方块数量。</returns>
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
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="position">目标格子坐标。</param>
        /// <param name="blockId">输出命中的方块 ID；未命中时返回空字符串。</param>
        /// <returns>命中方块返回 <c>true</c>。</returns>
        public static bool TryGetBlockIdAt(Transform root, Vector3Int position, out string blockId)
        {
            return Box3BlocksBuilderWindow.TryGetBlockIdAtApi(root, position, out blockId);
        }

        /// <summary>
        /// 判断指定坐标是否存在方块。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="position">目标格子坐标。</param>
        /// <returns>存在返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool ExistsAt(Transform root, Vector3Int position)
        {
            return Box3BlocksBuilderWindow.ExistsAtApi(root, position);
        }

        /// <summary>
        /// 获取指定列 (x,z) 的顶部 Y。
        /// </summary>
        /// <param name="root">方块根节点。不能为空。</param>
        /// <param name="x">列 X 坐标。</param>
        /// <param name="z">列 Z 坐标。</param>
        /// <param name="fallbackY">当该列没有方块时返回此默认值。</param>
        /// <returns>列顶部 Y；若为空列则返回 <paramref name="fallbackY"/>。</returns>
        public static int GetTopY(Transform root, int x, int z, int fallbackY = 0)
        {
            return Box3BlocksBuilderWindow.GetTopYApi(root, x, z, fallbackY);
        }

        /// <summary>
        /// 获取当前可放置的全部方块 ID 列表。
        /// </summary>
        /// <returns>只读方块 ID 列表。</returns>
        public static IReadOnlyList<string> GetAvailableBlockIds()
        {
            return Box3BlocksBuilderWindow.GetAvailableBlockIdsApi();
        }

        /// <summary>
        /// 判断某个方块 ID 是否属于透明方块。
        /// </summary>
        /// <param name="blockId">要查询的方块 ID。</param>
        /// <returns>透明返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool IsTransparent(string blockId)
        {
            return Box3BlocksBuilderWindow.IsTransparentApi(blockId);
        }

        /// <summary>
        /// 主动预热并生成 Box3 资源（<c>Assets/Box3</c> 下的网格、图集、材质）。
        /// </summary>
        /// <returns>
        /// 当网格、主材质、Bump 图集、Metallic 图集、Emission 图集均可用时返回 <c>true</c>。
        /// </returns>
        public static bool PrepareGeneratedAssets()
        {
            Box3BlocksBuilderWindow.ReloadLibraryApi();
            Box3BlocksAssetFactory.InvalidateCaches();
            Mesh mesh = Box3BlocksAssetFactory.GetOrCreateCubeMesh();
            Material atlasMaterial = Box3BlocksAssetFactory.GetAtlasMaterial();
            Texture2D bump = Box3BlocksAssetFactory.GetAtlasBumpTexture();
            Texture2D metallic = Box3BlocksAssetFactory.GetAtlasMaterialTexture();
            Texture2D emission = Box3BlocksAssetFactory.GetAtlasEmissionTexture();

            return mesh != null && atlasMaterial != null && bump != null && metallic != null && emission != null;
        }

        /// <summary>
        /// 设置“发光方块默认是否生成点光源”的全局开关。
        /// </summary>
        /// <param name="enabled">
        /// <c>true</c> 表示默认生成；<c>false</c> 表示默认不生成。
        /// 注意：若调用放置 API 时显式传了 <c>spawnRealtimeLight</c>，则单次参数优先生效。
        /// </param>
        public static void SetSpawnRealtimeLightForEmissive(bool enabled)
        {
            Box3BlocksBuilderWindow.SetSpawnRealtimeLightForEmissiveApi(enabled);
        }

        /// <summary>
        /// 获取“发光方块默认是否生成点光源”的全局开关状态。
        /// </summary>
        /// <returns>当前默认状态。</returns>
        public static bool GetSpawnRealtimeLightForEmissive()
        {
            return Box3BlocksBuilderWindow.GetSpawnRealtimeLightForEmissiveApi();
        }
    }
}
