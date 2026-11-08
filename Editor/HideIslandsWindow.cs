using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Standalone select-then-hide for any avatar mesh part (highlights, decorations). Same engine as
    /// the installer's Done page; the two can be used on the same avatar.</summary>
    public class HideIslandsWindow : EditorWindow
    {
        const string PK = "DocMe.IslandHide.";

        GameObject avatar;
        bool picking;
        bool advanced;
        string log = "";
        Vector2 scroll;
        readonly HidePickSession hides = new HidePickSession();
        readonly IslandPreview previews = new IslandPreview();

        [MenuItem("Tools/DocMe/Hide Mesh Islands")]
        static void Open()
        {
            var w = GetWindow<HideIslandsWindow>("Hide Islands");
            w.minSize = new Vector2(360, 300);
        }

        void OnEnable()
        {
            hides.deleteGeometry = EditorPrefs.GetBool(PK + "delete", false);
            hides.sinkIntoHead = EditorPrefs.GetBool(PK + "sink", true);
            if (avatar == null) avatar = LoadSavedAvatar() ?? AvatarFromSelection();
            OnAvatarSet();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            Save();
            SceneView.duringSceneGui -= OnSceneGUI;
            previews.Clear();
            picking = false;
        }

        void Save()
        {
            EditorPrefs.SetBool(PK + "delete", hides.deleteGeometry);
            EditorPrefs.SetBool(PK + "sink", hides.sinkIntoHead);
            EditorPrefs.SetString(PK + "avatar", avatar != null ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : "");
        }

        static GameObject LoadSavedAvatar()
        {
            string id = EditorPrefs.GetString(PK + "avatar", "");
            if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var gid)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as GameObject;
        }

        static GameObject AvatarFromSelection()
        {
            for (var p = Selection.activeTransform; p != null; p = p.parent)
            {
                var anim = p.GetComponent<Animator>();
                if (anim != null && anim.isHuman) return p.gameObject;
            }
            return null;
        }

        void OnAvatarSet()
        {
            hides.avatar = avatar;
            hides.Forget();
            previews.Clear();
            picking = false;
            log = "";
            if (avatar != null && hides.Count > 0) hides.Reload();
            Save();
            SceneView.RepaintAll();
        }

        static GUIContent GC(string key, string tipKey = null) => new GUIContent(L.Text(key), L.Tip(tipKey ?? key + ".tip"));

        GUIStyle _hint, _ok, _sel, _box;
        void EnsureStyles()
        {
            if (_box != null) return;
            _hint = new GUIStyle(EditorStyles.label) { wordWrap = true };
            _hint.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.3f, 0.3f, 0.3f);
            _ok = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _ok.normal.textColor = new Color(0.35f, 0.75f, 0.4f);
            _sel = new GUIStyle(EditorStyles.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _sel.normal.textColor = new Color(1f, 0.55f, 0.5f);
            _box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10) };
        }

        /// <summary>Runs a rebuild behind a progress bar: every rebuild writes the whole mesh asset.</summary>
        void Busy(string text, System.Action work)
        {
            EditorUtility.DisplayProgressBar(L.Text("window.title"), text, 0.4f);
            try { work(); }
            finally { EditorUtility.ClearProgressBar(); previews.Clear(); Repaint(); SceneView.RepaintAll(); }
        }

        void HideSelected()
        {
            int n = hides.PendingCount;
            if (n == 0) return;
            int done = 0;
            Busy(L.Count(n, "hide.busy.hiding", n), () => done = hides.HideSelected());
            log = done == n ? "" : L.Text("hide.notHidden", n - done);
        }

        void UndoLast()
        {
            if (hides.PendingCount > 0) hides.UndoLast();
            else Busy(L.Text("hide.busy.updating"), hides.UndoLast);
        }

        void OnGUI()
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L.Text("window.title"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
                if (DocMeLanguage.DrawPicker(L.Table.Languages())) Repaint();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            GUILayout.Label(L.Text("window.about"), _hint);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                avatar = (GameObject)EditorGUILayout.ObjectField(GC("avatar.field"), avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) OnAvatarSet();
                if (avatar == null)
                {
                    var fromSel = AvatarFromSelection();
                    if (fromSel != null && GUILayout.Button(L.Text("avatar.useSelected", fromSel.name), GUILayout.Height(24)))
                    { avatar = fromSel; OnAvatarSet(); }
                }

                EditorGUILayout.Space(6);
                int hidden = hides.Count, selected = hides.PendingCount;
                using (new EditorGUI.DisabledScope(avatar == null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.backgroundColor = picking ? new Color(0.6f, 0.8f, 1f) : Color.white;
                        if (GUILayout.Button(GC(picking ? "hide.selecting" : "hide.select", "hide.select.tip"), GUILayout.Height(26)))
                        { picking = !picking; SceneView.RepaintAll(); }
                        GUI.backgroundColor = selected > 0 ? new Color(0.6f, 0.9f, 0.65f) : Color.white;
                        using (new EditorGUI.DisabledScope(selected == 0))
                            if (GUILayout.Button(new GUIContent(selected == 0 ? L.Text("hide.button.none") : L.Count(selected, "hide.button", selected), L.Tip("hide.button.tip")), GUILayout.Height(26), GUILayout.Width(160)))
                                HideSelected();
                        GUI.backgroundColor = Color.white;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(hidden == 0 && selected == 0))
                            if (GUILayout.Button(GC("hide.undo"), GUILayout.Width(100), GUILayout.Height(22)))
                                UndoLast();
                        using (new EditorGUI.DisabledScope(hidden == 0))
                            if (GUILayout.Button(GC("hide.showAll"), GUILayout.Width(90), GUILayout.Height(22)))
                                Busy(L.Text("hide.busy.showAll"), hides.Clear);
                        GUILayout.FlexibleSpace();
                    }
                }
                if (picking)
                    GUILayout.Label(L.Text("hide.pickHelp"), _hint);
                if (selected > 0) GUILayout.Label(L.Count(selected, "hide.selected", selected), _sel);
                if (hidden > 0) GUILayout.Label(L.Count(hidden, "hide.hidden", hidden), _ok);
                if (!string.IsNullOrEmpty(log)) GUILayout.Label(log, _hint);
            }

            advanced = EditorGUILayout.Foldout(advanced, L.Text("advanced"), true);
            if (advanced)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                hides.deleteGeometry = EditorGUILayout.ToggleLeft(GC("hide.delete"), hides.deleteGeometry);
                using (new EditorGUI.DisabledScope(hides.deleteGeometry))
                    hides.sinkIntoHead = EditorGUILayout.ToggleLeft(GC("hide.fold"), hides.sinkIntoHead);
                if (EditorGUI.EndChangeCheck()) { Save(); if (hides.Count > 0) Busy(L.Text("hide.busy.updating"), hides.Reapply); }
                DocMeReapply.AutoEnabled = EditorGUILayout.ToggleLeft(GC("hide.autoRebuild"), DocMeReapply.AutoEnabled);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
        }

        void OnSceneGUI(SceneView sv)
        {
            if (avatar == null) return;
            var e = Event.current;

            if (e.type == EventType.Repaint)
            {
                var sel = new Color(1f, 0.35f, 0.35f);
                foreach (var p in hides.Pending) previews.Draw(previews.Get(p.renderer, p.vertex), sel, picking);
                if (picking)
                {
                    Handles.color = new Color(0.55f, 0.55f, 0.55f);
                    foreach (var p in hides.Picks)
                        Handles.SphereHandleCap(0, p.point, Quaternion.identity, HandleUtility.GetHandleSize(p.point) * 0.04f, EventType.Repaint);
                }
            }

            if (!picking) return;
            sv.Repaint();

            Handles.BeginGUI();
            GUI.Label(new Rect(8, 8, 620, 22), L.Text("hide.overlay", hides.PendingCount, hides.Count));
            Handles.EndGUI();
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { picking = false; e.Use(); Repaint(); return; }
                if (e.keyCode == KeyCode.Z && (e.control || e.command)) { UndoLast(); e.Use(); Repaint(); return; }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { HideSelected(); e.Use(); Repaint(); return; }
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (MeshPickUtil.Raycast(avatar, e.mousePosition, out var r, out int vert, out Vector3 pt, out string why))
                {
                    switch (hides.Select(new IslandPick { renderer = r, vertex = vert, point = pt }))
                    {
                        case HidePickSession.SelectResult.Selected:
                        case HidePickSession.SelectResult.Deselected: log = ""; break;
                        case HidePickSession.SelectResult.Protected: log = L.Text("hide.protected"); break;
                        case HidePickSession.SelectResult.TooBig: log = L.Text("hide.tooBig"); break;
                        default: log = L.Text("hide.unreadable"); break;
                    }
                }
                else log = why;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
    }
}
