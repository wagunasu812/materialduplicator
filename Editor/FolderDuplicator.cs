// Folder Duplicator
// Created with Claude Code (https://claude.ai/code)
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace MaterialDuplicatorTool
{

public class FolderDuplicator : EditorWindow
{
    private const string Version = "1.2.0";

    private DefaultAsset sourceFolderAsset;

    // 複製先フォルダ構造モード
    private bool         useDirectMode   = false;  // 直接指定モード
    private DefaultAsset baseFolderAsset;           // 複製先フォルダ（ベース）
    private string       parentFolderName = "";     // 命名可能な親フォルダ名
    private DefaultAsset parentFolderCreated;       // 作成された親フォルダ
    private string       destFolderName   = "";     // 実際の複製先フォルダ名
    private DefaultAsset destFolderAsset;           // 最終的な複製先（実行に使う）

    // Prefab 複製 / FBX 差し替え
    private class FbxPair { public Object src; public Object dst; }
    private bool          copyPrefabs       = true;
    private bool          rebakeConstraints = true;
    private bool          destPoseWins      = true;
    private List<FbxPair> fbxPairs          = new List<FbxPair>();

    private List<string> logs     = new List<string>();
    private Vector2      logScroll;
    private Vector2      mainScroll;

    private static readonly HashSet<string> TextureExts = new HashSet<string>(
        System.StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tiff", ".tif", ".gif", ".bmp", ".hdr", ".exr" };

    private static readonly HashSet<string> ModelExts = new HashSet<string>(
        System.StringComparer.OrdinalIgnoreCase)
        { ".fbx", ".obj", ".dae", ".blend", ".max", ".ma", ".mb" };

    [MenuItem("Tools/Folder Duplicator")]
    public static void Open()
    {
        var w = GetWindow<FolderDuplicator>("Folder Duplicator");
        w.minSize = new Vector2(400f, 520f);
    }

    // ─ 配色とスタイル ─────────────────────────────────────────────
    private static bool Pro { get { return EditorGUIUtility.isProSkin; } }

    private static readonly Color CBlue   = new Color(0.28f, 0.55f, 0.85f); // 複製元
    private static readonly Color CTeal   = new Color(0.18f, 0.64f, 0.58f); // 複製先
    private static readonly Color CPurple = new Color(0.54f, 0.44f, 0.78f); // オプション
    private static readonly Color CGreen  = new Color(0.33f, 0.72f, 0.42f);
    private static readonly Color CAmber  = new Color(0.92f, 0.70f, 0.22f);
    private static readonly Color CRed    = new Color(0.86f, 0.36f, 0.33f);
    private static readonly Color CGray   = new Color(0.55f, 0.55f, 0.55f);

    private GUIStyle stTitle, stChip, stHead, stBadge, stLog, stNote;

    private void EnsureStyles()
    {
        if (stTitle != null) return;

        stTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        stTitle.normal.textColor = Color.white;

        stChip = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        stChip.normal.textColor = Color.white;

        stHead = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        stHead.normal.textColor = Color.white;

        stBadge = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        stBadge.normal.textColor = new Color(1f, 1f, 1f, 0.85f);

        stLog  = new GUIStyle(EditorStyles.miniLabel) { richText = false };
        stNote = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
    }

    private static Color Dim(Color c, float f) { return new Color(c.r * f, c.g * f, c.b * f, 1f); }

    void OnGUI()
    {
        EnsureStyles();

        DrawTitleBar();

        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
        EditorGUILayout.Space(6);

        DrawNotice();
        EditorGUILayout.Space(8);

        DrawStepSource();
        EditorGUILayout.Space(8);

        DrawStepDest();
        EditorGUILayout.Space(8);

        DrawStepOptions();
        EditorGUILayout.Space(6);

        EditorGUILayout.EndScrollView();

        DrawFooter();
    }

    // ── タイトルバー ──────────────────────────────────────────────
    private void DrawTitleBar()
    {
        Rect r = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, Pro ? new Color(0.16f, 0.17f, 0.19f) : new Color(0.30f, 0.32f, 0.36f));
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), CBlue);

        GUI.Label(new Rect(r.x + 12f, r.y, r.width - 90f, r.height), "Folder Duplicator", stTitle);
        GUI.Label(new Rect(r.xMax - 78f, r.y, 66f, r.height), "v" + Version, stBadge);
    }

    // ── 注意書き ──────────────────────────────────────────────────
    private void DrawNotice()
    {
        Rect r = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), CAmber);
        GUILayout.Space(1);
        EditorGUILayout.LabelField("複製されるのは  マテリアル / テクスチャ / Prefab  のみです",
            EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("フォルダ構造は空フォルダも含めてそのまま再現します。", stNote);
        EditorGUILayout.LabelField(
            "FBX・メッシュ・アニメーション・スクリプトはコピーされません。\n" +
            "複製先に同名ファイルがある場合は上書きせずスキップします。",
            stNote);
        EditorGUILayout.EndVertical();
    }

    // ── ステップ見出し ────────────────────────────────────────────
    private void DrawStepHeader(string num, string title, string badge, bool filled, Color accent)
    {
        Rect r = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, Dim(accent, filled ? 1f : 0.52f));
        EditorGUI.DrawRect(new Rect(r.x, r.y, 30f, r.height), Dim(accent, filled ? 0.68f : 0.36f));

        GUI.Label(new Rect(r.x, r.y, 30f, r.height), num, stChip);
        GUI.Label(new Rect(r.x + 38f, r.y, r.width - 100f, r.height), title, stHead);
        GUI.Label(new Rect(r.xMax - 62f, r.y, 54f, r.height), badge, stBadge);
    }

    // ── STEP 1 複製元 ─────────────────────────────────────────────
    private void DrawStepSource()
    {
        bool ok = sourceFolderAsset != null;
        DrawStepHeader("1", "複製元フォルダ", ok ? "✓ 設定済" : "必須", ok, CBlue);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawFolderField("複製元", ref sourceFolderAsset);
        DrawPathHint(sourceFolderAsset, "アバターA用のセットアップ済みフォルダを指定します");
        EditorGUILayout.EndVertical();
    }

    // ── STEP 2 複製先 ─────────────────────────────────────────────
    private void DrawStepDest()
    {
        bool ok = destFolderAsset != null;
        DrawStepHeader("2", "複製先フォルダ", ok ? "✓ 設定済" : "必須", ok, CTeal);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        int mode = useDirectMode ? 1 : 0;
        int newMode = GUILayout.Toolbar(mode,
            new[] { "フォルダを作りながら指定", "既存フォルダを直接指定" }, GUILayout.Height(20));
        useDirectMode = (newMode == 1);
        EditorGUILayout.Space(6);

        if (useDirectMode) DrawDirectMode();
        else               DrawStructureMode();

        EditorGUILayout.EndVertical();
    }

    // ── STEP 3 オプション ─────────────────────────────────────────
    private void DrawStepOptions()
    {
        int validPairs = 0;
        foreach (var p in fbxPairs)
            if (p.src != null && p.dst != null && ValidateFbxPair(p) == null) validPairs++;

        DrawStepHeader("3", "Prefab / FBX オプション",
            validPairs > 0 ? "FBX " + validPairs + "組" : "任意", copyPrefabs, CPurple);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        copyPrefabs = EditorGUILayout.ToggleLeft(
            " Prefab / Prefab Variant も複製する", copyPrefabs);

        EditorGUI.BeginDisabledGroup(!copyPrefabs);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
        EditorGUILayout.BeginVertical();
        rebakeConstraints = EditorGUILayout.ToggleLeft(
            " コンストレイントのOffsetを焼き直す", rebakeConstraints);
        EditorGUILayout.LabelField(
            "Rest（調整済みの基本形状）は差分として引き継ぎ、Offset を複製先FBXの" +
            "ボーン基準で再計算します。スカートの破綻対策です。", stNote);
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(8);
        DrawSubLabel("FBX 差し替え", CPurple);
        EditorGUILayout.LabelField(
            "FBX自体はコピーせず、複製した Prefab 内の参照だけを差し替えます。", stNote);
        EditorGUILayout.Space(4);

        destPoseWins = EditorGUILayout.ToggleLeft(
            " ボーンの姿勢は複製先FBXに従う", destPoseWins);
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField(
            "複製元のボーン姿勢の上書きを引き継ぎません。OFFにすると複製元アバターの" +
            "ポーズが持ち込まれ、Tポーズ化や腕の変形が起きることがあります。", stNote);
        EditorGUI.indentLevel--;

        if (destPoseWins && copyPrefabs && !rebakeConstraints)
            DrawInlineWarning("再ベイクがOFFのため、スカート等の調整済み姿勢が失われます");

        EditorGUILayout.Space(2);

        if (fbxPairs.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("複製元FBX", EditorStyles.miniBoldLabel);
            GUILayout.Space(22);
            EditorGUILayout.LabelField("複製先FBX", EditorStyles.miniBoldLabel);
            GUILayout.Space(26);
            EditorGUILayout.EndHorizontal();
        }

        int removeAt = -1;
        for (int i = 0; i < fbxPairs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            fbxPairs[i].src = EditorGUILayout.ObjectField(fbxPairs[i].src, typeof(GameObject), false);
            GUILayout.Label("→", GUILayout.Width(18));
            fbxPairs[i].dst = EditorGUILayout.ObjectField(fbxPairs[i].dst, typeof(GameObject), false);
            if (GUILayout.Button("✕", GUILayout.Width(24))) removeAt = i;
            EditorGUILayout.EndHorizontal();

            string warn = ValidateFbxPair(fbxPairs[i]);
            if (warn != null) DrawInlineWarning(warn);
        }
        if (removeAt >= 0) fbxPairs.RemoveAt(removeAt);

        EditorGUILayout.Space(2);
        if (GUILayout.Button("＋  FBXの対応を追加", GUILayout.Height(22)))
            fbxPairs.Add(new FbxPair());

        EditorGUILayout.EndVertical();
    }

    // ── フッター（実行ボタン + ログ） ─────────────────────────────
    private void DrawFooter()
    {
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)),
            Pro ? new Color(0f, 0f, 0f, 0.4f) : new Color(0f, 0f, 0f, 0.15f));
        EditorGUILayout.Space(6);

        bool ready = sourceFolderAsset != null && destFolderAsset != null;

        if (!ready)
        {
            string miss = sourceFolderAsset == null ? "STEP 1 の複製元フォルダを指定してください"
                                                    : "STEP 2 の複製先フォルダを指定してください";
            DrawInlineWarning(miss);
            EditorGUILayout.Space(2);
        }

        Color prev = GUI.backgroundColor;
        if (ready) GUI.backgroundColor = CGreen;
        EditorGUI.BeginDisabledGroup(!ready);
        if (GUILayout.Button(ready ? "複 製 を 実 行" : "実行できません", GUILayout.Height(34)))
        { logs.Clear(); Execute(); }
        EditorGUI.EndDisabledGroup();
        GUI.backgroundColor = prev;

        if (logs.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("実行ログ", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("クリア", EditorStyles.miniButton, GUILayout.Width(52)))
                logs.Clear();
            EditorGUILayout.EndHorizontal();

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(150));
            foreach (string line in logs)
            {
                Color c = LogColor(line);
                Color save = stLog.normal.textColor;
                stLog.normal.textColor = c;
                EditorGUILayout.LabelField(line, stLog);
                stLog.normal.textColor = save;
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(2);
        var credit = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9 };
        EditorGUILayout.LabelField(
            string.Format("Folder Duplicator  v{0}  ·  Made with Claude Code", Version), credit);
        EditorGUILayout.Space(2);
    }

    private static Color LogColor(string line)
    {
        if (line.StartsWith("OK"))    return CGreen;
        if (line.StartsWith("SKIP"))  return CGray;
        if (line.StartsWith("WARN"))  return CAmber;
        if (line.StartsWith("ERROR")) return CRed;
        if (line.StartsWith("FBX差替")) return CPurple;
        return Pro ? new Color(0.82f, 0.82f, 0.82f) : new Color(0.20f, 0.20f, 0.20f);
    }

    // ── 小物 ──────────────────────────────────────────────────────
    private void DrawSubLabel(string text, Color accent)
    {
        EditorGUILayout.BeginHorizontal();
        Rect bar = GUILayoutUtility.GetRect(3f, 13f, GUILayout.Width(3f));
        EditorGUI.DrawRect(bar, accent);
        GUILayout.Space(5);
        EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawInlineWarning(string text)
    {
        EditorGUILayout.BeginHorizontal();
        Rect bar = GUILayoutUtility.GetRect(3f, 14f, GUILayout.Width(3f));
        EditorGUI.DrawRect(bar, CAmber);
        GUILayout.Space(5);
        var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
        st.normal.textColor = Pro ? new Color(0.95f, 0.80f, 0.40f) : new Color(0.62f, 0.44f, 0.05f);
        EditorGUILayout.LabelField(text, st);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPathHint(DefaultAsset asset, string emptyHint)
    {
        if (asset != null)
        {
            var st = new GUIStyle(EditorStyles.miniLabel);
            st.normal.textColor = Pro ? new Color(0.55f, 0.80f, 0.60f) : new Color(0.15f, 0.45f, 0.25f);
            EditorGUILayout.LabelField("  " + AssetDatabase.GetAssetPath(asset), st);
        }
        else
        {
            EditorGUILayout.LabelField("  " + emptyHint, stNote);
        }
    }

    private string ValidateFbxPair(FbxPair p)
    {
        if (p.src == null && p.dst == null) return null;
        if (p.src == null || p.dst == null) return "複製元と複製先の両方を指定してください";
        if (!IsModelAsset(p.src) || !IsModelAsset(p.dst)) return "FBXなどのモデルファイルを指定してください";
        if (p.src == p.dst) return "複製元と複製先が同じFBXです";
        return null;
    }

    private static bool IsModelAsset(Object o)
    {
        string path = AssetDatabase.GetAssetPath(o);
        return !string.IsNullOrEmpty(path) && ModelExts.Contains(Path.GetExtension(path));
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

        foreach (var p in fbxPairs)
        {
            string warn = ValidateFbxPair(p);
            if (warn != null)
            { EditorUtility.DisplayDialog("エラー", "FBX差し替えの設定に問題があります：\n" + warn, "OK"); return; }
        }

        // Phase 0: フォルダ構造をそのまま複製する。
        //   ファイルを置くときに必要な分だけ作る作りだと、中身の無いフォルダが再現されない。
        int createdFolders = 0;
        foreach (string sub in GetAllSubFolders(srcRoot))
        {
            string dstSub = ToDstPath(sub, srcRoot, dstRoot);
            if (AssetDatabase.IsValidFolder(dstSub)) continue;
            EnsureFolderExists(dstSub);
            if (AssetDatabase.IsValidFolder(dstSub))
            { Log("OK    [Dir] " + dstSub.Substring(dstRoot.Length).TrimStart('/')); createdFolders++; }
            else
            { Log("ERROR [Dir] " + dstSub); }
        }

        // 複製したファイルのパスマップ: srcPath → dstPath
        var fileMap = new Dictionary<string, string>();
        int copiedTex = 0, skippedTex = 0;

        // Phase 1: テクスチャをコピー
        foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { srcRoot }))
        {
            string srcPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!TextureExts.Contains(Path.GetExtension(srcPath))) continue;

            string dstPath = ToDstPath(srcPath, srcRoot, dstRoot);
            EnsureFolderExists(Path.GetDirectoryName(dstPath).Replace("\\", "/"));
            fileMap[srcPath] = dstPath;

            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            { Log("SKIP  [Tex] " + Path.GetFileName(dstPath)); skippedTex++; }
            else if (AssetDatabase.CopyAsset(srcPath, dstPath))
            { Log("OK    [Tex] " + Path.GetFileName(dstPath)); copiedTex++; }
            else
            { Log("ERROR [Tex] " + srcPath); }
        }

        // Phase 2: マテリアルをコピー（GUID置換はまだしない）
        int copiedMat = 0, skippedMat = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { srcRoot }))
        {
            string srcPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!srcPath.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (AssetDatabase.LoadAssetAtPath<Material>(srcPath) == null) continue;

            string dstPath = ToDstPath(srcPath, srcRoot, dstRoot);
            EnsureFolderExists(Path.GetDirectoryName(dstPath).Replace("\\", "/"));
            fileMap[srcPath] = dstPath;

            if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
            { Log("SKIP  [Mat] " + Path.GetFileName(dstPath)); skippedMat++; continue; }

            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
            { Log("ERROR [Mat] " + srcPath); continue; }

            Log("OK    [Mat] " + Path.GetFileName(dstPath));
            copiedMat++;
        }

        // Phase 3: Prefab / Prefab Variant をコピー（GUID置換はまだしない）
        int copiedPrefab = 0, skippedPrefab = 0;
        if (copyPrefabs)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { srcRoot }))
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!srcPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;

                string dstPath = ToDstPath(srcPath, srcRoot, dstRoot);
                EnsureFolderExists(Path.GetDirectoryName(dstPath).Replace("\\", "/"));
                fileMap[srcPath] = dstPath;

                if (AssetDatabase.LoadAssetAtPath<Object>(dstPath) != null)
                { Log("SKIP  [Pfb] " + Path.GetFileName(dstPath)); skippedPrefab++; continue; }

                if (!AssetDatabase.CopyAsset(srcPath, dstPath))
                { Log("ERROR [Pfb] " + srcPath); continue; }

                Log("OK    [Pfb] " + Path.GetFileName(dstPath));
                copiedPrefab++;
            }
        }

        // 全ファイルのインポート完了を待つ
        AssetDatabase.Refresh();

        // GUIDマップを構築: srcGUID → dstGUID
        var guidMap = new Dictionary<string, string>();
        foreach (var kv in fileMap)
        {
            string srcGuid = AssetDatabase.AssetPathToGUID(kv.Key);
            string dstGuid = AssetDatabase.AssetPathToGUID(kv.Value);
            if (!string.IsNullOrEmpty(srcGuid) && !string.IsNullOrEmpty(dstGuid) && srcGuid != dstGuid)
                guidMap[srcGuid] = dstGuid;
        }

        // FBX は複製せず、指定された対応に沿って参照だけ差し替える。
        //   FBX内のボーン参照は {fileID, guid} のペアなので、guid だけ差し替えると
        //   階層の違うボーン（Breast bone 等）で fileID が一致せず参照が壊れる。
        //   そこでボーン単位の fileID 対応表も作る。
        var fbxRemaps = new List<FbxRemap>();
        foreach (var p in fbxPairs)
        {
            if (p.src == null || p.dst == null) continue;
            string srcGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p.src));
            string dstGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p.dst));
            if (string.IsNullOrEmpty(srcGuid) || string.IsNullOrEmpty(dstGuid) || srcGuid == dstGuid) continue;

            var remap = BuildFbxRemap(p.src, p.dst, srcGuid, dstGuid);
            fbxRemaps.Add(remap);
            Log(string.Format("FBX差替 {0} → {1}   （ボーン対応 {2} 件）",
                p.src.name, p.dst.name, remap.IdMap.Count));
        }

        Log(string.Format("GUIDマップ: {0} 件（Tex/Mat/Prefab） + FBX差替 {1} 組",
            guidMap.Count, fbxRemaps.Count));

        // Phase 4: 複製した .mat / .prefab の参照を一括で書き換える
        //   → テクスチャ / マテリアル(m_Parent) / Prefab(m_SourcePrefab) / FBX内ボーン
        int rewritten = 0, strippedOverrides = 0;
        var unresolved = new SortedDictionary<string, int>();
        if (guidMap.Count > 0 || fbxRemaps.Count > 0)
        {
            foreach (var kv in fileMap)
            {
                if (!IsRewritableYaml(kv.Key)) continue;
                string absPath = Application.dataPath + kv.Value.Substring("Assets".Length);
                if (!File.Exists(absPath)) continue;

                string text = File.ReadAllText(absPath);
                string replaced = text;

                // 差し替え対象FBXのボーンに対する姿勢の上書きは引き継がない。
                //   値は複製元アバターの骨格を前提とした絶対値なので、
                //   複製先の骨格に適用するとポーズが壊れる。
                if (destPoseWins && fbxRemaps.Count > 0 &&
                    kv.Key.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    replaced = StripFbxTransformOverrides(replaced, fbxRemaps, ref strippedOverrides);

                replaced = RewriteReferences(replaced, guidMap, fbxRemaps, unresolved);

                if (replaced != text) { File.WriteAllText(absPath, replaced); rewritten++; }
            }
        }
        Log(string.Format("参照を書き換えたファイル: {0} 件", rewritten));
        if (strippedOverrides > 0)
            Log(string.Format("ボーン姿勢の上書きを除去: {0} 件（複製先FBXの姿勢を使用）", strippedOverrides));

        if (unresolved.Count > 0)
        {
            Log(string.Format("WARN  複製先FBXに見つからないボーン {0} 種類（参照が切れます）", unresolved.Count));
            int shown = 0;
            foreach (var u in unresolved)
            {
                Log("WARN    ・" + u.Key + (u.Value > 1 ? "  ×" + u.Value : ""));
                if (++shown >= 12) { Log("WARN    ・…ほか " + (unresolved.Count - shown) + " 種類"); break; }
            }
        }

        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();

        // Phase 5: コンストレイントを複製先FBXの初期姿勢で再ベイク
        int rebakedConst = 0, rebakedPrefabs = 0;
        if (copyPrefabs && rebakeConstraints)
        {
            foreach (var kv in fileMap)
            {
                if (!kv.Key.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                int n = RebakePrefabConstraints(kv.Key, kv.Value);
                if (n > 0) { rebakedConst += n; rebakedPrefabs++; }
            }
            if (rebakedConst > 0)
            {
                Log(string.Format("再ベイク: {0} 件のコンストレイント / {1} 個のPrefab",
                    rebakedConst, rebakedPrefabs));
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
            }
        }

        string summary = string.Format(
            "完了：テクスチャ {0}/{1}、マテリアル {2}/{3}、Prefab {4}/{5}（複製/スキップ）"
            + "\nフォルダ {7} 個を作成"
            + (rebakedConst > 0 ? "\nコンストレイント {6} 件を再ベイクしました" : ""),
            copiedTex, skippedTex, copiedMat, skippedMat, copiedPrefab, skippedPrefab,
            rebakedConst, createdFolders);
        Log(summary);
        EditorUtility.DisplayDialog("完了", summary, "OK");
    }

    // ─ FBX のボーン単位 fileID 対応 ───────────────────────────────
    private class FbxRemap
    {
        public string SrcGuid, DstGuid;
        public Dictionary<long, long>   IdMap  = new Dictionary<long, long>();
        public Dictionary<long, string> IdName = new Dictionary<long, string>(); // 未解決時の表示用
    }

    // 複製元FBXと複製先FBXのオブジェクトを対応付け、fileID の変換表を作る。
    //   1) 階層パス一致を優先（安全）
    //   2) 見つからなければ名前一致にフォールバック（階層が違うボーン対策）
    private FbxRemap BuildFbxRemap(Object srcFbx, Object dstFbx, string srcGuid, string dstGuid)
    {
        var remap = new FbxRemap { SrcGuid = srcGuid, DstGuid = dstGuid };

        var srcRoot = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(srcFbx)) as GameObject;
        var dstRoot = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(dstFbx)) as GameObject;
        if (srcRoot == null || dstRoot == null) return remap;

        // モデルPrefabのルート参照（m_SourcePrefab 等）は常にこのID
        remap.IdMap[100100000L] = 100100000L;

        // 1段階目: ルート同士から親子をたどって対応付ける
        var usedDst = new HashSet<Transform>();
        var pending = new List<Transform>();
        MatchRecursive(remap, srcRoot.transform, dstRoot.transform, usedDst, pending);

        // 2段階目: 余ったものを階層全体から名前で拾い直す
        //   （複製先に Upper Chest 等の中間ノードがあり深さが違う場合の救済）
        GlobalNameMatch(remap, dstRoot.transform, usedDst, pending);

        // メッシュ / Avatar / AnimationClip などのサブアセットを対応付ける
        MatchSubAssets(remap, AssetDatabase.GetAssetPath(srcFbx), AssetDatabase.GetAssetPath(dstFbx));

        return remap;
    }

    // 型＋名前で対応付け、余りが型ごとに1対1ならそれで確定する
    private static void MatchSubAssets(FbxRemap remap, string srcPath, string dstPath)
    {
        var dstList = new List<Object>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(dstPath))
            if (o != null && !(o is GameObject) && !(o is Transform)) dstList.Add(o);

        var srcList = new List<Object>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(srcPath))
            if (o != null && !(o is GameObject) && !(o is Transform)) srcList.Add(o);

        var used = new bool[dstList.Count];
        var pair = new Object[srcList.Count];

        for (int i = 0; i < srcList.Count; i++)
            for (int j = 0; j < dstList.Count; j++)
                if (!used[j] && dstList[j].GetType() == srcList[i].GetType()
                             && dstList[j].name == srcList[i].name)
                { pair[i] = dstList[j]; used[j] = true; break; }

        for (int i = 0; i < srcList.Count; i++)
        {
            if (pair[i] != null) continue;
            // 同じ型で余りが1つずつなら対応させる（Avatar 等の名前違い対策）
            int cs = 0, cd = 0, dIdx = -1;
            for (int k = 0; k < srcList.Count; k++)
                if (pair[k] == null && srcList[k].GetType() == srcList[i].GetType()) cs++;
            for (int j = 0; j < dstList.Count; j++)
                if (!used[j] && dstList[j].GetType() == srcList[i].GetType()) { cd++; dIdx = j; }
            if (cs == 1 && cd == 1) { pair[i] = dstList[dIdx]; used[dIdx] = true; }
        }

        for (int i = 0; i < srcList.Count; i++)
            AddIdPair(remap, srcList[i], pair[i], srcList[i].name);
    }

    // 親が対応付いた前提で子を照合していく。
    //   1) 完全一致
    //   2) 正規化名一致  … "Upper Leg.L" と "UpperLeg_L" を同一視する
    //   3) 残りが1対1   … "Armature" → "Armature_FemalBody" のような改名を救済
    private static void MatchRecursive(FbxRemap remap, Transform s, Transform d,
                                       HashSet<Transform> usedDst, List<Transform> pending)
    {
        AddIdPair(remap, s,            d,            s.name);
        AddIdPair(remap, s.gameObject, d.gameObject, s.name);
        usedDst.Add(d);

        int sc = s.childCount, dc = d.childCount;
        var pair = new Transform[sc];
        var used = new bool[dc];

        for (int i = 0; i < sc; i++)
            for (int j = 0; j < dc; j++)
                if (!used[j] && d.GetChild(j).name == s.GetChild(i).name)
                { pair[i] = d.GetChild(j); used[j] = true; break; }

        for (int i = 0; i < sc; i++)
        {
            if (pair[i] != null) continue;
            string key = NormName(s.GetChild(i).name);
            for (int j = 0; j < dc; j++)
                if (!used[j] && NormName(d.GetChild(j).name) == key)
                { pair[i] = d.GetChild(j); used[j] = true; break; }
        }

        // 兄弟の余りが1対1で、かつ名前が包含関係にあるときだけ改名とみなす
        //   （Armature → Armature_FemalBody のようなケース）
        int su = 0, sIdx = -1, du = 0, dIdx = -1;
        for (int i = 0; i < sc; i++) if (pair[i] == null) { su++; sIdx = i; }
        for (int j = 0; j < dc; j++) if (!used[j])        { du++; dIdx = j; }
        if (su == 1 && du == 1)
        {
            string a = NormName(s.GetChild(sIdx).name), b = NormName(d.GetChild(dIdx).name);
            if (a.Length > 0 && b.Length > 0 && (a.Contains(b) || b.Contains(a)))
            { pair[sIdx] = d.GetChild(dIdx); used[dIdx] = true; }
        }

        for (int i = 0; i < sc; i++)
        {
            if (pair[i] != null) MatchRecursive(remap, s.GetChild(i), pair[i], usedDst, pending);
            else foreach (var t in s.GetChild(i).GetComponentsInChildren<Transform>(true))
                     pending.Add(t);   // 後で階層全体から拾い直す
        }
    }

    // 親子たどりで余ったものを、複製先の階層全体から名前で照合する。
    //   複製先に中間ノードが増えていて深さが違う場合でも拾える。
    private static void GlobalNameMatch(FbxRemap remap, Transform dstRoot,
                                        HashSet<Transform> usedDst, List<Transform> pending)
    {
        var byName = new Dictionary<string, Transform>();
        var byNorm = new Dictionary<string, Transform>();
        var dupName = new HashSet<string>();
        var dupNorm = new HashSet<string>();

        foreach (var t in dstRoot.GetComponentsInChildren<Transform>(true))
        {
            if (usedDst.Contains(t)) continue;
            if (byName.ContainsKey(t.name)) dupName.Add(t.name); else byName[t.name] = t;
            string k = NormName(t.name);
            if (byNorm.ContainsKey(k)) dupNorm.Add(k); else byNorm[k] = t;
        }

        foreach (var st in pending)
        {
            Transform dt = null;
            if (!dupName.Contains(st.name)) byName.TryGetValue(st.name, out dt);
            if (dt == null)
            {
                string k = NormName(st.name);
                if (!dupNorm.Contains(k)) byNorm.TryGetValue(k, out dt);
            }
            if (dt != null && usedDst.Contains(dt)) dt = null;

            if (dt != null)
            {
                usedDst.Add(dt);
                AddIdPair(remap, st,            dt,            st.name);
                AddIdPair(remap, st.gameObject, dt.gameObject, st.name);
            }
            else
            {
                AddIdPair(remap, st,            null, st.name);
                AddIdPair(remap, st.gameObject, null, st.name);
            }
        }
    }

    // 区切り文字と大小文字の違いを吸収する（Blender式とUnity式の命名差対策）
    private static string NormName(string n)
    {
        var sb = new System.Text.StringBuilder(n.Length);
        foreach (char c in n)
            if (c != ' ' && c != '_' && c != '.' && c != '-') sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static void AddIdPair(FbxRemap remap, Object src, Object dst, string label)
    {
        long srcId = LocalIdOf(src);
        if (srcId == 0) return;
        remap.IdName[srcId] = label;
        if (dst == null) return;
        long dstId = LocalIdOf(dst);
        if (dstId != 0) remap.IdMap[srcId] = dstId;
    }

    private static long LocalIdOf(Object o)
    {
        string g; long id;
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out g, out id) ? id : 0L;
    }

    // 差し替え対象FBXのボーンに対する Transform 上書きを Prefab から取り除く。
    //   複製元アバターの骨格を前提とした絶対値なので、複製先に持ち込むと
    //   ポーズやサイズが壊れる（Tポーズ化・腕の変形など）。
    //   スカート等の調整分はコンストレイント再ベイクが差分として復元する。
    private static string StripFbxTransformOverrides(string text, List<FbxRemap> remaps, ref int removed)
    {
        foreach (var rm in remaps)
        {
            string pattern =
                @"[ \t]*- target: \{fileID: -?\d+, guid: " + rm.SrcGuid + @", type: \d+\}[ \t]*\r?\n" +
                @"[ \t]*propertyPath: (?:m_LocalPosition|m_LocalRotation|m_LocalScale|m_LocalEulerAnglesHint)\.[xyzw][ \t]*\r?\n" +
                @"[ \t]*value: [^\r\n]*\r?\n" +
                @"[ \t]*objectReference: \{fileID: \d+\}[ \t]*\r?\n";

            var rx = new System.Text.RegularExpressions.Regex(pattern);
            removed += rx.Matches(text).Count;
            text = rx.Replace(text, "");
        }
        return text;
    }

    // {fileID, guid} を対応表に沿って書き換える
    private static string RewriteReferences(string text,
                                            Dictionary<string, string> guidMap,
                                            List<FbxRemap> fbxRemaps,
                                            SortedDictionary<string, int> unresolved)
    {
        // FBX参照は fileID ごと差し替える
        foreach (var rm in fbxRemaps)
        {
            var remap = rm;
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\{fileID: (-?\d+), guid: " + remap.SrcGuid + @", type: (\d+)\}",
                m =>
                {
                    long id = long.Parse(m.Groups[1].Value);
                    long dstId;
                    if (remap.IdMap.TryGetValue(id, out dstId))
                        return "{fileID: " + dstId + ", guid: " + remap.DstGuid +
                               ", type: " + m.Groups[2].Value + "}";

                    string name;
                    if (!remap.IdName.TryGetValue(id, out name)) name = "(不明 id:" + id + ")";
                    int n;
                    unresolved.TryGetValue(name, out n);
                    unresolved[name] = n + 1;

                    // 対応が取れない場合も複製元FBXは参照させない（エクスポート汚染を防ぐ）
                    return "{fileID: " + id + ", guid: " + remap.DstGuid +
                           ", type: " + m.Groups[2].Value + "}";
                });
        }

        // テクスチャ / マテリアル / Prefab は fileID が変わらないので guid だけ差し替える
        foreach (var g in guidMap)
            text = text.Replace("guid: " + g.Key, "guid: " + g.Value);

        return text;
    }

    // ─ コンストレイントの再ベイク ─────────────────────────────────
    //   Rest（スカートの基本形状）は「FBX素の姿勢からの差分」として移植する。
    //     複製元Restをそのまま使う   → 複製元アバターの形状が残り破綻する
    //     複製先FBX素の姿勢にする    → 作者の調整分が失われ動きが荒ぶる
    //   そこで  差分 = Inverse(複製元FBX姿勢) * 複製元Rest  を
    //           新Rest = 複製先FBX姿勢 * 差分  として持ち込む。
    //   そのうえで Offset を複製先のソースボーン基準で焼き直す。
    private int RebakePrefabConstraints(string srcPrefabPath, string dstPrefabPath)
    {
        GameObject srcRoot = null, dstRoot = null;
        try { dstRoot = PrefabUtility.LoadPrefabContents(dstPrefabPath); }
        catch (System.Exception e)
        {
            Log("WARN  [Rebake] 読込失敗 " + Path.GetFileName(dstPrefabPath) + " : " + e.Message);
            return 0;
        }
        if (dstRoot == null) return 0;

        int baked = 0, transferred = 0;
        try
        {
            var rots = dstRoot.GetComponentsInChildren<RotationConstraint>(true);

            int others = dstRoot.GetComponentsInChildren<PositionConstraint>(true).Length
                       + dstRoot.GetComponentsInChildren<ParentConstraint>(true).Length
                       + dstRoot.GetComponentsInChildren<AimConstraint>(true).Length
                       + dstRoot.GetComponentsInChildren<ScaleConstraint>(true).Length
                       + dstRoot.GetComponentsInChildren<LookAtConstraint>(true).Length;
            if (others > 0)
                Log(string.Format("WARN  [Rebake] {0} : RotationConstraint以外 {1} 件は再ベイク対象外です",
                    Path.GetFileName(dstPrefabPath), others));

            if (rots.Length == 0) return 0;

            // 複製元Prefabを開き、階層パスでボーンを対応付けられるようにする
            var srcMap = new Dictionary<string, Transform>();
            try
            {
                srcRoot = PrefabUtility.LoadPrefabContents(srcPrefabPath);
                if (srcRoot != null)
                    foreach (var t in srcRoot.GetComponentsInChildren<Transform>(true))
                        srcMap[PathOf(t, srcRoot.transform)] = t;
            }
            catch { srcRoot = null; }

            // Pass 1: 調整差分を移植して Rest とボーン姿勢を決める
            foreach (var rc in rots)
            {
                if (!HasPositiveSource(rc)) continue;

                Transform dstT      = rc.transform;
                Quaternion dstFbx   = OriginalLocalRotation(dstT);
                Quaternion newLocal = dstFbx;

                Transform srcT;
                if (srcMap.TryGetValue(PathOf(dstT, dstRoot.transform), out srcT) && srcT != null)
                {
                    // 複製元での「FBX素の姿勢 → 実際の姿勢」の差分を求めて持ち込む
                    Quaternion delta = Quaternion.Inverse(OriginalLocalRotation(srcT)) * srcT.localRotation;
                    newLocal = dstFbx * delta;
                    transferred++;
                }

                dstT.localRotation = newLocal;
                rc.rotationAtRest  = newLocal.eulerAngles;
            }

            // Pass 2: 全ボーンが確定してから Offset を焼き直す
            foreach (var rc in rots)
            {
                if (!HasPositiveSource(rc)) continue;

                Transform t = rc.transform;
                Quaternion restWorld = t.parent != null
                    ? t.parent.rotation * Quaternion.Euler(rc.rotationAtRest)
                    : Quaternion.Euler(rc.rotationAtRest);

                rc.rotationOffset = (Quaternion.Inverse(WeightedSourceRotation(rc)) * restWorld).eulerAngles;
                baked++;
            }

            if (baked == 0)
            {
                Log(string.Format("WARN  [Rebake] {0} : ソースが解決できずベイク対象が 0 件でした",
                    Path.GetFileName(dstPrefabPath)));
            }
            else
            {
                // 保存前に損失チェック。
                //   複製先FBXに存在しないボーンがあると Unity はそのノードごと捨ててロードするため、
                //   そのまま保存すると PhysBone などが消えて確定してしまう。
                //   ファイルの体裁は Unity が正規化するので、テキストではなく
                //   「読み込まれた Transform 以外のコンポーネント数」を複製元と比べる。
                int dstComps = CountNonTransformComponents(dstRoot);
                int srcComps = srcRoot != null ? CountNonTransformComponents(srcRoot) : -1;

                if (srcComps >= 0 && dstComps < srcComps)
                {
                    Log(string.Format(
                        "WARN  [Rebake] {0} : コンポーネントが {1} → {2} に減るため保存を中止しました",
                        Path.GetFileName(dstPrefabPath), srcComps, dstComps));
                    Log("WARN    複製先FBXに存在しないボーンがあります。上のボーン名の警告を確認してください");
                    baked = 0;
                }
                else
                {
                    PrefabUtility.SaveAsPrefabAsset(dstRoot, dstPrefabPath);
                    Log(string.Format("OK    [Rebake] {0}  ({1} 件 / 調整差分の移植 {2} 件 / component {3})",
                        Path.GetFileName(dstPrefabPath), baked, transferred, dstComps));
                }
            }
        }
        catch (System.Exception e)
        {
            Log("ERROR [Rebake] " + Path.GetFileName(dstPrefabPath) + " : " + e.Message);
            baked = 0;
        }
        finally
        {
            if (srcRoot != null) PrefabUtility.UnloadPrefabContents(srcRoot);
            PrefabUtility.UnloadPrefabContents(dstRoot);
        }

        return baked;
    }

    // Transform 以外のコンポーネント数。
    //   ボーン数はFBXによって違ってよいが、PhysBone等のコンポーネントは
    //   複製元と同数でなければならない。減っていればノードが失われている。
    private static int CountNonTransformComponents(GameObject root)
    {
        int n = 0;
        foreach (var c in root.GetComponentsInChildren<Component>(true))
            if (c != null && !(c is Transform)) n++;
        return n;
    }

    // そのTransformの元アセット（FBX）側での初期ローカル回転
    private static Quaternion OriginalLocalRotation(Transform t)
    {
        var orig = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t);
        return orig != null ? orig.localRotation : t.localRotation;
    }

    // ルートから見た階層パス（ルート名は含めない）
    private static string PathOf(Transform t, Transform root)
    {
        if (t == root) return "";
        var sb = new System.Text.StringBuilder(t.name);
        for (Transform p = t.parent; p != null && p != root; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    // ソースの現在の回転をウェイト比で加重平均（SkirtConstraintBuilder と同一式）
    private static Quaternion WeightedSourceRotation(RotationConstraint rc)
    {
        Quaternion avg = Quaternion.identity;
        float acc = 0f;
        bool first = true;
        for (int i = 0; i < rc.sourceCount; i++)
        {
            var s = rc.GetSource(i);
            if (s.sourceTransform == null || s.weight <= 0f) continue;
            if (first) { avg = s.sourceTransform.rotation; acc = s.weight; first = false; }
            else
            {
                acc += s.weight;
                avg = Quaternion.Slerp(avg, s.sourceTransform.rotation, s.weight / acc);
            }
        }
        return first ? Quaternion.identity : avg;
    }

    private static bool HasPositiveSource(RotationConstraint rc)
    {
        for (int i = 0; i < rc.sourceCount; i++)
        {
            var s = rc.GetSource(i);
            if (s.sourceTransform != null && s.weight > 0f) return true;
        }
        return false;
    }

    // YAML テキストとして GUID 置換できる複製物か
    private static bool IsRewritableYaml(string path)
        => path.EndsWith(".mat",    System.StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);

    private static string ToDstPath(string srcPath, string srcRoot, string dstRoot)
        => (dstRoot + srcPath.Substring(srcRoot.Length)).Replace("\\", "/");

    // 指定フォルダ配下のサブフォルダを再帰的に列挙する（浅い順）
    private static List<string> GetAllSubFolders(string root)
    {
        var list  = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
            foreach (string sub in AssetDatabase.GetSubFolders(queue.Dequeue()))
            { list.Add(sub); queue.Enqueue(sub); }
        return list;
    }

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

    private void Log(string msg) { logs.Add(msg); Debug.Log("[FolderDuplicator] " + msg); }
}

} // namespace MaterialDuplicatorTool
