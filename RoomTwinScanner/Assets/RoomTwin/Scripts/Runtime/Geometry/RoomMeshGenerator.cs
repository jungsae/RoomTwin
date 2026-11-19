using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Builds a floor and four walls from corners already ordered counter-clockwise
    /// when viewed from above. Floor normals point up. Wall normals point into the room.
    /// </summary>
    public static class RoomMeshGenerator
    {
        public static Mesh BuildFloor(IReadOnlyList<Vector2> orderedCorners)
        {
            RequireCorners(orderedCorners);

            var mesh = new Mesh { name = "Floor" };
            var vertices = new Vector3[orderedCorners.Count];
            var normals = new Vector3[orderedCorners.Count];
            var uvs = new Vector2[orderedCorners.Count];
            for (int i = 0; i < orderedCorners.Count; i++)
            {
                vertices[i] = new Vector3(orderedCorners[i].x, 0f, orderedCorners[i].y);
                normals[i] = Vector3.up;
                uvs[i] = orderedCorners[i];
            }

            // Counter-clockwise on the XZ plane winds downward under Vector3.Cross, so the
            // triangle order is flipped to keep the visible face and the +Y normal together.
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh[] BuildWalls(IReadOnlyList<Vector2> orderedCorners, float heightMeters)
        {
            RequireCorners(orderedCorners);
            if (!float.IsFinite(heightMeters) || heightMeters <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(heightMeters), "Wall height must be greater than 0 m.");
            }

            var walls = new Mesh[orderedCorners.Count];
            for (int i = 0; i < orderedCorners.Count; i++)
            {
                walls[i] = BuildWall(orderedCorners[i], orderedCorners[(i + 1) % orderedCorners.Count], heightMeters, i);
            }

            return walls;
        }

        static Mesh BuildWall(Vector2 start, Vector2 end, float heightMeters, int index)
        {
            Vector3 bottomStart = new Vector3(start.x, 0f, start.y);
            Vector3 bottomEnd = new Vector3(end.x, 0f, end.y);
            Vector3 topEnd = bottomEnd + Vector3.up * heightMeters;
            Vector3 topStart = bottomStart + Vector3.up * heightMeters;
            Vector3 inward = Vector3.Cross(bottomEnd - bottomStart, Vector3.up).normalized;

            var mesh = new Mesh { name = $"Wall_{index}" };
            mesh.SetVertices(new[] { bottomStart, bottomEnd, topEnd, topStart });
            mesh.SetNormals(new[] { inward, inward, inward, inward });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void RequireCorners(IReadOnlyList<Vector2> orderedCorners)
        {
            if (orderedCorners == null || orderedCorners.Count != RoomGeometryValidator.RequiredCornerCount)
            {
                throw new ArgumentException("Wall and floor meshes require 4 counter-clockwise floor corners.");
            }
        }
    }
}
