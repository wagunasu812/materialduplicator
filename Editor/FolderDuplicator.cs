// Folder Duplicator
// Created with Claude Code (https://claude.ai/code)
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace MaterialDuplicatorTool
{

public class FolderDuplicator : EditorWindow
{
    private const string Version = "1.1.2";

    private DefaultAsset sourceFolderAsset;

    // 複製先フォルダ構造モード
    private bool         useDirectMode   = false;  // 直接指定モード
    private DefaultAsset baseFolderAsset;           // 複製先フォルダ（ベース）
    private string       parentFolderName = "";     // 命名可能な親フォルダ名
    private DefaultAsset parentFolderCreated;       // 作成された親フォルダ
    private string       destFolderName   = "";     // 実際の複製先フォルダ名
    private DefaultAsset destFolderAsset;           // 最終的な複製先（実行に使う）

    private List<string> logs     = new List<string>();
    private Vector2      logScroll;

    private static readonly HashSet<string> TextureExts = new HashSet<string>(
        System.StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tiff", ".tif", ".gif", ".bmp", ".hdr", ".exr" };

    [MenuItem("Tools/Folder Duplicator")]
    public static void Open() => GetWindow<FolderDuplicator>("Folder Duplicator  v" + Version);

    void OnGUI()
    {
        EditorGUILayout.Space(8);

        // ── 警告ボックス ──────────────────────────────────────────
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1.0f, 0.85f, 0.3f, 1f);
        GUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;
        GUILayout.Label("⚠  複製されるのは マテリアル と テクスチャ のみです", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        GUILayout.Label(
            "・メッシュ / プレハブ / アニメーション / スクリプト などはコピーされません\n" +
            "・複製先に同名ファイルが存在する場合は上書きせずスキップします\n" +
            "・アセット数が多い場合、実行に時間がかかることがあります\n" +
            "・実行前に必ず複製先フォルダの内容を確認してください",
            EditorStyles.wordWrappedMiniLabel);
        GUILayout.EndVertical();
        EditorGUILayout.Space(10);

        // ── 複製元 ────────────────────────────────────────────────
        DrawSectionLabel("複製元フォルダ");
        EditorGUILayout.Space(4);
        DrawFolderField("複製元", ref sourceFolderAsset);

        EditorGUILayout.Space(12);

        // ── 複製先 ────────────────────────────────────────────────
        DrawSectionLabel("複製先フォルダ");
        EditorGUILayout.Space(4);

        // モード切替
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        bool newMode = GUILayout.Toggle(useDirectMode, " 直接指定モード", "Button", GUILayout.Width(120));
        if (newMode != useDirectMode) useDirectMode = newMode;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(6);

        if (useDirectMode)
        {
            DrawDirectMode();
        }
        else
        {
            DrawStructureMode();
        }

        GUILayout.FlexibleSpace();
        DrawDivider();

        // ── 実行ボタン ────────────────────────────────────────────
        bool ready = sourceFolderAsset != null && destFolderAsset != null;
        EditorGUI.BeginDisabledGroup(!ready);
        if (GUILayout.Button("実行", GUILayout.Height(36))) { logs.Clear(); Execute(); }
        EditorGUI.EndDisabledGroup();

        if (!ready)
            EditorGUILayout.HelpBox(
                sourceFolderAsset == null ? "複製元フォルダを指定してください"
                                          : "複製先フォルダを指定してください",
                MessageType.Warning);

        if (logs.Count > 0)
        {
            EditorGUILayout.Space(4);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(140));
            foreach (string line in logs)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(2);
        var creditStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9 };
        EditorGUILayout.LabelField(
            string.Format("Folder Duplicator  v{0}  ·  Made with Claude Code", Version),
            creditStyle);
        EditorGUILayout.Space(2);
    }

    // ─ 直接指定モード UI ─────────────────────────────────────────
    private void DrawDirectMode()
    {
        DrawFolderField("複製先", ref destFolderAsset);

        // 複製先の中にサブフォルダを作成するオプション
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        string destPath = destFolderAsset != null ? AssetDatabase.GetAssetPath(destFolderAsset) : "";
        EditorGUILayout.LabelField(
            destFolderAsset != null ? destPath + "/" : "（複製先未選択）/",
            EditorStyles.miniLabel);
        destFolderName = EditorGUILayout.TextField(destFolderName, GUILayout.Width(120));
        EditorGUI.BeginDisabledGroup(destFolderAsset == null || string.IsNullOrEmpty(destFolderName.Trim()));
        if (GUILayout.Button("サブ作成", GUILayout.Width(68)))
        {
            string guid = AssetDatabase.CreateFolder(destPath, destFolderName.Trim());
            if (!string.IsNullOrEmpty(guid))
                destFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    // ─ フォルダ構造モード UI ──────────────────────────────────────
    //   複製先フォルダ
    //     └ 命名可能な親フォルダ
    //          └ 実際の複製先フォルダ  ← destFolderAsset
    private void DrawStructureMode()
    {
        // Level 1: ベースフォルダ（複製先フォルダ）
        DrawFolderField("複製先フォルダ", ref baseFolderAsset);

        EditorGUILayout.Space(4);

        // Level 2: 命名可能な親フォルダ
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(12);
        GUILayout.Label("└", GUILayout.Width(14));
        string basePath = baseFolderAsset != null ? AssetDatabase.GetAssetPath(baseFolderAsset) : "";
        EditorGUILayout.LabelField(
            baseFolderAsset != null ? basePath + "/" : "（未選択）/",
            EditorStyles.miniLabel, GUILayout.MaxWidth(200));
        parentFolderName = EditorGUILayout.TextField(parentFolderName, GUILayout.ExpandWidth(true));
        bool canCreateParent = baseFolderAsset != null && !string.IsNullOrEmpty(parentFolderName.Trim());
        EditorGUI.BeginDisabledGroup(!canCreateParent);
        if (GUILayout.Button("作成", GUILayout.Width(48)))
        {
            string guid = AssetDatabase.CreateFolder(basePath, parentFolderName.Trim());
            if (!string.IsNullOrEmpty(guid))
            {
                parentFolderCreated = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                destFolderAsset = null; // 親が変わったのでリセット
            }
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        // 親フォルダ確認表示
        if (parentFolderCreated != null)
        {
            string pp = AssetDatabase.GetAssetPath(parentFolderCreated);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("✓ " + pp, EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);

        // Level 3: 実際の複製先フォルダ
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(24);
        GUILayout.Label("└", GUILayout.Width(14));
        string parentPath = parentFolderCreated != null ? AssetDatabase.GetAssetPath(parentFolderCreated) : "";
        EditorGUILayout.LabelField(
            parentFolderCreated != null ? parentPath + "/" : "（親フォルダ未作成）/",
            EditorStyles.miniLabel, GUILayout.MaxWidth(200));
        destFolderName = EditorGUILayout.TextField(destFolderName, GUILayout.ExpandWidth(true));
        bool canCreateDest = parentFolderCreated != null && !string.IsNullOrEmpty(destFolderName.Trim());
        EditorGUI.BeginDisabledGroup(!canCreateDest);
        if (GUILayout.Button("作成", GUILayout.Width(48)))
        {
            string guid = AssetDatabase.CreateFolder(parentPath, destFolderName.Trim());
            if (!string.IsNullOrEmpty(guid))
                destFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        // 最終的な複製先パス表示
        EditorGUILayout.Space(6);
        if (destFolderAsset != null)
        {
            string fp = AssetDatabase.GetAssetPath(destFolderAsset);
            EditorGUILayout.HelpBox("複製先: " + fp, MessageType.Info);
        }
        else
        {
            string hint = "複製先フォルダ  /  親フォルダ名  /  複製先フォルダ名  の順に作成してください";
            EditorGUILayout.HelpBox(hint, MessageType.None);
        }
    }

    // ─ 実行処理 ──────────────────────────────────────────────
    private void Execute()
    {
        string srcRoot = AssetDatabase.GetAssetPath(sourceFolderAsset);
        string dstRoot = AssetDatabase.GetAssetPath(destFolderAsset);

        if (srcRoot == dstRoot)
        { EditorUtility.DisplayDialog("エラー", "複製元と複製先が同じフォルダです", "OK"); return; }

        if (dstRoot.StartsWith(srcRoot + "/"))
        { EditorUtility.DisplayDialog("エラー", "複製先が複製元の子フォルダになっています", "OK"); return; }

        var texMap = new Dictionary<string, string>();
        int copiedTex = 0, skippedTex = 0;

        string[] texGuids = AssetDatabase.FindAssets("t:Texture", new[] { srcRoot });
        foreach (string guid in texGuids)
        {
            string srcPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!TextureExts.Contains(Path.GetExtension(srcPath))) continue;

            string dstPath = ToDstPath(srcPath, srcRoot, dstRoot);
            EnsureFolderExists(Path.GetDirectoryName(dstPath).Replace("\\", "/"));
            texMap[srcPath] = dstPath;

            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            { Log("SKIP  [Tex] " + Path.GetFileName(dstPath)); skippedTex++; }
            else if (AssetDatabase.CopyAsset(srcPath, dstPath))
            { Log("OK    [Tex] " + Path.GetFileName(dstPath)); copiedTex++; }
            else
            { Log("ERROR [Tex] " + srcPath); }
        }

        AssetDatabase.Refresh();

        // Phase 2: マテリアルをコピー（再アサインは後で行う）
        int copiedMat = 0, skippedMat = 0;
        var matPairs = new List<(string srcPath, string dstPath)>();

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { srcRoot });
        foreach (string guid in matGuids)
        {
            string srcPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.LoadAssetAtPath<Material>(srcPath) == null) continue;

            string dstPath = ToDstPath(srcPath, srcRoot, dstRoot);
            EnsureFolderExists(Path.GetDirectoryName(dstPath).Replace("\\", "/"));

            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            { Log("SKIP  [Mat] " + Path.GetFileName(dstPath)); skippedMat++; }
            else if (AssetDatabase.CopyAsset(srcPath, dstPath))
            { Log("OK    [Mat] " + Path.GetFileName(dstPath)); copiedMat++; }
            else
            { Log("ERROR [Mat] " + srcPath); continue; }

            matPairs.Add((srcPath, dstPath));
        }

        // コピー済みマテリアルが全てインポートされてから再アサイン
        AssetDatabase.Refresh();

        // Phase 3: テクスチャ再アサイン
        foreach (var (srcPath, dstPath) in matPairs)
        {
            var srcMat = AssetDatabase.LoadAssetAtPath<Material>(srcPath);
            var dstMat = AssetDatabase.LoadAssetAtPath<Material>(dstPath);
            if (srcMat != null && dstMat != null)
                ReassignTextures(dstMat, srcMat, texMap);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = string.Format(
            "完了：テクスチャ {0} 件複製 / {1} 件スキップ、マテリアル {2} 件複製 / {3} 件スキップ",
            copiedTex, skippedTex, copiedMat, skippedMat);
        Log(summary);
        EditorUtility.DisplayDialog("完了", summary, "OK");
    }

    private void ReassignTextures(Material dstMat, Material srcMat, Dictionary<string, string> map)
    {
        bool changed = false;
        foreach (string prop in srcMat.GetTexturePropertyNames())
        {
            Texture tex = srcMat.GetTexture(prop);
            if (tex == null) continue;
            string srcTexPath = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(srcTexPath)) continue;
            if (map.TryGetValue(srcTexPath, out string dstTexPath))
            {
                Texture newTex = AssetDatabase.LoadAssetAtPath<Texture>(dstTexPath);
                if (newTex != null) { dstMat.SetTexture(prop, newTex); changed = true; }
            }
        }
        if (changed) EditorUtility.SetDirty(dstMat);
    }

    private static string ToDstPath(string srcPath, string srcRoot, string dstRoot)
        => (dstRoot + srcPath.Substring(srcRoot.Length)).Replace("\\", "/");

    private void EnsureFolderExists(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string name   = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return;
        EnsureFolderExists(parent);
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }

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
                asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    "Assets" + raw.Substring(Application.dataPath.Length));
            else if (!string.IsNullOrEmpty(raw))
                EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください", "OK");
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSectionLabel(string title)
    {
        Rect r = EditorGUILayout.GetControlRect(false, 22);
        EditorGUI.DrawRect(r, new Color(0.25f, 0.25f, 0.25f));
        EditorGUI.LabelField(r, "  " + title, EditorStyles.boldLabel);
    }

    private void DrawDivider()
    {
        EditorGUILayout.Space(4);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorGUILayout.Space(4);
    }

    private void Log(string msg) { logs.Add(msg); Debug.Log("[FolderDuplicator] " + msg); }
}

} // namespace MaterialDuplicatorTool