namespace Box3Blocks
{
    /// <summary>
    /// 方块碰撞体生成模式。
    /// <para/>
    /// Block collider generation mode.
    /// </summary>
    public enum Box3ColliderMode
    {
        /// <summary>
        /// 不生成碰撞体。
        /// <para/>
        /// No collider.
        /// </summary>
        None = 0,

        /// <summary>
        /// 仅生成顶面碰撞体（薄 BoxCollider）。
        /// <para/>
        /// Top surface only (thin BoxCollider).
        /// </summary>
        TopOnly = 1,

        /// <summary>
        /// 生成完整体积碰撞体（MeshCollider）。
        /// <para/>
        /// Full volume collider (MeshCollider).
        /// </summary>
        Full = 2,
    }
}
