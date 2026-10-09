using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>One clicked spot on a skinned mesh: the renderer, the vertex nearest the click and the world point.</summary>
    public struct IslandPick
    {
        public SkinnedMeshRenderer renderer;
        public int vertex;
        public Vector3 point;
    }
}
