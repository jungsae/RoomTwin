using System;
using UnityEngine;

namespace RoomTwin.AR
{
    /// <summary>
    /// Loads the transparent scan shader from a Resources material so the player build keeps it.
    /// A missing shader hides the mesh instead of falling back to an opaque material.
    /// </summary>
    static class ScanMaterials
    {
        const string LogTag = "[RoomTwin][FieldTest]";
        const string ResourcePath = "ScanPlaneTransparent";

        static Shader transparentShader;
        static bool warned;

        public static Material CreateTransparent(Color color)
        {
            Shader shader = TransparentShader();
            if (shader == null)
            {
                return null;
            }

            try
            {
                var material = new Material(shader);
                material.color = color;
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }

                return material;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogTag + " transparent-material=failure " + ex.GetType().Name);
                return null;
            }
        }

        public static Shader TransparentShader()
        {
            if (transparentShader != null)
            {
                return transparentShader;
            }

            Material template = Resources.Load<Material>(ResourcePath);
            if (template != null && template.shader != null && template.shader.name == "RoomTwin/ArPlaneTransparent")
            {
                transparentShader = template.shader;
                return transparentShader;
            }

            transparentShader = Shader.Find("RoomTwin/ArPlaneTransparent");
            if (transparentShader == null && !warned)
            {
                warned = true;
                Debug.LogWarning(LogTag + " transparent-shader=missing mesh-hidden");
            }

            return transparentShader;
        }
    }
}
