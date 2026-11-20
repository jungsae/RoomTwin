using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomTwin.Geometry
{
    /// <summary>
    /// Builds a room mesh from inspector data in the controller's local space.
    /// That space is room-local meters, independent of any AR session.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RoomGeometryController : MonoBehaviour
    {
        const string GeneratedName = "GeneratedRoom";
        const float LabelSizeMeters = 0.28f;

        static readonly string[][] DigitPatterns =
        {
            new[] { ".#...", "##...", ".#...", ".#...", ".#...", ".#...", "###.." },
            new[] { "###..", "...#.", "..#..", ".#...", "#....", "#....", "####." },
            new[] { "###..", "...#.", "...#.", "###..", "...#.", "...#.", "###.." },
            new[] { "#..#.", "#..#.", "#..#.", "####.", "...#.", "...#.", "...#." }
        };

        [SerializeField]
        RoomGeometryData geometry = null;

        [SerializeField]
        Material floorMaterial;

        [SerializeField]
        Material wallMaterial;

        readonly List<Object> ownedObjects = new List<Object>();
        Transform generatedRoot;

        public RoomGeometryData Geometry => geometry;
        public string LastError { get; private set; } = string.Empty;
        public bool HasValidMesh { get; private set; }

        public void SetGeometry(RoomGeometryData data)
        {
            geometry = data;
            Rebuild();
        }

        [ContextMenu("Rebuild Room")]
        public void Rebuild()
        {
            EnsureDefaultGeometry();
            ClearGenerated();

            if (!RoomGeometryValidator.TryValidate(geometry, out Vector2[] orderedCorners, out string error))
            {
                LastError = error;
                HasValidMesh = false;
                Debug.LogWarning("[RoomTwin][Geometry] " + error, this);
                return;
            }

            LastError = string.Empty;
            HasValidMesh = true;
            generatedRoot = CreateGeneratedRoot();

            CreateMeshObject("Floor", RoomMeshGenerator.BuildFloor(orderedCorners), FloorMaterial());
            Mesh[] walls = RoomMeshGenerator.BuildWalls(orderedCorners, geometry.wallHeightMeters);
            Material wallsMaterial = WallMaterial();
            for (int i = 0; i < walls.Length; i++)
            {
                CreateMeshObject(walls[i].name, walls[i], wallsMaterial);
            }

            Vector2[] sourceCorners = geometry.floorCornersXz;
            for (int i = 0; i < sourceCorners.Length; i++)
            {
                CreateCornerLabel(i, sourceCorners[i]);
            }
        }

        void Reset()
        {
            geometry = RoomGeometryData.CreateDefault();
        }

        void OnEnable()
        {
            Rebuild();
        }

        void EnsureDefaultGeometry()
        {
            if (geometry == null || geometry.floorCornersXz == null || geometry.floorCornersXz.Length == 0)
            {
                geometry = RoomGeometryData.CreateDefault();
            }
        }

        Transform CreateGeneratedRoot()
        {
            var rootObject = new GameObject(GeneratedName);
            rootObject.transform.SetParent(transform, false);
            return rootObject.transform;
        }

        void CreateMeshObject(string objectName, Mesh mesh, Material material)
        {
            ownedObjects.Add(mesh);
            var meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(generatedRoot, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        void CreateCornerLabel(int index, Vector2 corner)
        {
            var labelObject = new GameObject($"Corner_{index + 1}");
            labelObject.transform.SetParent(generatedRoot, false);
            labelObject.transform.localPosition = new Vector3(corner.x, 0.02f, corner.y);
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Mesh quad = BuildLabelQuad();
            ownedObjects.Add(quad);
            labelObject.AddComponent<MeshFilter>().sharedMesh = quad;

            Material labelMaterial = CreateLabelMaterial(index + 1);
            ownedObjects.Add(labelMaterial);
            MeshRenderer renderer = labelObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = labelMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        Material FloorMaterial()
        {
            return floorMaterial != null ? floorMaterial : CreateSolidMaterial("FloorFallback", new Color(0.62f, 0.48f, 0.32f));
        }

        Material WallMaterial()
        {
            return wallMaterial != null ? wallMaterial : CreateSolidMaterial("WallFallback", new Color(0.62f, 0.74f, 0.82f));
        }

        Material CreateSolidMaterial(string materialName, Color color)
        {
            Material material = CreateUnlitMaterial(materialName, color);
            ownedObjects.Add(material);
            return material;
        }

        Material CreateLabelMaterial(int number)
        {
            Material material = CreateUnlitMaterial($"Corner_{number}", Color.white);
            Texture2D texture = BuildDigitTexture(number);
            ownedObjects.Add(texture);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            return material;
        }

        static Material CreateUnlitMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader) { name = materialName };
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        static Mesh BuildLabelQuad()
        {
            float half = LabelSizeMeters * 0.5f;
            var mesh = new Mesh { name = "CornerLabel" };
            mesh.SetVertices(new[]
            {
                new Vector3(-half, -half, 0f),
                new Vector3(half, -half, 0f),
                new Vector3(half, half, 0f),
                new Vector3(-half, half, 0f)
            });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D BuildDigitTexture(int number)
        {
            const int scale = 8;
            string[] pattern = DigitPatterns[Mathf.Clamp(number, 1, 4) - 1];
            int width = pattern[0].Length * scale;
            int height = pattern.Length * scale;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"Digit_{number}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width * height];
            for (int row = 0; row < pattern.Length; row++)
            {
                for (int column = 0; column < pattern[row].Length; column++)
                {
                    Color color = pattern[row][column] == '#'
                        ? new Color(0.1f, 0.1f, 0.1f, 1f)
                        : new Color(0.95f, 0.78f, 0.2f, 1f);
                    int flippedRow = pattern.Length - 1 - row;
                    for (int y = 0; y < scale; y++)
                    {
                        for (int x = 0; x < scale; x++)
                        {
                            pixels[(flippedRow * scale + y) * width + column * scale + x] = color;
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        void ClearGenerated()
        {
            Transform existing = transform.Find(GeneratedName);
            if (existing != null)
            {
                DestroyObject(existing.gameObject);
            }

            generatedRoot = null;
            for (int i = 0; i < ownedObjects.Count; i++)
            {
                DestroyObject(ownedObjects[i]);
            }

            ownedObjects.Clear();
        }

        static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
