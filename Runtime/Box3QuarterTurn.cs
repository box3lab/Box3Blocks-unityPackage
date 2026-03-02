namespace Box3Blocks
{
    /// <summary>
    /// 方块旋转步进（绕 Y 轴，90° 为一个单位）。
    /// <para/>
    /// Quarter-turn rotation steps around Y axis (90 degrees per step).
    /// </summary>
    public enum Box3QuarterTurn
    {
        /// <summary>
        /// 不旋转（0°）。
        /// <para/>
        /// No rotation (0°).
        /// </summary>
        R0 = 0,

        /// <summary>
        /// 顺时针 90°。
        /// <para/>
        /// Clockwise 90°.
        /// </summary>
        R90 = 1,

        /// <summary>
        /// 180°。
        /// <para/>
        /// 180°.
        /// </summary>
        R180 = 2,

        /// <summary>
        /// 顺时针 270°（等价于逆时针 90°）。
        /// <para/>
        /// Clockwise 270° (equivalent to counter-clockwise 90°).
        /// </summary>
        R270 = 3,
    }
}
