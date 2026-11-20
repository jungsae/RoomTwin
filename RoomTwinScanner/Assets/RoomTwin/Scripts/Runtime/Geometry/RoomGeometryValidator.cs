using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Checks that four floor corners form one convex quadrilateral and returns them
    /// counter-clockwise when viewed from above.
    /// </summary>
    public static class RoomGeometryValidator
    {
        public const int RequiredCornerCount = 4;
        public const float MinimumEdgeMeters = 0.05f;

        public static bool TryValidate(RoomGeometryData data, out Vector2[] orderedCorners, out string error)
        {
            orderedCorners = null;
            error = null;

            if (data == null)
            {
                error = "Room geometry is missing.";
                return false;
            }

            if (!float.IsFinite(data.wallHeightMeters) || data.wallHeightMeters <= 0f)
            {
                error = "Wall height must be a finite value greater than 0 m.";
                return false;
            }

            Vector2[] corners = data.floorCornersXz;
            if (corners == null || corners.Length != RequiredCornerCount)
            {
                error = "Enter exactly 4 floor corners.";
                return false;
            }

            for (int i = 0; i < corners.Length; i++)
            {
                if (!float.IsFinite(corners[i].x) || !float.IsFinite(corners[i].y))
                {
                    error = $"Corner {i + 1} has a non-finite coordinate.";
                    return false;
                }
            }

            for (int i = 0; i < corners.Length; i++)
            {
                for (int j = i + 1; j < corners.Length; j++)
                {
                    if (Vector2.Distance(corners[i], corners[j]) < MinimumEdgeMeters)
                    {
                        error = $"Corners {i + 1} and {j + 1} are closer than {MinimumEdgeMeters:0.00} m.";
                        return false;
                    }
                }
            }

            float turnSign = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 previous = corners[i];
                Vector2 current = corners[(i + 1) % corners.Length];
                Vector2 next = corners[(i + 2) % corners.Length];
                float turn = Cross(current - previous, next - current);
                if (Mathf.Abs(turn) <= 0.0001f)
                {
                    error = "Corners are collinear. Use a convex quadrilateral in boundary order.";
                    return false;
                }

                float sign = Mathf.Sign(turn);
                if (turnSign == 0f)
                {
                    turnSign = sign;
                }
                else if (sign != turnSign)
                {
                    error = "Corner order is not a convex quadrilateral. Walk the boundary clockwise or counter-clockwise without crossing.";
                    return false;
                }
            }

            orderedCorners = (Vector2[])corners.Clone();
            if (SignedArea(orderedCorners) < 0f)
            {
                Vector2 second = orderedCorners[1];
                orderedCorners[1] = orderedCorners[3];
                orderedCorners[3] = second;
            }

            return true;
        }

        public static float SignedArea(Vector2[] corners)
        {
            float sum = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 current = corners[i];
                Vector2 next = corners[(i + 1) % corners.Length];
                sum += current.x * next.y - next.x * current.y;
            }

            return sum * 0.5f;
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
