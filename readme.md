# win-boo-lib

WinUI 3 アプリ向けの共通ライブラリ集です。
各アプリから **Git サブモジュール** として取り込み、プロジェクト参照で利用します。

| プロジェクト | 名前空間 | 概要 | 依存 |
|---|---|---|---|
| `Utils` | `io.github.toyota32k.BooLib.Utils` | 汎用ユーティリティ | - |
| `Server` | `io.github.toyota32k.BooLib.Server` | HTTP/HTTPS サーバー、mDNS など | `Utils` |

## 動作環境

- .NET 10 (`net10.0-windows10.0.19041.0`, `TargetPlatformMinVersion` 10.0.17763.0)
- Windows App SDK 2.5.1 以上
- Visual Studio 2026

利用側アプリは、上記の TFM および Windows App SDK のバージョン以上である必要があります。

---

## 1. 新しいプロジェクトに win-boo-lib を追加する

### 1.1 サブモジュールとして追加

アプリのリポジトリのルートで実行します。配置先は `external/win-boo-lib` に統一します。

```powershell
cd <アプリのリポジトリ>
git submodule add https://github.com/toyota-m2k/win-boo-lib external/win-boo-lib
git commit -m "Add win-boo-lib submodule"
```

推奨設定（親リポジトリの push 時にサブモジュールの push 漏れを検出）:

```powershell
git config push.recurseSubmodules check
```

### 1.2 ソリューションに追加

Visual Studio でソリューションを右クリック → __追加 > 既存のプロジェクト__ で以下を追加します（必要なものだけでよい）。

- `external/win-boo-lib/Utils/Utils.csproj`
- `external/win-boo-lib/Server/Server.csproj`

`.slnx` に直接書く場合:

```xml
<Project Path="external/win-boo-lib/Server/Server.csproj" />
<Project Path="external/win-boo-lib/Utils/Utils.csproj" />
```

### 1.3 プロジェクト参照を追加

アプリのプロジェクトを右クリック → __追加 > プロジェクト参照__ でチェックを入れるか、`.csproj` に直接記述します。

```xml
<ItemGroup>
  <ProjectReference Include="..\external\win-boo-lib\Server\Server.csproj" />
  <ProjectReference Include="..\external\win-boo-lib\Utils\Utils.csproj" />
</ItemGroup>
```

パスはアプリの `.csproj` の位置からの相対パスです。

### 1.4 クローン時の注意

サブモジュールを含むリポジトリは、サブモジュールも一緒に取得する必要があります。

```powershell
# 新規クローン
git clone --recurse-submodules <アプリのリポジトリURL>

# 既存クローン（サブモジュールが空の場合）
git submodule update --init

# pull 時にサブモジュールも追従
git pull --recurse-submodules
```

---

## 2. win-boo-lib に新しいライブラリプロジェクトを追加する

ここでは例として `Media` ライブラリを追加します。

### 2.1 プロジェクトを作成

win-boo-lib のルートにフォルダーを作成し、既存の `Utils/Utils.csproj` をベースに `.csproj` を作成します。

```plaintext
win-boo-lib/
├─ Server/
├─ Utils/
└─ Media/            ← 新規
   └─ Media.csproj
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
	<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
	<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
	<RootNamespace>io.github.toyota32k.BooLib.Media</RootNamespace>
	<UseWinUI>true</UseWinUI>
	<WinUISDKReferences>false</WinUISDKReferences>
	<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
	<Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
	<PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2705" />
	<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
  </ItemGroup>
  <ItemGroup>
	<!-- 他のライブラリに依存する場合 -->
	<ProjectReference Include="..\Utils\Utils.csproj" />
  </ItemGroup>
</Project>
```

Visual Studio のテンプレート（__クラス ライブラリ (WinUI 3 in Desktop)__）で作成する場合も、作成後に上記の設定（TFM・`RootNamespace`・パッケージバージョン）に揃えてください。

### 2.2 規約

- フォルダー名 = プロジェクト名 = `RootNamespace` の末尾（`io.github.toyota32k.BooLib.<Name>`）
- TFM / `TargetPlatformMinVersion` / Windows App SDK のバージョンは全ライブラリで揃える
- ライブラリ間の参照は `..\<Name>\<Name>.csproj` の相対パスで行う（win-boo-lib の外を参照しない）
- 特定アプリに依存するコード（アプリ固有の設定・画面など）は含めない。必要ならインターフェイスやコールバックで外部から注入する

### 2.3 開発・コミット

ライブラリ単体のソリューションは持たないため、利用側アプリ（例: DukeBox）のソリューションにプロジェクトを追加して開発・ビルドします。

```powershell
cd <アプリ>\external\win-boo-lib
git switch main
git add Media
git commit -m "Add Media library"
git push
```

その後、利用側アプリでソリューション / `.csproj` に参照を追加し、サブモジュールの参照を更新してコミットします（[3.3](#33-利用側アプリでサブモジュールの参照を更新) 参照）。

---

## 3. win-boo-lib の修正・更新手順

### 3.0 仕組み

親リポジトリ（アプリ）は、サブモジュールの **コミット ID だけ** を記録しています。
そのため、ライブラリの修正は次の 2 段階で反映します。

1. win-boo-lib でコミット → push
2. 親リポジトリで「参照するコミット ID の更新」をコミット → push

**必ず 1 → 2 の順に push してください。** 1 を push せずに 2 を push すると、他の環境で存在しないコミットを参照してしまい `git submodule update` が失敗します。

### 3.1 作業前の確認

`git clone --recurse-submodules` や `git submodule update` で取得したサブモジュールは **detached HEAD**（どのブランチにも属さない状態）になっています。そのままコミットしないよう、必ずブランチに切り替えてから作業します。

```powershell
cd <アプリ>\external\win-boo-lib
git status            # "HEAD detached at ..." なら下記を実行
git switch main
git pull
```

### 3.2 win-boo-lib で修正をコミット・push

```powershell
cd <アプリ>\external\win-boo-lib
# ...編集...
git add -A
git commit -m "Utils: ○○を修正"
git push
```

### 3.3 利用側アプリでサブモジュールの参照を更新

```powershell
cd <アプリ>
git status            # "modified: external/win-boo-lib (new commits)" と表示される
git add external/win-boo-lib
git commit -m "Update win-boo-lib"
git push
```

### 3.4 ブランチを使った開発

サブモジュール内でも通常どおりブランチを作成できます。親リポジトリとブランチ名を揃えると対応関係が分かりやすくなります。

```powershell
cd <アプリ>\external\win-boo-lib
git switch -c feature/xxx
# ...編集・コミット...
git push -u origin feature/xxx

cd ..\..
git add external/win-boo-lib
git commit -m "win-boo-lib: feature/xxx を参照"
```

作業完了後は main にマージし、親リポジトリの参照を main 上のコミットに更新します。

```powershell
cd <アプリ>\external\win-boo-lib
git switch main
git merge feature/xxx
git push

cd ..\..
git add external/win-boo-lib
git commit -m "Update win-boo-lib (main)"
```

**注意:** 親リポジトリがフィーチャーブランチ上のコミットを参照している間に、そのブランチを削除したり squash マージ / rebase したりしないでください。参照先のコミットが消え、他の環境で取得できなくなります。

### 3.5 他のアプリで最新版を取り込む

他のアプリが参照するコミットは自動では更新されません。各アプリの都合のよいタイミングで取り込みます。

```powershell
cd <アプリ>\external\win-boo-lib
git switch main
git pull

cd ..\..
# ビルド・動作確認
git add external/win-boo-lib
git commit -m "Update win-boo-lib"
git push
```

または:

```powershell
git submodule update --remote external/win-boo-lib
```

### 3.6 Visual Studio での操作

Visual Studio は親リポジトリとサブモジュールを別々のリポジトリとして扱います。

1. __表示 > Git 変更__ を開く
2. 上部（またはステータスバー）のリポジトリ選択で **win-boo-lib** を選び、コミット → プッシュ
3. リポジトリを **アプリ側** に切り替え、`external/win-boo-lib` の変更をコミット → プッシュ

ブランチの作成・切り替えも、ステータスバーのブランチ名からリポジトリを選択して行えます。

---

## 4. 互換性に関する注意

- win-boo-lib は複数のアプリから利用されます。公開 API（public なクラス・メソッドのシグネチャ）を変更する場合は、利用側アプリへの影響を確認してください。
- 破壊的変更を行う場合は、コミットメッセージに `BREAKING:` を付けるなどして明示してください。
- TFM や Windows App SDK のバージョンを上げる場合は、全ライブラリで同時に上げ、利用側アプリも合わせて更新してください。
