using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace DVRouteOverlay
{
    internal sealed class MapRibbon : IDisposable
    {
        private readonly GameObject host;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly Func<Vector3, bool, Vector3> project;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        internal int VertexCount { get { return mesh ? mesh.vertexCount : 0; } }

        internal MapRibbon(MapMarkersController map)
        {
            var method = typeof(MapMarkersController).GetMethod("GetMapPosition",
                BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Vector3), typeof(bool) }, null);
            if (method == null) throw new MissingMethodException("MapMarkersController", "GetMapPosition");
            project = (Func<Vector3, bool, Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3, bool, Vector3>), map, method);
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            if (!shader) throw new InvalidOperationException("No supported overlay shader is available.");
            material = new Material(shader) { name = "DVRouteOverlay Material", renderQueue = 3001 };
            if (material.HasProperty("_MainTex")) material.mainTexture = Texture2D.whiteTexture;
            mesh = new Mesh { name = "DVRouteOverlay Ribbon" };
            mesh.MarkDynamic();
            host = new GameObject("DVRouteOverlay Ribbon");
            host.layer = map.gameObject.layer;
            host.transform.SetParent(map.transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            host.SetActive(false);
        }

        internal bool Draw(List<Vector3> points, float width, Color color)
        {
            if (!host || points.Count < 2) { Hide(); return false; }
            vertices.Clear();
            triangles.Clear();
            Vector3 a = project(points[0], false) + Vector3.up * 0.00015f;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 b = project(points[i], false) + Vector3.up * 0.00015f;
                Vector3 delta = b - a;
                delta.y = 0;
                if (delta.sqrMagnitude > 0.000000000001f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, delta).normalized * (width * 0.5f);
                    int n = vertices.Count;
                    vertices.Add(a - side); vertices.Add(a + side);
                    vertices.Add(b - side); vertices.Add(b + side);
                    // Both windings make the overlay visible with any map orientation.
                    triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
                    triangles.Add(n + 2); triangles.Add(n + 1); triangles.Add(n + 3);
                    triangles.Add(n + 2); triangles.Add(n + 1); triangles.Add(n);
                    triangles.Add(n + 3); triangles.Add(n + 1); triangles.Add(n + 2);
                }
                a = b;
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            material.color = color;
            host.SetActive(vertices.Count > 0);
            return vertices.Count > 0;
        }

        internal void Hide() { if (host) host.SetActive(false); }
        internal void SetVisible(bool visible) { if (host) host.SetActive(visible && mesh && mesh.vertexCount > 0); }
        public void Dispose()
        {
            if (host) { host.SetActive(false); UnityEngine.Object.Destroy(host); }
            if (mesh) UnityEngine.Object.Destroy(mesh);
            if (material) UnityEngine.Object.Destroy(material);
        }
    }
}
