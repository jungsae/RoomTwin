using System.Collections.Generic;
using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Maps AR world positions into one horizontal room frame.
    /// The first corner is the XZ origin. Y is flattened to one floor height.
    /// These coordinates are room-local meters, not a live camera pose.
    /// </summary>
    public static class RoomLocalFrame
    {
        public static bool TryMap(
            IReadOnlyList<Vector3> worldCorners,
            float wallHeightMeters,
            out RoomGeometryData data,
            out Vector3 anchorWorld,
            out float floorHeightMeters,
            out float[] horizontalEdgeMeters,
            out string error)
        {
            data = null;
            anchorWorld = default;
            floorHeightMeters = 0f;
            horizontalEdgeMeters = null;
            error = null;

            if (worldCorners == null || worldCorners.Count != RoomGeometryValidator.RequiredCornerCount)
            {
                error = "Four floor corners are required.";
                return false;
            }

            float heightSum = 0f;
            for (int i = 0; i < worldCorners.Count; i++)
            {
                Vector3 corner = worldCorners[i];
                if (!float.IsFinite(corner.x) || !float.IsFinite(corner.y) || !float.IsFinite(corner.z))
                {
                    error = $"Corner {i + 1} has a non-finite world position.";
                    return false;
                }

                heightSum += corner.y;
            }

            floorHeightMeters = heightSum / worldCorners.Count;
            Vector3 first = worldCorners[0];
            anchorWorld = new Vector3(first.x, floorHeightMeters, first.z);

            var localCorners = new Vector2[worldCorners.Count];
            horizontalEdgeMeters = new float[worldCorners.Count];
            for (int i = 0; i < worldCorners.Count; i++)
            {
                localCorners[i] = new Vector2(
                    worldCorners[i].x - first.x,
                    worldCorners[i].z - first.z);
            }

            for (int i = 0; i < localCorners.Length; i++)
            {
                Vector2 next = localCorners[(i + 1) % localCorners.Length];
                horizontalEdgeMeters[i] = Vector2.Distance(localCorners[i], next);
            }

            data = new RoomGeometryData
            {
                floorCornersXz = localCorners,
                wallHeightMeters = wallHeightMeters
            };

            if (!RoomGeometryValidator.TryValidate(data, out Vector2[] ordered, out error))
            {
                return false;
            }

            data.floorCornersXz = ordered;
            return true;
        }
    }

    /// <summary>
    /// Tap-order floor corners before they are turned into a room mesh.
    /// </summary>
    public sealed class FloorCornerCapture
    {
        readonly List<Vector3> corners = new List<Vector3>(RoomGeometryValidator.RequiredCornerCount);

        public int Count => corners.Count;
        public bool IsComplete => corners.Count == RoomGeometryValidator.RequiredCornerCount;
        public IReadOnlyList<Vector3> WorldCorners => corners;

        public bool TryAdd(Vector3 worldCorner, out string rejection)
        {
            rejection = null;
            if (!float.IsFinite(worldCorner.x) || !float.IsFinite(worldCorner.y) || !float.IsFinite(worldCorner.z))
            {
                rejection = "Corner position is not finite.";
                return false;
            }

            if (IsComplete)
            {
                rejection = "Four corners are already selected. Reset before measuring again.";
                return false;
            }

            var candidate = new Vector2(worldCorner.x, worldCorner.z);
            for (int i = 0; i < corners.Count; i++)
            {
                var existing = new Vector2(corners[i].x, corners[i].z);
                if (Vector2.Distance(existing, candidate) < RoomGeometryValidator.MinimumEdgeMeters)
                {
                    rejection =
                        $"Corner {corners.Count + 1} is closer than {RoomGeometryValidator.MinimumEdgeMeters:0.00} m to corner {i + 1}.";
                    return false;
                }
            }

            corners.Add(worldCorner);
            return true;
        }

        public void Reset()
        {
            corners.Clear();
        }
    }
}
