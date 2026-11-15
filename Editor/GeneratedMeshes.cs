using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DocMe.HideIslands
{
    /// <summary>Mesh files the eye and hide tools wrote that nothing uses any more, such as the copy an install leaves
    /// behind when it is undone with Ctrl+Z. A face copy with many blendshapes is hundreds of megabytes.</summary>
    public static class GeneratedMeshes
    {
        /// <summary>Where the tools write meshes. Versions before the split wrote to EyeInstaller.</summary>
        public static readonly string[] Folders = { SourceModel.GenDir, SourceModel.GenRoot + "/EyeInstaller" };

        static readonly HashSet<string> ReferencingTypes = new HashSet<string>
            { ".unity", ".prefab", ".asset", ".anim", ".controller", ".overridecontroller", ".playable" };

        public static List<string> FindUnused() => FindUnused(MeshFiles(Folders), ProjectFiles(), OpenRoots());

        /// <summary>The candidates that no file and no object under the roots refers to. Text files are searched for the
        /// candidates' GUIDs directly, several at once (Unity's own GetDependencies took 51 s on a 10,000 file project);
        /// binary files and files that cannot be read go through GetDependencies.</summary>
        public static List<string> FindUnused(IEnumerable<string> candidates, IEnumerable<string> files, IEnumerable<GameObject> roots)
        {
            var paths = candidates.Distinct().ToArray();
            if (paths.Length == 0) return new List<string>();
            var candidateFiles = new HashSet<string>(paths);
            var guids = paths.Select(p => Encoding.ASCII.GetBytes(AssetDatabase.AssetPathToGUID(p))).ToArray();
            var found = new int[paths.Length];
            int left = paths.Length;
            var unreadable = new ConcurrentBag<string>();

            Parallel.ForEach(files.Where(f => !candidateFiles.Contains(f)).ToArray(), (file, loop) =>
            {
                if (Volatile.Read(ref left) == 0) { loop.Stop(); return; }
                if (!SearchText(file, guids, found, ref left)) unreadable.Add(file);
            });

            var unused = new HashSet<string>(paths.Where((p, i) => found[i] == 0));
            if (unused.Count > 0 && !unreadable.IsEmpty) unused.ExceptWith(AssetDatabase.GetDependencies(unreadable.ToArray(), false));

            foreach (var mesh in MeshesUnder(roots))
                if (mesh != null) unused.Remove(AssetDatabase.GetAssetPath(mesh));

            return unused.OrderBy(p => p).ToList();
        }

        static readonly byte[] GuidKey = Encoding.ASCII.GetBytes("guid: ");
        static readonly byte[] YamlStart = Encoding.ASCII.GetBytes("%YAML");
        static readonly byte[] ObjectStart = Encoding.ASCII.GetBytes("--- !u!");
        static readonly byte[] MeshObject = Encoding.ASCII.GetBytes("--- !u!43 ");

        /// <summary>Marks every candidate GUID the file mentions. False for a file that is not Unity text YAML or cannot
        /// be read.</summary>
        static bool SearchText(string path, byte[][] guids, int[] found, ref int left)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (IOException) { return false; }
            catch (System.UnauthorizedAccessException) { return false; }
            if (IndexOf(data, YamlStart, 0, YamlStart.Length) != 0) return false;
            int first = IndexOf(data, ObjectStart, 0, System.Math.Min(data.Length, 400));
            if (first >= 0 && Same(data, first, MeshObject)) return true; // a Mesh refers to nothing

            int last = data.Length - GuidKey.Length - 32;
            for (int at = IndexOf(data, GuidKey, 0, data.Length); at >= 0 && at <= last; at = IndexOf(data, GuidKey, at + 1, data.Length))
            {
                int g = at + GuidKey.Length;
                for (int k = 0; k < guids.Length; k++)
                {
                    if (Volatile.Read(ref found[k]) != 0 || !Same(data, g, guids[k])) continue;
                    if (Interlocked.Exchange(ref found[k], 1) == 0 && Interlocked.Decrement(ref left) == 0) return true;
                }
            }
            return true;
        }

        static int IndexOf(byte[] data, byte[] key, int from, int to)
        {
            for (int i = from; i <= to - key.Length; i++)
            {
                i = System.Array.IndexOf(data, key[0], i, to - key.Length - i + 1);
                if (i < 0) return -1;
                if (Same(data, i, key)) return i;
            }
            return -1;
        }

        static bool Same(byte[] data, int at, byte[] key)
        {
            if (at + key.Length > data.Length) return false;
            for (int j = 0; j < key.Length; j++) if (data[at + j] != key[j]) return false;
            return true;
        }

        /// <summary>Meshes the loaded objects use: renderers, mesh filters and colliders, plus any mesh a script field
        /// holds (the DocMe install records keep the original and the generated mesh that way).</summary>
        static IEnumerable<Mesh> MeshesUnder(IEnumerable<GameObject> roots)
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    switch (c)
                    {
                        case SkinnedMeshRenderer s: yield return s.sharedMesh; break;
                        case MeshFilter f: yield return f.sharedMesh; break;
                        case MeshCollider m: yield return m.sharedMesh; break;
                        case MonoBehaviour b:
                            var it = new SerializedObject(b).GetIterator();
                            while (it.Next(true))
                                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is Mesh held)
                                    yield return held;
                            break;
                    }
                }
            }
        }

        public static List<string> MeshFiles(IEnumerable<string> folders)
        {
            var valid = folders.Where(AssetDatabase.IsValidFolder).ToArray();
            if (valid.Length == 0) return new List<string>();
            return AssetDatabase.FindAssets("t:Mesh", valid).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        }

        /// <summary>Every file in the project that can hold a reference to a mesh.</summary>
        public static IEnumerable<string> ProjectFiles()
            => AssetDatabase.GetAllAssetPaths().Where(p => (p.StartsWith("Assets/") || p.StartsWith("Packages/"))
                                                           && ReferencingTypes.Contains(Path.GetExtension(p).ToLowerInvariant()));

        /// <summary>Everything loaded right now, unsaved changes included: the open scenes and an open prefab.</summary>
        public static IEnumerable<GameObject> OpenRoots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) foreach (var r in scene.GetRootGameObjects()) yield return r;
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) yield return stage.prefabContentsRoot;
        }

        public static long Bytes(IEnumerable<string> paths)
            => paths.Select(p => new FileInfo(p)).Where(f => f.Exists).Sum(f => f.Length);

        /// <summary>Deletes the files and returns how many went.</summary>
        public static int Delete(IEnumerable<string> paths)
        {
            var list = paths.ToArray();
            if (list.Length == 0) return 0;
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(list, failed);
            return list.Length - failed.Count;
        }

        [MenuItem("Tools/DocMe/Delete Unused Generated Meshes")]
        static void MenuCleanup() => OfferCleanup();

        /// <summary>Looks for unused files, asks, deletes. Returns true when something was deleted.</summary>
        public static bool OfferCleanup()
        {
            List<string> unused;
            try
            {
                EditorUtility.DisplayProgressBar(L.Text("cleanup.title"), L.Text("cleanup.searching"), 0.5f);
                unused = FindUnused();
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog(L.Text("cleanup.title"), L.Text("cleanup.none"), L.Text("cleanup.ok"));
                return false;
            }
            string size = EditorUtility.FormatBytes(Bytes(unused));
            if (!EditorUtility.DisplayDialog(L.Text("cleanup.title"), L.Count(unused.Count, "cleanup.ask", unused.Count, size),
                                             L.Text("cleanup.delete"), L.Text("cleanup.keep")))
                return false;

            int deleted = Delete(unused);
            Debug.Log(L.Count(deleted, "cleanup.done", deleted, size) + "\n" + string.Join("\n", unused));
            return deleted > 0;
        }
    }
}
