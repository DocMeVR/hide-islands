using UnityEngine;

namespace DocMe.HideIslands
{
    public static class MeshBake
    {
        /// <summary>Current-pose vertices in world space. Use <paramref name="baked"/> for normals
        /// or triangles if needed, then Object.DestroyImmediate it.</summary>
        public static Vector3[] BakeWorldVertices(SkinnedMeshRenderer smr, out Mesh baked)
        {
            baked = new Mesh();
            smr.BakeMesh(baked);

            // BakeMesh output already carries the avatar's scale (it comes through the bones), so
            // the renderer's full localToWorldMatrix would apply that scale a second time: every
            // position ends up inflated on any avatar whose root scale isn't 1. Rotation and
            // translation only.
            var t = smr.transform;
            var m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

            var local = baked.vertices;
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = m.MultiplyPoint3x4(local[i]);
            return world;
        }
    }
}
