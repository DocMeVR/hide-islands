using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Hide Mesh Islands logic, separate from its window so it can be re-run after a reimport.</summary>
    public static class HideIslandsCore
    {
        const string HideShape = "DocMe_HideIslands";

        /// <summary>Vertices another tool owns on a renderer (the eye installer's new iris, say), which a click
        /// must never hide. Each function gets the mesh underneath this tool's own copy; vertex indices survive
        /// every rebuild, so indices into that mesh apply to the current one.</summary>
        public static readonly List<System.Func<GameObject, SkinnedMeshRenderer, Mesh, IEnumerable<int>>> ProtectedVertices
            = new List<System.Func<GameObject, SkinnedMeshRenderer, Mesh, IEnumerable<int>>>();

        public enum PartKind { None, Part, TooBig, Protected }

        /// <summary>What a click landed on: a part that can be hidden, the face (too big) or something another tool owns.</summary>
        public static PartKind Classify(GameObject avatar, IslandPick pick, out List<int> island)
        {
            island = null;
            if (avatar == null || pick.renderer == null || pick.renderer.sharedMesh == null || !pick.renderer.sharedMesh.isReadable) return PartKind.None;
            island = MeshIslands.SelectIslandFromSeed(pick.renderer, pick.vertex, Vector3.zero, Vector3.forward, out _, out _);
            if (island.Count == 0) return PartKind.TooBig;
            var keep = Protected(avatar, pick.renderer);
            return island.Exists(keep.Contains) ? PartKind.Protected : PartKind.Part;
        }

        public static bool Apply(GameObject avatar, List<IslandPick> picks, bool deleteGeometry, bool sinkIntoHead,
                                 out int verts, out int meshes, out int skippedProtected)
        {
            verts = meshes = skippedProtected = 0;
            if (avatar == null || picks == null) return false;
            if (avatar.GetComponent<DocMeIslandHide>() != null) Revert(avatar);

            Undo.RegisterFullObjectHierarchyUndo(avatar, "Hide Mesh Islands");
            SourceModel.EnsureGenDir();

            var perRenderer = new Dictionary<SkinnedMeshRenderer, List<List<int>>>();
            var keepVerts = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();
            var used = new List<IslandPick>();
            foreach (var p in picks)
            {
                if (p.renderer == null || p.renderer.sharedMesh == null || !p.renderer.sharedMesh.isReadable) continue;
                var island = MeshIslands.SelectIslandFromSeed(p.renderer, p.vertex, Vector3.zero, Vector3.forward, out _, out _);
                if (island.Count == 0) continue;

                if (!keepVerts.TryGetValue(p.renderer, out var keep)) keepVerts[p.renderer] = keep = Protected(avatar, p.renderer);
                if (island.Exists(keep.Contains)) { skippedProtected++; continue; }

                if (!perRenderer.TryGetValue(p.renderer, out var l)) perRenderer[p.renderer] = l = new List<List<int>>();
                l.Add(island);
                used.Add(p);
            }
            if (perRenderer.Count == 0) return false;

            var marker = Undo.AddComponent<DocMeIslandHide>(avatar);
            marker.deleteGeometry = deleteGeometry;
            marker.sinkIntoHead = sinkIntoHead;
            marker.appliedAt = System.DateTime.UtcNow.Ticks;
            foreach (var p in used) marker.picks.Add(SourceModel.ToPick(avatar, "", p));

            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;

            foreach (var kv in perRenderer)
            {
                var smr = kv.Key;
                var src = smr.sharedMesh;
                var work = Object.Instantiate(src);
                work.name = src.name + (deleteGeometry ? "_DocMeTrimmed" : "_DocMeHidden");

                bool ok;
                if (deleteGeometry) ok = MeshIslands.RemoveIslandTriangles(work, kv.Value);
                else
                {
                    Vector3? sink = null;
                    if (sinkIntoHead && MeshIslands.TryGetBoneBindPosition(smr, head, out var hp)) sink = hp;
                    ok = MeshIslands.AddHideShape(work, kv.Value, HideShape, default, 2f, sink);
                }
                if (!ok) { Object.DestroyImmediate(work); continue; }

                AssetDatabase.CreateAsset(work, AssetDatabase.GenerateUniqueAssetPath(SourceModel.GenDir + "/" + work.name + ".asset"));

                var entry = new DocMeIslandHide.Entry
                {
                    renderer = smr, originalMesh = src, modifiedMesh = work,
                    blendShapeName = deleteGeometry ? null : HideShape,
                };
                SourceModel.Root(avatar, src, out entry.sourcePath, out entry.sourceMeshName);
                marker.entries.Add(entry);

                smr.sharedMesh = work;
                if (!deleteGeometry) smr.SetBlendShapeWeight(work.GetBlendShapeIndex(HideShape), 100f);
                foreach (var island in kv.Value) verts += island.Count;
                meshes++;
            }

            marker.sourceHash = SourceModel.Hash(marker.entries.Select(e => e.sourcePath));
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            return meshes > 0;
        }

        static HashSet<int> Protected(GameObject avatar, SkinnedMeshRenderer smr)
        {
            var set = new HashSet<int>();
            var baseMesh = smr.sharedMesh;
            var hide = avatar.GetComponent<DocMeIslandHide>();
            if (hide != null)
                foreach (var e in hide.entries)
                    if (e.renderer == smr && e.modifiedMesh == baseMesh && e.originalMesh != null) baseMesh = e.originalMesh;
            foreach (var f in ProtectedVertices)
            {
                var v = f(avatar, smr, baseMesh);
                if (v != null) set.UnionWith(v);
            }
            return set;
        }

        public static void Revert(GameObject avatar)
        {
            var marker = avatar != null ? avatar.GetComponent<DocMeIslandHide>() : null;
            if (marker == null) return;

            Undo.RegisterFullObjectHierarchyUndo(avatar, "Revert Hide Mesh Islands");
            foreach (var e in marker.entries)
            {
                if (e.renderer != null)
                {
                    var restore = e.originalMesh != null ? e.originalMesh : SourceModel.Resolve(e.sourcePath, e.sourceMeshName, e.renderer.name);
                    if (restore != null) e.renderer.sharedMesh = restore;
                }
                if (e.modifiedMesh != null)
                {
                    string p = AssetDatabase.GetAssetPath(e.modifiedMesh);
                    if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
                }
            }
            Undo.DestroyObjectImmediate(marker);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Hides the parts behind stored picks again on the current mesh. Returns a short report.</summary>
        public static string Reapply(GameObject avatar, List<DocMePick> picks, bool deleteGeometry, bool sinkIntoHead)
        {
            var cache = new SourceModel.BakeCache();
            var seeds = new List<IslandPick>();
            int lost = 0;
            foreach (var p in picks)
                if (SourceModel.TryResolve(avatar, p, cache, out var seed)) seeds.Add(seed); else lost++;
            bool ok = Apply(avatar, seeds, deleteGeometry, sinkIntoHead, out int verts, out int meshes, out int skipped);
            var sb = new StringBuilder();
            sb.AppendLine(ok ? $"Hidden parts re-applied ({verts} verts)." : "Hidden parts: nothing left to hide.");
            if (lost + skipped > 0) sb.AppendLine($"⚠ {lost + skipped} hide {(lost + skipped == 1 ? "pick" : "picks")} no longer match the mesh and were dropped.");
            return sb.ToString().TrimEnd();
        }
    }
}
