using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Scene-view click -> nearest vertex on a skinned mesh, shared by the pickers.</summary>
    public static class MeshPickUtil
    {
        /// <summary>
        /// Ray-tests every active skinned mesh under <paramref name="root"/> and returns the vertex
        /// nearest the closest hit.
        ///
        /// Deliberately does NOT use HandleUtility.PickGameObject: that returns whatever renders
        /// topmost, so sprite/particle billboards (VRCFury icons, quality badges) parented to the
        /// avatar swallow the click. Testing the meshes ourselves also ignores scene
        /// visibility/pickability toggles and prefab-root quirks.
        /// </summary>
        public static bool Raycast(GameObject root, Vector2 mousePos,
                                   out SkinnedMeshRenderer smr, out int vertex, out Vector3 point, out string reason)
            => Raycast(root, HandleUtility.GUIPointToWorldRay(mousePos), out smr, out vertex, out point, out reason);

        /// <summary>Ray overload (also lets this be exercised outside a Scene-view GUI context).</summary>
        public static bool Raycast(GameObject root, Ray ray,
                                   out SkinnedMeshRenderer smr, out int vertex, out Vector3 point, out string reason)
        {
            smr = null; vertex = -1; point = default; reason = null;
            if (root == null) { reason = "No avatar assigned."; return false; }

            int unreadable = 0, tested = 0;
            float best = float.MaxValue;

            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // activeSelf, not activeInHierarchy: still works when the whole avatar is toggled off.
                if (!r.enabled || !r.gameObject.activeSelf) continue;
                var mesh = r.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable) { unreadable++; continue; }
                // Cheap reject before the expensive bake.
                if (!r.bounds.IntersectRay(ray)) continue;

                tested++;
                var hits = RaycastAll(r, ray);
                if (hits.Count > 0 && hits[0].distance < best)
                {
                    best = hits[0].distance; smr = r; vertex = hits[0].vertex; point = hits[0].point;
                }
            }

            if (smr != null) return true;

            if (unreadable > 0)
                reason = $"Missed. {unreadable} mesh(es) are skipped because Read/Write is disabled: enable it on the model importer.";
            else if (tested == 0)
                reason = "Missed: no skinned mesh under the ray. Is the right avatar assigned?";
            else
                reason = "Missed: click directly on the mesh surface.";
            return false;
        }

        public struct Hit
        {
            public float distance;
            public int vertex;     // nearest corner of the hit triangle
            public Vector3 point;
        }

        /// <summary>Every ray hit on one renderer's current pose, nearest first.</summary>
        public static List<Hit> RaycastAll(SkinnedMeshRenderer r, Ray ray)
        {
            var hits = new List<Hit>();
            if (r == null || r.sharedMesh == null || !r.sharedMesh.isReadable) return hits;

            var world = MeshBake.BakeWorldVertices(r, out var baked);
            var tris = baked.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (!RayTriangle(ray, world[a], world[b], world[c], out float dist, out Vector3 p)) continue;
                float da = (world[a] - p).sqrMagnitude, db = (world[b] - p).sqrMagnitude, dc = (world[c] - p).sqrMagnitude;
                int nv = da <= db && da <= dc ? a : db <= dc ? b : c;
                hits.Add(new Hit { distance = dist, vertex = nv, point = p });
            }
            Object.DestroyImmediate(baked);
            hits.Sort((x, y) => x.distance.CompareTo(y.distance));
            return hits;
        }

        static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float dist, out Vector3 hit)
        {
            dist = 0f; hit = default;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float inv = 1f / det;
            Vector3 tv = ray.origin - a;
            float u = Vector3.Dot(tv, p) * inv; if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(ray.direction, qv) * inv; if (v < 0f || u + v > 1f) return false;
            float td = Vector3.Dot(e2, qv) * inv; if (td <= 0f) return false;
            dist = td; hit = ray.origin + ray.direction * td; return true;
        }
    }
}
