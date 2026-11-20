using System;
using UnityEngine;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Room-local floor plan. Coordinates are meters on the XZ plane and are not AR session poses.
    /// Vector2.x is room X and Vector2.y is room Z.
    /// </summary>
    [Serializable]
    public sealed class RoomGeometryData
    {
        public const float DefaultWidthMeters = 4f;
        public const float DefaultDepthMeters = 3f;
        public const float DefaultWallHeightMeters = 2.4f;

        [Tooltip("Four floor corners in room-local meters. x = X, y = Z.")]
        public Vector2[] floorCornersXz;

        [Tooltip("Wall height in room-local meters.")]
        public float wallHeightMeters = DefaultWallHeightMeters;

        public static RoomGeometryData CreateRectangle(float widthMeters, float depthMeters, float heightMeters)
        {
            return new RoomGeometryData
            {
                floorCornersXz = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(widthMeters, 0f),
                    new Vector2(widthMeters, depthMeters),
                    new Vector2(0f, depthMeters)
                },
                wallHeightMeters = heightMeters
            };
        }

        public static RoomGeometryData CreateDefault()
        {
            return CreateRectangle(DefaultWidthMeters, DefaultDepthMeters, DefaultWallHeightMeters);
        }
    }
}
