using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DocMe.HideIslands.Tests
{
    public class GeneratedMeshesTests
    {
        const string Folder = "Assets/ZzDocMeGeneratedMeshesTest";
        string byPrefab, byClip, byRenderer, byRecord, unused;
        string prefabPath, clipPath;
        readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            byPrefab = MakeMesh("ByPrefab");
            byClip = MakeMesh("ByClip");
            byRenderer = MakeMesh("ByRenderer");
            byRecord = MakeMesh("ByRecord");
            unused = MakeMesh("Unused");

            // Unity saves no prefab from a DontSave object, and a plain object would dirty the open scene.
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("Prefab", HideFlags.HideAndDontSave, typeof(MeshFilter));
                SceneManager.MoveGameObjectToScene(go, preview);
                go.hideFlags = HideFlags.None;
                go.GetComponent<MeshFilter>().sharedMesh = Load(byPrefab);
                prefabPath = Folder + "/Holder.prefab";
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(go, prefabPath), "test prefab");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            var clip = new AnimationClip();
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SkinnedMeshRenderer), "m_Mesh");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[] { new ObjectReferenceKeyframe { time = 0f, value = Load(byClip) } });
            clipPath = Folder + "/Swap.anim";
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in objects) if (o != null) Object.DestroyImmediate(o);
            objects.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }

        string MakeMesh(string name)
        {
            var path = Folder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(new Mesh { name = name }, path);
            return path;
        }

        static Mesh Load(string path) => AssetDatabase.LoadAssetAtPath<Mesh>(path);

        GameObject Hidden(string name)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            objects.Add(go);
            return go;
        }

        List<string> Candidates => new List<string> { byPrefab, byClip, byRenderer, byRecord, unused };

        GameObject[] LoadedAvatar()
        {
            var avatar = Hidden("Avatar");
            var face = Hidden("Face");
            face.transform.SetParent(avatar.transform);
            face.AddComponent<SkinnedMeshRenderer>().sharedMesh = Load(byRenderer);
            var record = avatar.AddComponent<DocMeIslandHide>();
            record.entries.Add(new DocMeIslandHide.Entry { modifiedMesh = Load(byRecord) });
            return new[] { avatar };
        }

        [Test]
        public void OnlyTheMeshNothingUsesIsUnused()
        {
            var found = GeneratedMeshes.FindUnused(Candidates, new[] { prefabPath, clipPath }, LoadedAvatar());
            CollectionAssert.AreEqual(new[] { unused }, found);
        }

        [Test]
        public void AMeshOnlyAnInstallRecordHoldsIsKept()
        {
            var found = GeneratedMeshes.FindUnused(new[] { byRecord }, new string[0], LoadedAvatar());
            CollectionAssert.IsEmpty(found, "the record keeps the mesh a revert or a rebuild needs");
        }

        [Test]
        public void WithoutFilesOrObjectsEveryCandidateIsUnused()
        {
            var found = GeneratedMeshes.FindUnused(Candidates, new string[0], new GameObject[0]);
            CollectionAssert.AreEquivalent(Candidates, found);
        }

        [Test]
        public void AFileThatIsNotTextIsCheckedByUnity()
        {
            var junk = Folder + "/Junk.prefab";
            File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            try
            {
                var found = GeneratedMeshes.FindUnused(new[] { unused }, new[] { junk }, new GameObject[0]);
                CollectionAssert.AreEqual(new[] { unused }, found);
            }
            finally { File.Delete(junk); }
        }

        [Test]
        public void TheProjectScanCoversPrefabsAndClips()
        {
            var files = GeneratedMeshes.ProjectFiles().ToList();
            CollectionAssert.Contains(files, prefabPath);
            CollectionAssert.Contains(files, clipPath);
        }

        [Test]
        public void MeshFilesListsTheMeshesInAFolderOnly()
        {
            CollectionAssert.AreEquivalent(Candidates, GeneratedMeshes.MeshFiles(new[] { Folder, "Assets/ZzDocMeNoSuchFolder" }));
        }

        [Test]
        public void DeleteRemovesOnlyTheGivenFiles()
        {
            Assert.AreEqual(1, GeneratedMeshes.Delete(new[] { unused }));
            Assert.IsNull(Load(unused));
            Assert.IsNotNull(Load(byPrefab));
            Assert.AreEqual(0, GeneratedMeshes.Delete(new string[0]));
        }
    }
}
