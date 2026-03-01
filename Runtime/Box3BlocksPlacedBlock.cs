using UnityEngine;

namespace Box3Blocks
{
    public class Box3BlocksPlacedBlock : MonoBehaviour
    {
        [SerializeField]
        private string blockId;

        [SerializeField]
        private bool hasAnimation;

        public string BlockId
        {
            get => blockId;
            set => blockId = value;
        }

        public bool HasAnimation
        {
            get => hasAnimation;
            set => hasAnimation = value;
        }
    }
}
