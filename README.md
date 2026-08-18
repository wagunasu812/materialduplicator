# Material Duplicator

VRChatアバター対応モデル製作者向け マテリアル・テクスチャ複製 & 衣装移植ツール

用途の異なる **2つのツール** が含まれています。

| ツール | メニュー | 役割 |
|---|---|---|
| **Material Duplicator** | `Tools > Material Duplicator` | マテリアル1つ分のテクスチャを条件を絞って複製し、自動アサイン |
| **Folder Duplicator** | `Tools > Folder Duplicator` | 衣装フォルダを丸ごと別アバター用に移植 |

## 📖 マニュアル

**[使用マニュアル（PDF）](docs/MaterialDuplicator_Manual.pdf)** — 両ツールの使い方・FAQを収録

## 機能

### Material Duplicator
- 複製元マテリアルを指定フォルダへ複製
- マテリアルに使用されているテクスチャを別フォルダへ複製
- 複製したテクスチャを複製マテリアルへ自動アサイン
- キーワードフィルターによる対象テクスチャの選択
- プリセット管理

### Folder Duplicator
- 衣装フォルダのマテリアル・テクスチャ・Prefab を丸ごと複製（空フォルダも再現）
- **FBX差し替え** — FBX自体はコピーせず、Prefab内の参照だけを複製先FBXへ張り替え
- **ボーンの自動対応付け** — `Upper Leg.L`（Blender式）と `UpperLeg_L`（Unity式）のような
  命名規則の違いや、階層の深さの違いを吸収
- **コンストレイントの再ベイク** — スカート等のRotation Constraintを複製先の骨格に合わせ直す
- アバターB用のFBXを用意するだけで対応版が作れます

## インストール方法

### VCC（VRChat Creator Companion）経由
1. VCCを開き「Settings > Packages > Add Repository」を選択
2. 以下のURLを追加：
   `https://wagunasu812.github.io/vpm-listing/index.json`
3. プロジェクトの「Manage Project」から「Material Duplicator」を追加

### unitypackage経由
1. [Releases](https://github.com/wagunasu812/materialduplicator/releases) から最新の `.unitypackage` をダウンロード
2. UnityのProjectウィンドウにドラッグ＆ドロップ
3. 「Import」をクリック

## 使い方
Unityのメニューバーから `Tools > Material Duplicator` または `Tools > Folder Duplicator` を選択

詳しい手順は **[使用マニュアル（PDF）](docs/MaterialDuplicator_Manual.pdf)** を参照してください。

## 動作環境
- Unity 2019.4 以降

## ライセンス
MIT License

---
Made with [Claude Code](https://claude.ai/code)