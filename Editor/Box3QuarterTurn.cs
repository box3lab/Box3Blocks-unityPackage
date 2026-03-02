namespace Box3Blocks.Editor
{
    /// <summary>
    /// 表示 90 度步进旋转（绕 Y 轴）。
    /// <para/>
    /// Represents quarter-turn rotation steps (around Y axis).
    /// </summary>
    public enum Box3QuarterTurn
    {
        /// <summary>不旋转（0°）。 / No rotation (0°).</summary>
        R0 = 0,

        /// <summary>顺时针旋转 90°。 / Rotate clockwise 90°.</summary>
        R90 = 1,

        /// <summary>旋转 180°。 / Rotate 180°.</summary>
        R180 = 2,

        /// <summary>顺时针旋转 270°（等价于逆时针 90°）。 / Rotate clockwise 270° (same as counter-clockwise 90°).</summary>
        R270 = 3,
    }
}
