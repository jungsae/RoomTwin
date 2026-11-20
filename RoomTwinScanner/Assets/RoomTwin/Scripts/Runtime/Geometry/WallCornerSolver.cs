using System.Collections.Generic;
using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// One detected wall. The normal is world-space. Boundary points are in the wall's
    /// local XZ plane, relative to the plane transform, not world positions.
    /// </summary>
    public struct DetectedWall
    {
        public string Id;
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Center;
        public Vector3 AxisU;
        public Vector3 AxisV;
        public Vector2 HalfExtents;
        public Vector2[] Boundary;
    }

    public struct WallCornerEstimate
    {
        public bool Accepted;
        public string Rejection;
        public string SelectedWallId;
        public string OtherWallId;
        public Vector3 Corner;
        public Vector3 IntersectionDirection;
        public float FloorY;
        public float PlaneResidualMeters;
        public float BoundaryOverflowMeters;
        public float NormalCrossMagnitude;
    }

    /// <summary>
    /// Estimates one floor corner from two vertical walls and one floor height.
    /// The result is an estimate, not a surveyed measurement.
    /// </summary>
    public static class WallCornerSolver
    {
        public const float MinNormalCrossMagnitude = 0.26f;
        public const float MaxCornerDistanceMeters = 6f;
        public const float BoundaryMarginMeters = 0.5f;
        public const float MinimumDeterminant = 0.0001f;

        public static bool TryChooseFloorY(
            IReadOnlyList<float> horizontalPlaneYs,
            float toleranceMeters,
            out float floorY,
            out string error)
        {
            floorY = 0f;
            error = null;
            if (horizontalPlaneYs == null || horizontalPlaneYs.Count == 0)
            {
                error = "Scan the floor. No HorizontalUp plane is available, so the floor height is not locked.";
                return false;
            }

            float min = horizontalPlaneYs[0];
            float max = horizontalPlaneYs[0];
            float sum = 0f;
            for (int i = 0; i < horizontalPlaneYs.Count; i++)
            {
                float y = horizontalPlaneYs[i];
                if (!float.IsFinite(y))
                {
                    error = "A horizontal plane has a non-finite height.";
                    return false;
                }

                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
                sum += y;
            }

            if (max - min > toleranceMeters)
            {
                error =
                    $"Floor height is uncertain. HorizontalUp planes span {(max - min):0.000} m, " +
                    $"which is wider than {toleranceMeters:0.00} m. Scan the room floor before locking a corner.";
                return false;
            }

            floorY = sum / horizontalPlaneYs.Count;
            return true;
        }

        public static bool TryEstimate(
            DetectedWall selected,
            IReadOnlyList<DetectedWall> otherWalls,
            float floorY,
            out WallCornerEstimate estimate)
        {
            estimate = new WallCornerEstimate
            {
                Accepted = false,
                SelectedWallId = selected.Id,
                FloorY = floorY,
                Rejection = "Scan more of the adjacent wall."
            };

            if (otherWalls == null || otherWalls.Count == 0)
            {
                estimate.Rejection = "Scan more of the adjacent wall. No other vertical plane was found.";
                return false;
            }

            bool found = false;
            float bestScore = float.PositiveInfinity;
            WallCornerEstimate best = estimate;
            var reasons = new List<string>();

            for (int i = 0; i < otherWalls.Count; i++)
            {
                if (!TryPair(selected, otherWalls[i], floorY, out WallCornerEstimate candidate))
                {
                    reasons.Add(otherWalls[i].Id + ": " + candidate.Rejection);
                    continue;
                }

                float score = Score(selected, otherWalls[i], candidate.Corner);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    found = true;
                }
            }

            if (!found)
            {
                estimate.Rejection = "Scan more of the adjacent wall. " + string.Join(" | ", reasons);
                return false;
            }

            estimate = best;
            return true;
        }

        public static bool TryPair(
            DetectedWall selected,
            DetectedWall other,
            float floorY,
            out WallCornerEstimate estimate)
        {
            estimate = new WallCornerEstimate
            {
                SelectedWallId = selected.Id,
                OtherWallId = other.Id,
                FloorY = floorY
            };

            Vector3 n1 = selected.Normal.normalized;
            Vector3 n2 = other.Normal.normalized;
            Vector3 direction = Vector3.Cross(n1, n2);
            float crossMagnitude = direction.magnitude;
            estimate.NormalCrossMagnitude = crossMagnitude;
            estimate.IntersectionDirection = crossMagnitude > 0.0001f ? direction / crossMagnitude : Vector3.zero;

            if (crossMagnitude < MinNormalCrossMagnitude)
            {
                estimate.Rejection = "The walls are parallel or nearly parallel, so they do not define a corner.";
                return false;
            }

            if (!TryIntersectThreePlanes(
                    n1, Vector3.Dot(n1, selected.Point),
                    n2, Vector3.Dot(n2, other.Point),
                    Vector3.up, floorY,
                    out Vector3 corner))
            {
                estimate.Rejection = "The wall planes and the floor do not meet at one point.";
                return false;
            }

            estimate.Corner = corner;
            estimate.PlaneResidualMeters = PlaneResidual(corner, n1, selected.Point, n2, other.Point, floorY);
            float overflow = BoundaryOverflow(corner, selected) + BoundaryOverflow(corner, other);
            estimate.BoundaryOverflowMeters = overflow;

            float selectedDistance = Vector3.Distance(corner, selected.Center);
            float otherDistance = Vector3.Distance(corner, other.Center);
            if (selectedDistance > MaxCornerDistanceMeters || otherDistance > MaxCornerDistanceMeters)
            {
                estimate.Rejection =
                    $"The intersection is too far from the detected walls ({Mathf.Max(selectedDistance, otherDistance):0.00} m).";
                return false;
            }

            if (overflow > 0f)
            {
                estimate.Rejection =
                    $"The intersection is outside the detected wall boundaries by {overflow:0.00} m.";
                return false;
            }

            estimate.Accepted = true;
            estimate.Rejection = string.Empty;
            return true;
        }

        public static bool TryIntersectThreePlanes(
            Vector3 n1, float d1,
            Vector3 n2, float d2,
            Vector3 n3, float d3,
            out Vector3 point)
        {
            Vector3 c23 = Vector3.Cross(n2, n3);
            float determinant = Vector3.Dot(n1, c23);
            if (Mathf.Abs(determinant) < MinimumDeterminant)
            {
                point = default;
                return false;
            }

            point = (d1 * c23 + d2 * Vector3.Cross(n3, n1) + d3 * Vector3.Cross(n1, n2)) / determinant;
            return true;
        }

        static float Score(DetectedWall selected, DetectedWall other, Vector3 corner)
        {
            return Vector3.Distance(corner, selected.Center) + Vector3.Distance(corner, other.Center);
        }

        static float PlaneResidual(
            Vector3 point,
            Vector3 n1,
            Vector3 p1,
            Vector3 n2,
            Vector3 p2,
            float floorY)
        {
            float a = Mathf.Abs(Vector3.Dot(n1, point - p1));
            float b = Mathf.Abs(Vector3.Dot(n2, point - p2));
            float c = Mathf.Abs(point.y - floorY);
            return Mathf.Max(a, Mathf.Max(b, c));
        }

        public static float BoundaryOverflow(Vector3 worldPoint, DetectedWall wall)
        {
            Vector2 local = ToPlanePoint(worldPoint, wall);
            if (wall.Boundary != null && wall.Boundary.Length >= 3)
            {
                if (Contains(wall.Boundary, local))
                {
                    return 0f;
                }

                return Mathf.Max(0f, DistanceToPolygon(wall.Boundary, local) - BoundaryMarginMeters);
            }

            Vector2 delta = local - ToPlanePoint(wall.Center, wall);
            float outsideU = Mathf.Max(0f, Mathf.Abs(delta.x) - wall.HalfExtents.x - BoundaryMarginMeters);
            float outsideV = Mathf.Max(0f, Mathf.Abs(delta.y) - wall.HalfExtents.y - BoundaryMarginMeters);
            return outsideU + outsideV;
        }

        static Vector2 ToPlanePoint(Vector3 worldPoint, DetectedWall wall)
        {
            Vector3 offset = worldPoint - wall.Point;
            return new Vector2(Vector3.Dot(offset, wall.AxisU), Vector3.Dot(offset, wall.AxisV));
        }

        static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y + 0.0000001f) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        static float DistanceToPolygon(Vector2[] polygon, Vector2 point)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                best = Mathf.Min(best, DistanceToSegment(point, a, b));
            }

            return best;
        }

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 0.0000001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
