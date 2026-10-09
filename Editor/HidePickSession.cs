using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>The select-then-hide workflow shared by the installer's Done page and the standalone Hide
    /// Mesh Islands window. A click only selects a part; Hide rebuilds the mesh once for the whole list,
    /// because every rebuild writes the complete mesh asset.</summary>
    public class HidePickSession
    {
        public enum SelectResult { Selected, Deselected, Protected, TooBig, Missed }

        public GameObject avatar;
        public bool deleteGeometry;
        public bool sinkIntoHead = true;

        readonly List<IslandPick> picks = new List<IslandPick>();
        readonly List<IslandPick> pending = new List<IslandPick>();
        readonly List<HashSet<int>> pendingIslands = new List<HashSet<int>>();

        /// <summary>Parts hidden on the avatar, as seeds.</summary>
        public IReadOnlyList<IslandPick> Picks => picks;
        /// <summary>Parts selected but not hidden yet.</summary>
        public IReadOnlyList<IslandPick> Pending => pending;
        public int PendingCount => pending.Count;

        /// <summary>Parts hidden on the avatar right now (stored on its marker).</summary>
        public int Count
        {
            get { var h = avatar != null ? avatar.GetComponent<DocMeIslandHide>() : null; return h != null ? h.picks.Count : 0; }
        }

        /// <summary>Adds the clicked part to the selection, or takes it out again when it is already selected.</summary>
        public SelectResult Select(IslandPick seed)
        {
            switch (HideIslandsCore.Classify(avatar, seed, out var island))
            {
                case HideIslandsCore.PartKind.None: return SelectResult.Missed;
                case HideIslandsCore.PartKind.TooBig: return SelectResult.TooBig;
                case HideIslandsCore.PartKind.Protected: return SelectResult.Protected;
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].renderer != seed.renderer || !pendingIslands[i].Contains(seed.vertex)) continue;
                pending.RemoveAt(i); pendingIslands.RemoveAt(i);
                SceneView.RepaintAll();
                return SelectResult.Deselected;
            }
            pending.Add(seed);
            pendingIslands.Add(new HashSet<int>(island));
            SceneView.RepaintAll();
            return SelectResult.Selected;
        }

        /// <summary>Hides every selected part in one rebuild. Returns how many parts were hidden.</summary>
        public int HideSelected()
        {
            if (pending.Count == 0 || avatar == null) return 0;
            if (picks.Count == 0 && Count > 0) Reload();
            int before = Count;
            picks.AddRange(pending);
            ClearSelection();
            HideIslandsCore.Apply(avatar, picks, deleteGeometry, sinkIntoHead, out _, out _, out _);
            if (Count < picks.Count) Reload(); // a part the rebuild could not use drops out of the list
            SceneView.RepaintAll();
            return Count - before;
        }

        public void ClearSelection()
        {
            pending.Clear(); pendingIslands.Clear();
            SceneView.RepaintAll();
        }

        /// <summary>Takes back the last selected part, or the last hidden one when nothing is selected.</summary>
        public void UndoLast()
        {
            if (pending.Count > 0)
            {
                pending.RemoveAt(pending.Count - 1); pendingIslands.RemoveAt(pendingIslands.Count - 1);
                SceneView.RepaintAll();
                return;
            }
            if (picks.Count == 0) { Reload(); if (picks.Count == 0) return; }
            picks.RemoveAt(picks.Count - 1);
            Reapply();
        }

        /// <summary>Shows every hidden part again and drops the selection.</summary>
        public void Clear()
        {
            ClearSelection();
            picks.Clear();
            HideIslandsCore.Revert(avatar);
            SceneView.RepaintAll();
        }

        public void Reapply()
        {
            if (picks.Count == 0) HideIslandsCore.Revert(avatar);
            else HideIslandsCore.Apply(avatar, picks, deleteGeometry, sinkIntoHead, out _, out _, out _);
            SceneView.RepaintAll();
        }

        /// <summary>Drop the in-memory lists (the avatar changed); what is on the avatar stays.</summary>
        public void Forget() { picks.Clear(); ClearSelection(); }

        /// <summary>Picks stored on the avatar, turned back into clickable seeds. Resolved against the mesh
        /// as it was before hiding, since the hidden pieces are gone from the current one.</summary>
        public void Reload()
        {
            picks.Clear();
            var h = avatar != null ? avatar.GetComponent<DocMeIslandHide>() : null;
            if (h == null) return;
            deleteGeometry = h.deleteGeometry;
            sinkIntoHead = h.sinkIntoHead;
            var swapped = new List<(SkinnedMeshRenderer r, Mesh m)>();
            foreach (var e in h.entries)
                if (e.renderer != null && e.originalMesh != null) { swapped.Add((e.renderer, e.renderer.sharedMesh)); e.renderer.sharedMesh = e.originalMesh; }
            try
            {
                var cache = new SourceModel.BakeCache();
                foreach (var p in h.picks)
                    if (SourceModel.TryResolve(avatar, p, cache, out var seed)) picks.Add(seed);
            }
            finally { foreach (var (r, m) in swapped) r.sharedMesh = m; }
        }
    }
}
