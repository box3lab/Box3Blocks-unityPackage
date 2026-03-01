using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks.Editor
{
    public partial class Box3BlocksBuilderWindow
    {
        private static readonly Vector3Int[] NeighborDirections =
        {
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0)
        };

        private static readonly Vector3[] FaceNormals =
        {
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, -1f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f)
        };

        private static readonly Vector3[][] FaceVertices =
        {
            new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            },
            new[]
            {
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            }
        };

        private void RefreshTransparentAround(Vector3Int position)
        {
            UpdateTransparentBlockAt(position);
            for (int i = 0; i < NeighborDirections.Length; i++)
            {
                UpdateTransparentBlockAt(position + NeighborDirections[i]);
            }
        }

        private void UpdateTransparentBlockAt(Vector3Int position)
        {
            GameObject target = FindBlockAt(position);
            if (target == null)
            {
                return;
            }

            UpdateTransparentBlockMesh(target);
        }

        private void UpdateTransparentBlockMesh(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            Box3BlocksPlacedBlock marker = target.GetComponent<Box3BlocksPlacedBlock>();
            if (marker == null)
            {
                return;
            }

            BlockDefinition definition = FindDefinitionById(marker.BlockId);
            if (definition == null || !definition.transparent)
            {
                return;
            }

            if (HasAnimatedFaces(definition))
            {
                return;
            }

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(definition.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData))
            {
                return;
            }

            Vector3Int pos = Vector3Int.RoundToInt(target.transform.position);
            float yRot = NormalizeYRotation(target.transform.eulerAngles.y);
            int rotQuarter = Mathf.RoundToInt(yRot / 90f) & 3;
            Mesh mesh = BuildCulledTransparentMesh(pos, rotQuarter, renderData.faceMainTexSt);
            if (mesh == null)
            {
                return;
            }

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mf.sharedMesh = mesh;
            }

            MeshCollider mc = target.GetComponent<MeshCollider>();
            if (mc != null)
            {
                mc.sharedMesh = mesh;
            }
        }

        private Mesh BuildCulledTransparentMesh(Vector3Int position, int rotationQuarter, Vector4[] faceMainTexSt)
        {
            if (faceMainTexSt == null || faceMainTexSt.Length < SideOrder.Length)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>(24);
            List<Vector2> uvs = new List<Vector2>(24);
            List<int> triangles = new List<int>(36);
            Quaternion rotation = Quaternion.Euler(0f, (rotationQuarter & 3) * 90f, 0f);

            for (int face = 0; face < SideOrder.Length; face++)
            {
                Vector3 dir = rotation * FaceNormals[face];
                Vector3Int neighbor = position + ToVector3Int(dir);
                if (FindBlockAt(neighbor) != null)
                {
                    continue;
                }

                Vector4 st = faceMainTexSt[face];
                int baseIndex = vertices.Count;
                vertices.Add(FaceVertices[face][0]);
                vertices.Add(FaceVertices[face][1]);
                vertices.Add(FaceVertices[face][2]);
                vertices.Add(FaceVertices[face][3]);

                uvs.Add(new Vector2(st.z, st.w));
                uvs.Add(new Vector2(st.z + st.x, st.w));
                uvs.Add(new Vector2(st.z + st.x, st.w + st.y));
                uvs.Add(new Vector2(st.z, st.w + st.y));

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"CulledTransparent_{position.x}_{position.y}_{position.z}"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3Int ToVector3Int(Vector3 dir)
        {
            return new Vector3Int(
                Mathf.RoundToInt(dir.x),
                Mathf.RoundToInt(dir.y),
                Mathf.RoundToInt(dir.z));
        }

        private static float NormalizeYRotation(float y)
        {
            float n = y % 360f;
            if (n < 0f)
            {
                n += 360f;
            }

            return n;
        }
    }
}
