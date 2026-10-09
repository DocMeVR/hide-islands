using System.Collections.Generic;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Revert record for the Hide Mesh Islands tool. Stripped at build by the VRChat SDK.</summary>
    [DisallowMultipleComponent]
    public class DocMeIslandHide : MonoBehaviour
#if VRC_SDK_VRCSDK3
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [System.Serializable]
        public class Entry
        {
            public SkinnedMeshRenderer renderer;
            public Mesh originalMesh;   // restored on revert
            public Mesh modifiedMesh;   // our copy, deleted on revert
            public string blendShapeName;
            public string sourcePath;   // model file the mesh ultimately comes from
            public string sourceMeshName;
        }

        public List<Entry> entries = new List<Entry>();

        public List<DocMePick> picks = new List<DocMePick>();
        public bool deleteGeometry;
        public bool sinkIntoHead = true;
        public long appliedAt;
        public string sourceHash;
    }
}
