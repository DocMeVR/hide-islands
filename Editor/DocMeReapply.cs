using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>A tool that leaves a marker on the avatar and can rebuild its work on the current source model.</summary>
    public interface IReapplyHandler
    {
        /// <summary>Lower runs first. A tool that builds on top of another tool's work rebuilds both, so it
        /// registers with a lower order and the other tool's handler is not run for that avatar.</summary>
        int Order { get; }
        /// <summary>True when this tool's marker is on the avatar.</summary>
        bool Claims(GameObject avatar);
        /// <summary>Every avatar in the open scenes that carries this tool's marker.</summary>
        IEnumerable<GameObject> FindInstalled();
        /// <summary>Model files the install was built from.</summary>
        IEnumerable<string> SourcePaths(GameObject avatar);
        /// <summary>True when a source model changed since the install.</summary>
        bool IsStale(GameObject avatar);
        /// <summary>The recorded source of a mesh this tool generated.</summary>
        bool TryGetSource(GameObject avatar, Mesh generated, out string path, out string meshName);
        /// <summary>Rebuilds the install on the current source model and returns a short report.</summary>
        string Reapply(GameObject avatar);
    }

    /// <summary>Runs the stored installs again on the current source model (after a reimport). Tools register
    /// a handler once per domain load.</summary>
    public static class DocMeReapply
    {
        const string AutoKey = "DocMe.EyeInstaller.autoReapply";
        public static bool AutoEnabled
        {
            get => EditorPrefs.GetBool(AutoKey, true);
            set => EditorPrefs.SetBool(AutoKey, value);
        }

        static readonly List<IReapplyHandler> handlers = new List<IReapplyHandler>();
        public static IReadOnlyList<IReapplyHandler> Handlers => handlers;

        public static void Register(IReapplyHandler handler)
        {
            if (handlers.Any(h => h.GetType() == handler.GetType())) return;
            handlers.Add(handler);
            handlers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public static IEnumerable<string> SourcePaths(GameObject avatar) => handlers.SelectMany(h => h.SourcePaths(avatar));

        /// <summary>True when any tool's source model was reimported since its install.</summary>
        public static bool IsStale(GameObject avatar) => handlers.Any(h => h.Claims(avatar) && h.IsStale(avatar));

        public static string ReapplyAll(GameObject avatar)
        {
            foreach (var h in handlers)
                if (h.Claims(avatar)) return h.Reapply(avatar);
            return "Nothing installed.";
        }
    }

    class HideReapplyHandler : IReapplyHandler
    {
        [InitializeOnLoadMethod]
        static void Init() => DocMeReapply.Register(new HideReapplyHandler());

        public int Order => 100;
        public bool Claims(GameObject avatar) => avatar != null && avatar.GetComponent<DocMeIslandHide>() != null;
        public IEnumerable<GameObject> FindInstalled() => Object.FindObjectsOfType<DocMeIslandHide>(true).Select(m => m.gameObject);

        public IEnumerable<string> SourcePaths(GameObject avatar)
        {
            var hide = avatar.GetComponent<DocMeIslandHide>();
            if (hide != null) foreach (var e in hide.entries) yield return e.sourcePath;
        }

        public bool IsStale(GameObject avatar)
        {
            var hide = avatar.GetComponent<DocMeIslandHide>();
            return hide != null && !string.IsNullOrEmpty(hide.sourceHash) &&
                   hide.sourceHash != SourceModel.Hash(hide.entries.Select(e => e.sourcePath));
        }

        public bool TryGetSource(GameObject avatar, Mesh generated, out string path, out string meshName)
        {
            path = meshName = null;
            var hide = avatar.GetComponent<DocMeIslandHide>();
            if (hide == null) return false;
            foreach (var e in hide.entries)
                if (e.modifiedMesh == generated && !string.IsNullOrEmpty(e.sourcePath)) { path = e.sourcePath; meshName = e.sourceMeshName; return true; }
            return false;
        }

        public string Reapply(GameObject avatar)
        {
            var hide = avatar.GetComponent<DocMeIslandHide>();
            if (hide == null) return "Nothing hidden.";
            if (hide.appliedAt == 0)
                return "⚠ Hidden with an older version that didn't record its picks. Show all and hide the parts once more; after that it re-applies by itself.";
            var picks = new List<DocMePick>(hide.picks);
            bool delete = hide.deleteGeometry, sink = hide.sinkIntoHead;
            HideIslandsCore.Revert(avatar);
            return picks.Count == 0 ? "Nothing hidden." : HideIslandsCore.Reapply(avatar, picks, delete, sink);
        }
    }

    /// <summary>Rebuilds every install whose source model was just reimported.</summary>
    class ReimportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!DocMeReapply.AutoEnabled || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var changed = new HashSet<string>(imported.Where(p => !SourceModel.IsGenerated(p)));
            if (changed.Count == 0) return;

            var avatars = new HashSet<GameObject>();
            foreach (var h in DocMeReapply.Handlers)
                foreach (var a in h.FindInstalled()) avatars.Add(a);
            var hit = avatars.Where(a => DocMeReapply.SourcePaths(a).Any(p => p != null && changed.Contains(p))).ToList();
            if (hit.Count == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (var a in hit)
                    if (a != null) Debug.Log($"[DocMe] '{a.name}': source model reimported.\n" + DocMeReapply.ReapplyAll(a), a);
            };
        }
    }
}
