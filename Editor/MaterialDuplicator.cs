// Material Duplicator
// Created with Claude Code (https://claude.ai/code)
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace MaterialDuplicatorTool
{

public class MaterialDuplicator : EditorWindow
{
    // ─ バージョン ─────────────────────────────────────────
    private const string Version      = "1.0.0";
    private const string PrefsKey     = "com.MaterialDuplicatorTool.presets_v1";

    // ─ データ ────────────────────────────────────────────
    private Material     sourceMaterial;
    private DefaultAsset materialDestAsset;
    private DefaultAsset textureDestAsset;
    private string       newMaterialFolderName = "";
    private string       newTextureFolderName  = "";
    private DefaultAsset parentBaseAsset;
    private string       newParentFolderName   = "";
    private string       newMaterialName       = "";

    private List<string> keywords        = new List<string> { "Albed", "AO", "Emission", "Metallic", "Normal", "Smoothness" };
    private List<string> excludeKeywords = new List<string>();
    private string       newKeyword        = "";
    private string       newExcludeKeyword = "";
    private List<string> logs = new List<string>();

    [System.Serializable]
    private class Preset
    {
        public string       name            = "";
        public List<string> keywords        = new List<string>();
        public List<string> excludeKeywords = new List<string>();
    }
    [System.Serializable]
    private class PresetList { public List<Preset> items = new List<Preset>(); }
    private List<Preset> presets       = new List<Preset>();
    private string       newPresetName = "";

    private struct PreviewEntry
    {
        public string  texName;
        public Texture tex;
        public bool    willProcess;
        public string  reason;
    }
    private List<PreviewEntry> previewEntries = new List<PreviewEntry>();

    // ─ タブ ──────────────────────────────────────────────
    private int  mainTab      = 0;
    private int  filterTab    = 0;
    private bool matchAllMode = false;
    private static readonly string[] mainTabLabels   = { "① 設定", "② フィルター" };
    private static readonly string[] filterTabLabels = { "複製対象", "プリセット", "除外" };

    // ─ スクロール ─────────────────────────────────────────
    private Vector2 tab1Scroll;
    private Vector2 tab2Scroll;
    private Vector2 keywordScroll;
    private Vector2 presetScroll;
    private Vector2 excludeScroll;
    private Vector2 previewScroll;
    private Vector2 logScroll;

    [MenuItem("Tools/Material Duplicator")]
    public static void Open() => GetWindow<MaterialDuplicator>("Material Duplicator  v" + Version);

    void OnEnable()  => LoadPresets();
    void OnDisable() => SavePresets();

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        int prevMainTab = mainTab;
        mainTab = GUILayout.Toolbar(mainTab, mainTabLabels, GUILayout.Height(28));
        if (mainTab != prevMainTab) GUI.FocusControl(null);
        EditorGUILayout.Space(6);
        switch (mainTab)
        {
            case 0: DrawTab1(); break;
            case 1: DrawTab2(); break;
        }
        DrawDivider();
        DrawBottomSection();
    }

    // ─ Tab ①: 設定 ───────────────────────────────────────
    private void DrawTab1()
    {
        tab1Scroll = EditorGUILayout.BeginScrollView(tab1Scroll, GUILayout.ExpandHeight(true));

        DrawSectionLabel("複製元マテリアル");
        EditorGUILayout.Space(4);
        sourceMaterial = (Material)EditorGUILayout.ObjectField(
            "マテリアル", sourceMaterial, typeof(Material), false);
        EditorGUILayout.Space(4);
        GUI.SetNextControlName("MaterialNameField");
        newMaterialName = EditorGUILayout.TextField(
            new GUIContent("複製後の名称", "空白の場合は元の名称をそのまま使用"),
            newMaterialName);
        EditorGUILayout.LabelField("　※ 空白の場合は元の名称をそのまま使用", EditorStyles.miniLabel);

        EditorGUILayout.Space(12);
        DrawSectionLabel("コピー先フォルダ");
        EditorGUILayout.Space(6);

        DrawSubSectionLabel("親フォルダ（任意）");
        var prevParent = parentBaseAsset;
        DrawFolderField("基準フォルダ", ref parentBaseAsset);
        if (parentBaseAsset != prevParent && parentBaseAsset != null)
        {
            materialDestAsset = parentBaseAsset;
            textureDestAsset  = parentBaseAsset;
        }
        DrawParentFolderRow();

        EditorGUILayout.Space(10);
        DrawSubSectionLabel("マテリアル保存先");
        DrawFolderField("マテリアル保存先", ref materialDestAsset);
        DrawNewFolderRow(materialDestAsset, ref newMaterialFolderName, ref materialDestAsset);

        EditorGUILayout.Space(8);
        DrawSubSectionLabel("テクスチャ保存先");
        DrawFolderField("テクスチャ保存先", ref textureDestAsset);
        DrawNewFolderRow(textureDestAsset, ref newTextureFolderName, ref textureDestAsset);

        EditorGUILayout.EndScrollView();
    }

    // ─ Tab ②: フィルター ─────────────────────────────────
    private void DrawTab2()
    {
        tab2Scroll = EditorGUILayout.BeginScrollView(tab2Scroll, GUILayout.ExpandHeight(true));

        var onStyle = new GUIStyle(GUI.skin.button);
        onStyle.normal.textColor = matchAllMode ? Color.black : GUI.skin.button.normal.textColor;
        onStyle.fontStyle        = matchAllMode ? FontStyle.Bold : FontStyle.Normal;
        var toggleColor = matchAllMode ? new Color(0.4f, 1f, 0.5f) : GUI.color;
        var prevColor   = GUI.color;
        GUI.color = toggleColor;
        if (GUILayout.Button(
            matchAllMode ? "★ 全テクスチャを複製対象（フィルター無効）" : "☆ 全テクスチャを複製対象にする",
            onStyle, GUILayout.Height(26)))
        {
            matchAllMode = !matchAllMode;
            previewEntries.Clear();
        }
        GUI.color = prevColor;

        EditorGUI.BeginDisabledGroup(matchAllMode);
        EditorGUILayout.Space(4);
        int prevFilterTab = filterTab;
        filterTab = GUILayout.Toolbar(filterTab, filterTabLabels, GUILayout.Height(24));
        if (filterTab != prevFilterTab) GUI.FocusControl(null);
        EditorGUILayout.Space(4);

        switch (filterTab)
        {
            case 0:
                DrawSectionLabel("複製対象テクスチャ文字列");
                EditorGUILayout.Space(4);
                GUI.SetNextControlName("KeywordScroll");
                DrawKeywordList(keywords, ref newKeyword, ref keywordScroll, "KeywordAdd");
                break;
            case 1:
                DrawSectionLabel("プリセット");
                EditorGUILayout.Space(4);
                DrawPresetContent();
                break;
            case 2:
                DrawSectionLabel("除外テクスチャ文字列");
                EditorGUILayout.Space(4);
                var c = GUI.color;
                GUI.color = new Color(1f, 0.7f, 0.7f);
                DrawKeywordList(excludeKeywords, ref newExcludeKeyword, ref excludeScroll, "ExcludeAdd");
                GUI.color = c;
                break;
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8);
        DrawDivider();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("複製対象プレビュー", EditorStyles.boldLabel);
        if (GUILayout.Button("更新", GUILayout.Width(50))) RefreshPreview();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
        DrawPreviewContent();

        EditorGUILayout.EndScrollView();
    }

    // ─ 共通下部 ───────────────────────────────────────────
    private void DrawBottomSection()
    {
        bool ready = sourceMaterial    != null
                  && materialDestAsset != null
                  && textureDestAsset  != null
                  && keywords.Count    > 0;

        EditorGUI.BeginDisabledGroup(!ready);
        if (GUILayout.Button("実行", GUILayout.Height(32))) { logs.Clear(); Execute(); }
        EditorGUI.EndDisabledGroup();

        if (!ready)
            EditorGUILayout.HelpBox(
                sourceMaterial    == null ? "マテリアルを指定してください（タブ①）"
              : materialDestAsset == null ? "マテリアル保存先を選択してください（タブ①）"
              : textureDestAsset  == null ? "テクスチャ保存先を選択してください（タブ①）"
              :                             "フィルター文字列を追加してください（タブ②）",
                MessageType.Warning);

        if (logs.Count > 0)
        {
            EditorGUILayout.Space(4);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(80));
            foreach (string line in logs)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(2);
        var creditStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        creditStyle.fontSize = 9;
        EditorGUILayout.LabelField(
            string.Format("Material Duplicator  v{0}  ·  Made with Claude Code", Version),
            creditStyle);
        EditorGUILayout.Space(2);
    }

    // ─ プリセット ─────────────────────────────────────────
    private void DrawPresetContent()
    {
        presetScroll = EditorGUILayout.BeginScrollView(presetScroll, GUILayout.Height(120));
        int removeAt = -1;
        for (int i = 0; i < presets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(presets[i].name, GUILayout.MinWidth(100));
            if (GUILayout.Button("読み込む", GUILayout.Width(70))) ApplyPreset(presets[i]);
            if (GUILayout.Button("削除",    GUILayout.Width(42))) removeAt = i;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if (removeAt >= 0) { presets.RemoveAt(removeAt); SavePresets(); }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.SetNextControlName("PresetNameField");
        newPresetName = EditorGUILayout.TextField("新規プリセット名", newPresetName);
        if (GUILayout.Button("保存", GUILayout.Width(48)))
        {
            string n = newPresetName.Trim();
            if (!string.IsNullOrEmpty(n))
            {
                var ex = presets.Find(p => p.name == n);
                if (ex != null) { ex.keywords = new List<string>(keywords); ex.excludeKeywords = new List<string>(excludeKeywords); }
                else presets.Add(new Preset { name = n, keywords = new List<string>(keywords), excludeKeywords = new List<string>(excludeKeywords) });
                SavePresets(); newPresetName = ""; GUI.FocusControl(null);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ─ キーワードリスト ───────────────────────────────────
    private void DrawKeywordList(List<string> list, ref string newEntry, ref Vector2 scroll, string fieldName)
    {
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            list[i] = EditorGUILayout.TextField(list[i]);
            if (GUILayout.Button("✕", GUILayout.Width(28))) { list.RemoveAt(i); break; }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.SetNextControlName(fieldName);
        newEntry = EditorGUILayout.TextField("追加", newEntry);
        if (GUILayout.Button("追加", GUILayout.Width(50)))
        {
            string t = newEntry.Trim();
            if (!string.IsNullOrEmpty(t) && !list.Contains(t))
            { list.Add(t); newEntry = ""; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndHorizontal();
    }

    // ─ プレビュー ─────────────────────────────────────────
    private void DrawPreviewContent()
    {
        if (previewEntries.Count == 0)
        { EditorGUILayout.HelpBox("「更新」ボタンでプレビューを表示", MessageType.None); return; }

        int targetCount = previewEntries.FindAll(e => e.willProcess).Count;
        EditorGUILayout.LabelField(
            string.Format("全 {0} 件 / 複製対象 {1} 件", previewEntries.Count, targetCount),
            EditorStyles.miniLabel);

        const int thumbSize = 68;
        bool needRepaint = false;
        previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(120));
        EditorGUILayout.BeginHorizontal();
        foreach (var entry in previewEntries)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(thumbSize + 6));
            Texture2D thumb = AssetPreview.GetAssetPreview(entry.tex);
            if (thumb == null && AssetPreview.IsLoadingAssetPreview(entry.tex.GetInstanceID()))
                needRepaint = true;
            Rect rect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
            Color frame = entry.willProcess ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.45f, 0.45f, 0.45f);
            EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, thumbSize + 4, thumbSize + 4), frame);
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            if (thumb != null) GUI.DrawTexture(rect, thumb, ScaleMode.ScaleToFit);
            else if (entry.tex is Texture2D t2d) GUI.DrawTexture(rect, t2d, ScaleMode.ScaleToFit);
            EditorGUILayout.LabelField(entry.texName, EditorStyles.miniLabel, GUILayout.Width(thumbSize + 6));
            var prev = GUI.color;
            GUI.color = entry.willProcess ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
            EditorGUILayout.LabelField(entry.reason, EditorStyles.miniLabel, GUILayout.Width(thumbSize + 6));
            GUI.color = prev;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();
        if (needRepaint) Repaint();
    }

    // ─ フォルダ選択 ───────────────────────────────────────
    private void DrawFolderField(string label, ref DefaultAsset asset)
    {
        EditorGUILayout.BeginHorizontal();
        var sel = (DefaultAsset)EditorGUILayout.ObjectField(label, asset, typeof(DefaultAsset), false);
        if (sel != asset)
        {
            if (sel == null) asset = null;
            else
            {
                string p = AssetDatabase.GetAssetPath(sel);
                if (AssetDatabase.IsValidFolder(p)) asset = sel;
                else EditorUtility.DisplayDialog("エラー", "フォルダを指定してください", "OK");
            }
        }
        if (GUILayout.Button("選択", GUILayout.Width(48)))
        {
            string raw = EditorUtility.OpenFolderPanel(label, "Assets", "");
            if (!string.IsNullOrEmpty(raw) && raw.StartsWith(Application.dataPath))
                asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets" + raw.Substring(Application.dataPath.Length));
            else if (!string.IsNullOrEmpty(raw))
                EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください", "OK");
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawParentFolderRow()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        string basePath = parentBaseAsset != null ? AssetDatabase.GetAssetPath(parentBaseAsset) : "";
        EditorGUILayout.LabelField(
            parentBaseAsset != null ? basePath + "/" : "（基準フォルダ未選択）/", EditorStyles.miniLabel);
        newParentFolderName = EditorGUILayout.TextField(newParentFolderName, GUILayout.Width(120));
        EditorGUI.BeginDisabledGroup(parentBaseAsset == null || string.IsNullOrEmpty(newParentFolderName.Trim()));
        if (GUILayout.Button("フォルダ作成", GUILayout.Width(80)))
        {
            string guid = AssetDatabase.CreateFolder(basePath, newParentFolderName.Trim());
            if (!string.IsNullOrEmpty(guid))
            {
                var created = AssetDatabase.LoadAssetAtPath<DefaultAsset>(AssetDatabase.GUIDToAssetPath(guid));
                materialDestAsset = created;
                textureDestAsset  = created;
            }
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawNewFolderRow(DefaultAsset parent, ref string folderName, ref DefaultAsset assignTarget)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        string parentPath = parent != null ? AssetDatabase.GetAssetPath(parent) : "";
        EditorGUILayout.LabelField(
            parent != null ? parentPath + "/" : "（親フォルダ未選択）/", EditorStyles.miniLabel);
        folderName = EditorGUILayout.TextField(folderName, GUILayout.Width(120));
        EditorGUI.BeginDisabledGroup(parent == null || string.IsNullOrEmpty(folderName.Trim()));
        if (GUILayout.Button("フォルダ作成", GUILayout.Width(80)))
        {
            string guid = AssetDatabase.CreateFolder(parentPath, folderName.Trim());
            if (!string.IsNullOrEmpty(guid))
                assignTarget = AssetDatabase.LoadAssetAtPath<DefaultAsset>(AssetDatabase.GUIDToAssetPath(guid));
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    // ─ ヘルパー ───────────────────────────────────────────
    private void DrawSectionLabel(string title)
    {
        Rect r = EditorGUILayout.GetControlRect(false, 22);
        EditorGUI.DrawRect(r, new Color(0.25f, 0.25f, 0.25f));
        EditorGUI.LabelField(r, "  " + title, EditorStyles.boldLabel);
    }

    private void DrawSubSectionLabel(string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private void RefreshPreview()
    {
        previewEntries.Clear();
        if (sourceMaterial == null) return;
        foreach (string prop in sourceMaterial.GetTexturePropertyNames())
        {
            Texture tex = sourceMaterial.GetTexture(prop);
            if (tex == null) continue;
            bool isBuiltin = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex));
            bool inc  = !isBuiltin && MatchesInclude(tex.name);
            bool exc  = MatchesExclude(tex.name);
            bool will = inc && !exc;
            previewEntries.Add(new PreviewEntry
            {
                texName     = tex.name,
                tex         = tex,
                willProcess = will,
                reason      = isBuiltin ? "ビルトイン（スキップ）"
                            : !inc      ? "フィルター不一致"
                            : exc       ? "除外キーワード一致"
                            :             "複製対象"
            });
        }
    }

    private void ApplyPreset(Preset p)
    {
        keywords.Clear();        keywords.AddRange(p.keywords);
        excludeKeywords.Clear(); excludeKeywords.AddRange(p.excludeKeywords);
        previewEntries.Clear();
    }

    private void SavePresets() =>
        EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new PresetList { items = presets }));

    private void LoadPresets()
    {
        string json = EditorPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return;
        try { var l = JsonUtility.FromJson<PresetList>(json); if (l?.items != null && l.items.Count > 0) presets = l.items; }
        catch { }
    }

    private bool MatchesInclude(string name)
    {
        if (matchAllMode) return true;
        foreach (string kw in keywords)
            if (name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private bool MatchesExclude(string name)
    {
        foreach (string kw in excludeKeywords)
            if (name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private void Execute()
    {
        string matDest  = AssetDatabase.GetAssetPath(materialDestAsset);
        string texDest  = AssetDatabase.GetAssetPath(textureDestAsset);
        string srcMat   = AssetDatabase.GetAssetPath(sourceMaterial);
        string matExt   = Path.GetExtension(srcMat);
        string matBase  = string.IsNullOrWhiteSpace(newMaterialName)
                        ? Path.GetFileNameWithoutExtension(srcMat)
                        : newMaterialName.Trim();
        string dstMat   = (matDest + "/" + matBase + matExt).Replace("\\", "/");

        if (srcMat == dstMat)
        { EditorUtility.DisplayDialog("エラー", "コピー元とコピー先が同じフォルダです", "OK"); return; }
        if (!TryCopyMaterial(srcMat, dstMat)) return;

        Material newMat = AssetDatabase.LoadAssetAtPath<Material>(dstMat);
        if (newMat == null) { Log("ERROR: 複製マテリアルの読み込み失敗"); return; }

        int copied = 0, skipped = 0;
        foreach (string prop in newMat.GetTexturePropertyNames())
        {
            Texture tex = sourceMaterial.GetTexture(prop);
            if (tex == null) continue;
            string srcTex = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(srcTex))
            { Log(string.Format("SKIP  [{0}] {1} (ビルトイン)", prop, tex.name)); skipped++; continue; }
            if (!MatchesInclude(tex.name) || MatchesExclude(tex.name))
            { Log(string.Format("SKIP  [{0}] {1}", prop, tex.name)); skipped++; continue; }
            string dstTex = (texDest + "/" + Path.GetFileName(srcTex)).Replace("\\", "/");
            TryCopyTexture(srcTex, dstTex);
            Texture newTex = AssetDatabase.LoadAssetAtPath<Texture>(dstTex);
            if (newTex != null)
            { newMat.SetTexture(prop, newTex); Log(string.Format("OK    [{0}] {1}", prop, tex.name)); copied++; }
        }

        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        string summary = string.Format("完了：テクスチャ {0} 件複製、{1} 件スキップ", copied, skipped);
        Log(summary); EditorUtility.DisplayDialog("完了", summary, "OK");
    }

    private bool TryCopyMaterial(string src, string dst)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(dst) != null)
        { Log("INFO  既存マテリアルを使用: " + Path.GetFileName(dst)); return true; }
        bool ok = AssetDatabase.CopyAsset(src, dst);
        if (!ok) Log("ERROR コピー失敗: " + src);
        return ok;
    }

    private void TryCopyTexture(string src, string dst)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(dst) != null)
        { Log("INFO  既存ファイルを使用: " + Path.GetFileName(dst)); return; }
        if (!AssetDatabase.CopyAsset(src, dst)) Log("ERROR コピー失敗: " + src);
    }

    private void Log(string msg) { logs.Add(msg); Debug.Log("[MaterialDuplicator] " + msg); }

    private void DrawDivider()
    {
        EditorGUILayout.Space(4);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorGUILayout.Space(4);
    }
}

} // namespace MaterialDuplicatorTool