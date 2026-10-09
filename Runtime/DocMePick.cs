using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>A Scene-view pick stored by position (avatar-local) instead of vertex index, so it
    /// survives a reimport that renumbers the mesh.</summary>
    [System.Serializable]
    public class DocMePick
    {
        public string label;                 // what the pick is for: "L" / "R" for eye picks, empty for hide picks
        public SkinnedMeshRenderer renderer;
        public Vector3 avatarLocalPoint;
    }
}
