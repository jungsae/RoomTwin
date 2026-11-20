using NUnit.Framework;
using UnityEngine;

namespace RoomTwin.Geometry.Tests
{
    public sealed class RoomGeometryTests
    {
        [Test]
        public void DefaultRectangle_IsValidAndCounterClockwise()
        {
            RoomGeometryData data = RoomGeometryData.CreateDefault();

            bool valid = RoomGeometryValidator.TryValidate(data, out Vector2[] ordered, out string error);

            Assert.IsTrue(valid, error);
            Assert.Greater(RoomGeometryValidator.SignedArea(ordered), 0f);
            Assert.AreEqual(new Vector2(0f, 0f), ordered[0]);
            Assert.AreEqual(new Vector2(4f, 0f), ordered[1]);
            Assert.AreEqual(new Vector2(4f, 3f), ordered[2]);
            Assert.AreEqual(new Vector2(0f, 3f), ordered[3]);
        }

        [Test]
        public void ClockwiseRectangle_IsAcceptedAndNormalized()
        {
            var data = new RoomGeometryData
            {
                floorCornersXz = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 3f),
                    new Vector2(4f, 3f),
                    new Vector2(4f, 0f)
                },
                wallHeightMeters = 2.4f
            };

            bool valid = RoomGeometryValidator.TryValidate(data, out Vector2[] ordered, out string error);

            Assert.IsTrue(valid, error);
            Assert.Greater(RoomGeometryValidator.SignedArea(ordered), 0f);
            Assert.AreEqual(data.floorCornersXz[0], ordered[0]);
        }

        [Test]
        public void CrossedCorners_FailWithOrderError()
        {
            var data = new RoomGeometryData
            {
                floorCornersXz = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(4f, 3f),
                    new Vector2(4f, 0f),
                    new Vector2(0f, 3f)
                },
                wallHeightMeters = 2.4f
            };

            bool valid = RoomGeometryValidator.TryValidate(data, out _, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("convex", error);
        }

        [Test]
        public void DuplicateCorner_Fails()
        {
            var data = RoomGeometryData.CreateDefault();
            data.floorCornersXz[1] = data.floorCornersXz[0];

            bool valid = RoomGeometryValidator.TryValidate(data, out _, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("closer", error);
        }

        [Test]
        public void NonPositiveHeight_Fails()
        {
            RoomGeometryData data = RoomGeometryData.CreateDefault();
            data.wallHeightMeters = 0f;

            bool valid = RoomGeometryValidator.TryValidate(data, out _, out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("height", error);
        }

        [Test]
        public void FloorTriangles_PointUp_AndMatchDefaultSize()
        {
            Assert.IsTrue(RoomGeometryValidator.TryValidate(
                RoomGeometryData.CreateDefault(),
                out Vector2[] corners,
                out string error), error);

            Mesh floor = RoomMeshGenerator.BuildFloor(corners);
            try
            {
                Assert.AreEqual(4, floor.vertexCount);
                Assert.AreEqual(6, floor.triangles.Length);
                AssertFloorWindingPointsUp(floor);
                Assert.AreEqual(4f, floor.bounds.size.x, 0.001f);
                Assert.AreEqual(3f, floor.bounds.size.z, 0.001f);
                Assert.AreEqual(0f, floor.bounds.size.y, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(floor);
            }
        }

        [Test]
        public void WallNormals_PointInward_AndUseRequestedHeight()
        {
            const float height = 2.4f;
            Assert.IsTrue(RoomGeometryValidator.TryValidate(
                RoomGeometryData.CreateDefault(),
                out Vector2[] corners,
                out string error), error);

            Mesh[] walls = RoomMeshGenerator.BuildWalls(corners, height);
            Vector3 centroid = new Vector3(2f, height * 0.5f, 1.5f);
            try
            {
                Assert.AreEqual(4, walls.Length);
                for (int i = 0; i < walls.Length; i++)
                {
                    AssertWallFacesInward(walls[i], centroid, height);
                }
            }
            finally
            {
                for (int i = 0; i < walls.Length; i++)
                {
                    Object.DestroyImmediate(walls[i]);
                }
            }
        }

        static void AssertFloorWindingPointsUp(Mesh floor)
        {
            Vector3[] vertices = floor.vertices;
            int[] triangles = floor.triangles;
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                Vector3 winding = Vector3.Cross(
                    vertices[triangles[triangle + 1]] - vertices[triangles[triangle]],
                    vertices[triangles[triangle + 2]] - vertices[triangles[triangle]]);
                Assert.Greater(winding.y, 0f);
            }
            foreach (Vector3 normal in floor.normals)
            {
                Assert.Greater(normal.y, 0.99f);
            }
        }

        static void AssertWallFacesInward(Mesh wall, Vector3 centroid, float height)
        {
            Vector3[] vertices = wall.vertices;
            int[] triangles = wall.triangles;
            Vector3 towardInside = centroid - wall.bounds.center;
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                Vector3 winding = Vector3.Cross(
                    vertices[triangles[triangle + 1]] - vertices[triangles[triangle]],
                    vertices[triangles[triangle + 2]] - vertices[triangles[triangle]]).normalized;
                Assert.Greater(Vector3.Dot(winding, towardInside), 0f);
            }
            Assert.Greater(Vector3.Dot(wall.normals[0], towardInside), 0f);
            Assert.AreEqual(height, wall.bounds.size.y, 0.001f);
        }
    }
}
