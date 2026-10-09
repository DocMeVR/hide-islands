using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe.HideIslands
{
    /// <summary>Scene-view highlight of the mesh islands the user has picked: builds a world-space
    /// preview mesh per island (cached) and draws it as a pulsing translucent overlay plus a
    /// see-through ghost, so it reads even when the island is behind eyelashes.</summary>
    public class IslandPreview
    {
        readonly Dictionary<(SkinnedMeshRenderer, int), Mesh> cache = new Dictionary<(SkinnedMeshRenderer, int), Mesh>();
        Material mat;

        public void Clear()
        {
            foreach (var m in cache.Values) if (m != null) Object.DestroyImmediate(m);
            cache.Clear();
        }

        /// <summary>Island preview mesh for a clicked vertex, built once and reused.</summary>
        public Mesh Get(SkinnedMeshRenderer smr, int seedVertex)
        {
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) return null;
            var key = (smr, seedVertex);
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var island = MeshIslands.SelectIslandFromSeed(smr, seedVertex, Vector3.zero, Vector3.forward, out _, out _);
            if (island.Count == 0) return null;
            return cache[key] = Build(smr, island);
        }

        /// <summary>Preview mesh for a known vert set (what auto detection found). Keyed by a negative id.</summary>
        public Mesh GetFromVerts(SkinnedMeshRenderer smr, List<int> island, int id)
        {
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable || island == null || island.Count == 0) return null;
            var key = (smr, -1 - id);
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            return cache[key] = Build(smr, island);
        }

        static Mesh Build(SkinnedMeshRenderer smr, List<int> island)
        {

            var world = MeshBake.BakeWorldVertices(smr, out var baked);
            var tris = baked.triangles;
            Object.DestroyImmediate(baked);

            var inIsland = new HashSet<int>(island);
            var remap = new Dictionary<int, int>(island.Count);
            var verts = new List<Vector3>(island.Count);
            var outTris = new List<int>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (!inIsland.Contains(a) || !inIsland.Contains(b) || !inIsland.Contains(c)) continue;
                foreach (int v in new[] { a, b, c })
                {
                    if (!remap.TryGetValue(v, out int nv)) { nv = verts.Count; remap[v] = nv; verts.Add(world[v]); }
                    outTris.Add(nv);
                }
            }
            if (outTris.Count == 0) return null;

            var preview = new Mesh { name = "DocMe_IslandPreview", hideFlags = HideFlags.HideAndDontSave };
            preview.SetVertices(verts);
            preview.SetTriangles(outTris, 0);
            preview.RecalculateBounds();
            return preview;
        }

        /// <summary>Draws the highlight. Call from OnSceneGUI during Repaint.</summary>
        public void Draw(Mesh preview, Color color, bool pulse)
        {
            if (preview == null) return;
            if (mat == null)
            {
                mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_Cull", (int)CullMode.Off);
                mat.SetInt("_ZWrite", 0);
            }

            float t = pulse ? 0.55f + 0.45f * Mathf.Sin((float)EditorApplication.timeSinceStartup * 4f) : 1f;

            // Ghost pass: visible through the mesh so you can see picks hidden behind lashes/hair.
            mat.SetInt("_ZTest", (int)CompareFunction.Always);
            mat.SetColor("_Color", new Color(color.r, color.g, color.b, 0.14f * t));
            mat.SetPass(0);
            Graphics.DrawMeshNow(preview, Matrix4x4.identity);

            // Solid pass on the visible surface.
            mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            mat.SetColor("_Color", new Color(color.r, color.g, color.b, 0.55f * t));
            mat.SetPass(0);
            Graphics.DrawMeshNow(preview, Matrix4x4.identity);
        }
    }
}
