using UnityEditor;
using UnityEngine;

namespace DocMe.HideIslands
{
    /// <summary>Standalone select-then-hide for any avatar mesh part (highlights, decorations). Same engine as
    /// the installer's Done page; the two can be used on the same avatar.</summary>
    public class HideIslandsWindow : EditorWindow
    {
        const string PK = "DocMe.IslandHide.";
        const string LangKey = "DocMe.EyeInstaller.japanese";

        GameObject avatar;
        bool japanese;
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
            japanese = EditorPrefs.GetBool(LangKey, false);
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
            EditorPrefs.SetBool(LangKey, japanese);
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

        string L(string en, string jp) => japanese ? jp : en;
        GUIContent GC(string en, string jp, string tEn = null, string tJp = null)
            => new GUIContent(L(en, jp), tEn == null ? null : L(tEn, tJp));

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
        void Busy(string en, string jp, System.Action work)
        {
            EditorUtility.DisplayProgressBar("DocMe Eyes", L(en, jp), 0.4f);
            try { work(); }
            finally { EditorUtility.ClearProgressBar(); previews.Clear(); Repaint(); SceneView.RepaintAll(); }
        }

        void HideSelected()
        {
            int n = hides.PendingCount;
            if (n == 0) return;
            int done = 0;
            Busy(n == 1 ? "Hiding 1 part…" : $"Hiding {n} parts…", $"{n} 箇所を隠しています…", () => done = hides.HideSelected());
            log = done == n ? "" : L($"{n - done} of the selected parts could not be hidden.", $"選択した {n - done} 箇所は隠せませんでした。");
        }

        void UndoLast()
        {
            if (hides.PendingCount > 0) hides.UndoLast();
            else Busy("Updating…", "更新中…", hides.UndoLast);
        }

        void OnGUI()
        {
            EnsureStyles();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L("Hide Mesh Islands", "メッシュの一部を非表示"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
                int cur = japanese ? 1 : 0;
                int sel = GUILayout.Toolbar(cur, new[] { "EN", "日本語" }, GUILayout.Width(110), GUILayout.Height(20));
                if (sel != cur) { japanese = sel == 1; Save(); GUIUtility.keyboardControl = 0; }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            GUILayout.Label(L("Hide any small part of a mesh: a highlight, a sticker, a decoration. Select the parts in the Scene view, then hide them all at once. Nothing is lost; Show all brings everything back.",
                              "メッシュの小さな部分（ハイライト、シール、飾りなど）を隠します。シーンで部分を選んでから、まとめて隠します。消えたものは「全て表示」で戻せます。"), _hint);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(_box))
            {
                EditorGUI.BeginChangeCheck();
                avatar = (GameObject)EditorGUILayout.ObjectField(GC("Avatar", "アバター"), avatar, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) OnAvatarSet();
                if (avatar == null)
                {
                    var fromSel = AvatarFromSelection();
                    if (fromSel != null && GUILayout.Button(L($"Use selected: {fromSel.name}", $"選択中の「{fromSel.name}」を使う"), GUILayout.Height(24)))
                    { avatar = fromSel; OnAvatarSet(); }
                }

                EditorGUILayout.Space(6);
                int hidden = hides.Count, selected = hides.PendingCount;
                using (new EditorGUI.DisabledScope(avatar == null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.backgroundColor = picking ? new Color(0.6f, 0.8f, 1f) : Color.white;
                        if (GUILayout.Button(GC(picking ? "Selecting… press to stop" : "Select parts to hide", picking ? "選択中… 押すと終了" : "隠す部分を選択",
                                                "Click parts in the Scene view to select them; click a selected part again to unselect it.",
                                                "シーンで部分をクリックして選びます。選んだ部分をもう一度クリックすると外れます。"), GUILayout.Height(26)))
                        { picking = !picking; SceneView.RepaintAll(); }
                        GUI.backgroundColor = selected > 0 ? new Color(0.6f, 0.9f, 0.65f) : Color.white;
                        using (new EditorGUI.DisabledScope(selected == 0))
                            if (GUILayout.Button(GC(selected == 0 ? "Hide selected parts" : selected == 1 ? "Hide 1 selected part" : $"Hide {selected} selected parts", selected == 0 ? "選択した部分を隠す" : $"選択した {selected} 箇所を隠す",
                                                    "Hides all selected parts in one go. Enter in the Scene view does the same.",
                                                    "選択した部分をまとめて隠します。シーンで Enter を押しても同じです。"), GUILayout.Height(26), GUILayout.Width(160)))
                                HideSelected();
                        GUI.backgroundColor = Color.white;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(hidden == 0 && selected == 0))
                            if (GUILayout.Button(GC("Undo last", "最後を取り消す",
                                                    "Takes back the last selected part, or the last hidden part when nothing is selected.",
                                                    "最後に選んだ部分を外します。何も選んでいなければ最後に隠した部分を戻します。"), GUILayout.Width(100), GUILayout.Height(22)))
                                UndoLast();
                        using (new EditorGUI.DisabledScope(hidden == 0))
                            if (GUILayout.Button(GC("Show all", "全て表示", "Brings every hidden part back.", "隠した部分をすべて戻します。"), GUILayout.Width(90), GUILayout.Height(22)))
                                Busy("Showing all…", "すべて表示中…", hides.Clear);
                        GUILayout.FlexibleSpace();
                    }
                }
                if (picking)
                    GUILayout.Label(L("Click parts in the Scene view. Enter hides the selection, Esc stops, Ctrl+Z takes back the last one.",
                                      "シーンで部分をクリックしてください。Enter で選択を隠す、Esc で終了、Ctrl+Z で最後を取り消し。"), _hint);
                if (selected > 0) GUILayout.Label(L(selected == 1 ? "1 part selected. Press Hide." : $"{selected} parts selected. Press Hide.", $"{selected} 箇所を選択中。「隠す」を押してください。"), _sel);
                if (hidden > 0) GUILayout.Label(L(hidden == 1 ? "1 part hidden." : $"{hidden} parts hidden.", $"{hidden} 箇所を隠しました。"), _ok);
                if (!string.IsNullOrEmpty(log)) GUILayout.Label(log, _hint);
            }

            advanced = EditorGUILayout.Foldout(advanced, L("Advanced", "詳細設定"), true);
            if (advanced)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                hides.deleteGeometry = EditorGUILayout.ToggleLeft(GC("Delete hidden parts instead of folding them", "隠す部分を畳まずに削除",
                    "Folding uses a blendshape. Deleting removes the triangles, which stops other blendshapes from pulling them back into view.",
                    "畳む方法はブレンドシェイプです。削除はポリゴンを消すので、他のブレンドシェイプで再表示されなくなります。"), hides.deleteGeometry);
                using (new EditorGUI.DisabledScope(hides.deleteGeometry))
                    hides.sinkIntoHead = EditorGUILayout.ToggleLeft(GC("Fold hidden parts into the head", "隠す部分を頭の中に畳み込む"), hides.sinkIntoHead);
                if (EditorGUI.EndChangeCheck()) { Save(); if (hides.Count > 0) Busy("Updating…", "更新中…", hides.Reapply); }
                DocMeReapply.AutoEnabled = EditorGUILayout.ToggleLeft(GC("Rebuild automatically when the model is reimported", "モデルの再インポート時に自動で作り直す"), DocMeReapply.AutoEnabled);
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
            GUI.Label(new Rect(8, 8, 620, 22), L($"Click parts to select them ({hides.PendingCount} selected, {hides.Count} hidden). Enter hides them, Esc stops, Ctrl+Z takes back the last.",
                                                  $"隠す部分をクリックして選択（{hides.PendingCount} 選択、{hides.Count} 非表示）。Enter で隠す、Esc で終了、Ctrl+Z で最後を取り消し。"));
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
                        case HidePickSession.SelectResult.Protected: log = L("Another DocMe tool uses that part, so it stays.", "その部分は別の DocMe ツールが使っているので隠しません。"); break;
                        case HidePickSession.SelectResult.TooBig: log = L("That part is too big to hide. Click a small piece such as a highlight.", "その部分は大きすぎて隠せません。ハイライトなど小さな部分をクリックしてください。"); break;
                        default: log = L("That mesh cannot be read.", "そのメッシュは読み取れません。"); break;
                    }
                }
                else log = why;
                e.Use(); Repaint(); SceneView.RepaintAll();
            }
        }
    }
}
