using System;
using System.Collections.Generic;
using UnityEngine;
using BlockWorldMVP;

namespace BlockWorldMVP.Editor
{
    public sealed partial class VoxelGzImportWindow
    {
        [Serializable]
        private sealed class VoxelPayload
        {
            public string formatVersion;
            public int[] shape;
            public int[] dir;
            public int[] indices;
            public int[] data;
            public int[] rot;
        }

        private readonly struct ChunkKey : IEquatable<ChunkKey>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;

            public ChunkKey(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(ChunkKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = (h * 31) + x;
                    h = (h * 31) + y;
                    h = (h * 31) + z;
                    return h;
                }
            }
        }

        private sealed class BlockDefinition
        {
            public readonly Dictionary<string, string> sideTexturePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PreparedBlock
        {
            public bool valid;
            public Mesh mesh;
            public Material material;
            public Material[] materials;
            public Vector4[] faceMainTexSt;
            public bool hasAnimation;
            public BlockTextureAnimator.FaceAnimation[] animations;
            public bool usesSubmeshes;
        }

        private sealed class ChunkBucket
        {
            public readonly List<CombineInstance> opaqueCombines = new List<CombineInstance>(2048);
            public readonly List<TransparentVoxel> transparentVoxels = new List<TransparentVoxel>(512);
            public readonly List<EmissiveLightVoxel> emissiveVoxels = new List<EmissiveLightVoxel>(64);
        }

        private sealed class ImportStats
        {
            public int total;
            public int valid;
            public int createdChunks;
            public int createdBlocks;
            public int createdSurfaceColliders;
            public int createdMeshColliders;
            public int skippedAir;
            public int skippedWater;
            public int skippedBarrier;
            public int skippedUnknown;
            public int skippedInvalid;
            public double startTime;
        }

        private readonly struct TransparentVoxel
        {
            public readonly Vector3Int pos;
            public readonly int rot;
            public readonly PreparedBlock prepared;

            public TransparentVoxel(Vector3Int pos, int rot, PreparedBlock prepared)
            {
                this.pos = pos;
                this.rot = rot;
                this.prepared = prepared;
            }
        }

        private readonly struct PendingBlock
        {
            public readonly Vector3Int pos;
            public readonly int rot;
            public readonly PreparedBlock prepared;
            public readonly string blockName;

            public PendingBlock(Vector3Int pos, int rot, PreparedBlock prepared, string blockName)
            {
                this.pos = pos;
                this.rot = rot;
                this.prepared = prepared;
                this.blockName = blockName;
            }
        }

        private readonly struct EmissiveLightVoxel
        {
            public readonly Vector3Int pos;
            public readonly Color color;

            public EmissiveLightVoxel(Vector3Int pos, Color color)
            {
                this.pos = pos;
                this.color = color;
            }
        }

        private enum SourceType
        {
            LocalFile,
            Url
        }

        private enum Phase
        {
            Idle,
            ProcessVoxels,
            PlaceSingleBlocks,
            BuildChunks,
            Done
        }

        private enum ImportMode
        {
            Chunk = 0,
            SingleBlock = 1
        }

        private sealed class FaceAnimationSpec
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }
    }
}
