# UnityLightmapToResonite

---
## 🇬🇧 English(日本語説明は下にあります)

### Overview
The **Unlit Lightmap Generator** converts Unity baked‑lightmap objects into UV1‑based *unlit* meshes that can be used on platforms that **do not support secondary lightmap UVs (UV2)**—for example **Resonite**.

### Key Features
- 🔄 **UV2 ➜ UV1 baking** – keeps the baked lighting exactly as in Unity.
- 📦 **Per‑lightmap mesh merging** – combines objects that share the same lightmap material, reducing draw calls dramatically.
- 🏗 **Hierarchy preservation or flat export** – keep the original transform tree or flatten everything.
- 🧹 **Automatic clean‑up** – removes empty helper objects after merging.
- 🌐 **Bilingual editor GUI** – switches between *English* and *Japanese* automatically based on the Unity Editor language.
- 🔧 **Debug tools** – verbose logging, texture dump, pre‑flight lightmap info.

### Requirements
| Item | Version |
|------|---------|
| Unity | 2019.4 LTS or newer (tested up to 2022.3) |
| Scripting Runtime | .NET 4.x |
| Rendering | Built‑in Render Pipeline (URP & HDRP not officially supported) |

### Installation
1. Copy the `ResoniteTools` folder (or the individual `*.cs` files) into **`Assets/Editor/`** in your Unity project.
2. Wait for Unity to re‑compile; a new menu item **Tools ▸ Generate Unlit Lightmap Objects** will appear.

**Alternative Installation (Recommended)**
1. Download the latest `.unitypackage` from the [GitHub Releases](https://github.com/rabbuttz/UnityLightmapToResonite/releases/) page.
2. Open your Unity project.
3. Double-click the downloaded `.unitypackage` file to import it into your project.
4. Click "Import" in the dialog that appears.
5. Wait for Unity to re‑compile; a new menu item **Tools ▸ Generate Unlit Lightmap Objects** will appear.

### Usage – Quick Start
1. Bake lighting in Unity as usual.
2. Select a root GameObject that contains the baked meshes.
3. Open **Tools ▸ Generate Unlit Lightmap Objects**.
4. Adjust options:
   - **Target Root**: selected object
   - **Process Child Objects**: on/off
   - *(Optional)* **Combine Same‑Lightmap Meshes** for maximum optimisation
5. Click **Generate Unlit Lightmap Objects**.
6. The tool creates a new GameObject named `<TargetRoot>_UnlitLM_Group` containing the unlit meshes.

### Export & Import to Resonite
1. **Install UniVRM** – download the latest `UniVRM‑*.unitypackage` from the [official repository]([https://github.com/ousttrue/UniGLTF](https://github.com/vrm-c/UniVRM)) or grab v0.128.3 directly: <https://github.com/vrm-c/UniVRM/releases/download/v0.128.3/UniVRM-0.128.3_b045.unitypackage>.
2. In Unity, use **UniGLTF ▸ Export** to export the generated lightmap meshes.  
   • If you also want the *original* shaded meshes, select both the original meshes and the generated lightmap meshes before exporting.
3. Import the resulting **.glb** file(s) into Resonite.
4. Inside Resonite, adjust each material on the lightmap meshes:
   - **Shader**: *Unlit*
   - **Blend Mode**: *Multiply*
   - **Sidedness**: *Front* (recommended in most cases)
   - **OffsetFactor**: *-0.1*
   - Set **PreferredFormat** to *BC7_LZMA* or *BC6H_LZMA* for optimal texture quality
5. Because the lightmap meshes share the same pivot as their originals, simply parent each lightmap mesh under its corresponding original object and reset **Position** and **Rotation** to `0`—they will align perfectly.

### GUI Options Explained
| Section | Option | What it does |
|---------|--------|--------------|
| Target Settings | Process Child Objects | Recursively scan children |
| Advanced Options | Vertex Offset | Push duplicated mesh along normals to avoid Z‑fighting |
|  | Share Materials | Re‑use the same unlit material for objects that share a lightmap |
|  | Add Dither Noise | Optional per‑pixel noise to break banding |
| Hierarchy Options | Preserve Hierarchy | Clone the original transform tree |
|  | Combine All Meshes | Merge everything into one multi‑submesh object |
|  | Combine Same‑Lightmap Meshes | Merge meshes that share the same unlit material |
| Debug Options | Verbose Logging | Print extra details to the Console |

### Logs & Debugging
Enable **Verbose Logging** to see step‑by‑step progress. The tool will output messages such as:
```
[LM‑Combine] Lightmap‑0: 12 meshes → 1 (18 456 verts)
```

### Known Limitations
- SkinnedMeshRenderer support is basic (no bone optimisation).
- URP/HDRP materials are converted but the appearance may differ.
- Only the built‑in *Unlit/Texture*, *Unlit/Transparent*, and a minimal *Cutout* shader are generated.

### Important Note on Z-Fighting
**It is recommended to avoid using the Vertex Offset feature.** Instead, adjust the `OffsetFactor` value within Resonite to prevent Z-fighting. The Vertex Offset feature is planned to be removed in future versions.

### License
MIT License – see `LICENSE` file.

---
## 🇯🇵 日本語

### 概要
**Unlit Lightmap Generator** は、Unity でベイクしたライトマップ付きオブジェクトを **UV1 ベースのアンリットメッシュ** に変換するエディタ拡張です。**UV2 をサポートしないプラットフォーム（例：Resonite）** で、Unity と同じ見た目のライティングを再現できます。

### 特長
- 🔄 **UV2 → UV1 変換** — ベイク結果をそのまま転写
- 📦 **ライトマップごとのメッシュ結合** — 同じライトマップを共有するメッシュを自動で 1 つにまとめ、ドローコールを削減
- 🏗 **階層の保持 or フラット出力** — 元の Transform ツリーを残すことも、平坦化することも可能
- 🧹 **不要オブジェクトの自動削除** — メッシュを持たない空ノードをクリーンアップ
- 🌐 **日英バイリンガル GUI** — Unity エディタの言語設定に合わせて表示を切替
- 🔧 **デバッグツール** — 詳細ログ、ライトマップ情報出力、テクスチャ保存 など

### 動作環境
| 項目 | バージョン |
|------|------------|
| Unity | 2019.4 LTS 以上（2022.3 で動作確認）|
| スクリプティングランタイム | .NET 4.x |
| レンダリング | Built‑in RP 推奨（URP/HDRP は暫定対応）|

### インストール手順
1. `ResoniteTools` フォルダ（または *.cs ファイル一式）を **`Assets/Editor/`** にコピー。
2. Unity のコンパイル完了後、メニュー **Tools ▸ Generate Unlit Lightmap Objects** が追加されます。

**代替インストール方法（推奨）**
1. [GitHub Releases](https://github.com/rabbuttz/UnityLightmapToResonite/releases/) ページから最新の `.unitypackage` をダウンロード。
2. Unityプロジェクトを開く。
3. ダウンロードした `.unitypackage` ファイルをダブルクリックしてプロジェクトにインポート。
4. 表示されるダイアログで「Import」をクリック。
5. Unity のコンパイル完了後、メニュー **Tools ▸ Generate Unlit Lightmap Objects** が追加されます。

### 使い方
1. Unity でライトマップをベイク。
2. 対象ルートオブジェクトを選択。
3. **Tools ▸ Generate Unlit Lightmap Objects** を開く。
4. オプションを設定：
   - **Target Root** : 選択したオブジェクト
   - **Process Child Objects** : 子オブジェクトを含めるか
   - **Combine Same‑Lightmap Meshes** : 最適化したい場合 ON
5. **Generate Unlit Lightmap Objects** をクリック。
6. `<TargetRoot>_UnlitLM_Group` という新しい GameObject に結果が生成されます。

### Resonite へのエクスポート手順
1. **UniVRM をインストール** — [公式リポジトリ]([https://github.com/ousttrue/UniGLTF](https://github.com/vrm-c/UniVRM)) から最新 `UniVRM‑*.unitypackage` を取得するか、v0.128.3 を直接ダウンロード: <https://github.com/vrm-c/UniVRM/releases/download/v0.128.3/UniVRM-0.128.3_b045.unitypackage>。
2. Unity で **UniGLTF ▸ Export** を実行し、生成されたライトマップメッシュを glb で出力。  
   • *元のメッシュ* も一緒に使いたい場合は、ライトマップメッシュと元メッシュの両方を選択してエクスポートします。
3. 出力された **.glb** を Resonite にインポート。
4. Resonite 内でライトマップメッシュの各マテリアルを設定：
   - **Shader**: *Unlit*
   - **Blend Mode**: *Multiply*
   - **Sidedness**: *Front*（推奨）
   - **OffsetFactor**: *-0.1*（推奨）
   - **PreferredFormat** を *BC7_LZMA* または *BC6H_LZMA* に設定（最適なテクスチャ品質のため）
5. ライトマップメッシュは元オブジェクトと同じ中心点を持つため、子オブジェクトにして **Position / Rotation** を 0 にリセットすればピッタリ重なります。

### GUI 項目
| セクション | オプション | 機能 |
|------------|-----------|------|
| Target Settings | Process Child Objects | 子オブジェクトを再帰検索 |
| Advanced Options | Vertex Offset | 頂点を法線方向にオフセットして Z‑ファイト防止 |
|  | Share Materials | 同一ライトマップの場合、マテリアルを共有 |
|  | Add Dither Noise | バンディング抑制のノイズ追加 |
| Hierarchy Options | Preserve Hierarchy | 元の階層構造を複製 |
|  | Combine All Meshes | すべてを 1 つのメッシュに統合 |
|  | Combine Same‑Lightmap Meshes | 同一アンリットマテリアル単位で統合 |
| Debug Options | Verbose Logging | 詳細ログを表示 |

### デバッグ
**Verbose Logging** を有効にすると処理ステップが Console に出力されます：
```
[LM‑Combine] Lightmap‑0: 12 meshes → 1 (18 456 verts)
```

### 既知の制限
- SkinnedMeshRenderer でのボーン最適化は未実装
- URP/HDRP 素材は見た目が多少変わる場合あり
- 生成されるシェーダは Unlit 系最小構成

### Z-ファイト防止に関する重要な注意
**頂点オフセット機能の使用は推奨されていません。** 代わりに、Resonite内で `OffsetFactor` の値を-0.1などに調整することでZ-ファイトを防止してください。頂点オフセット機能は将来のバージョンで削除される予定です。

### ライセンス
MIT License

