# リバースエンジニアリングレポート: LocalAIContinueVS

## 1. プロジェクト概要
`LocalAIContinueVS` は、Visual Studio 用の拡張機能であり、ローカルで実行されている大型言語モデル（LLM）と連携して、コーディング支援を提供するツールです。Ollama や LM Studio などの API を活用し、チャット形式での対話、コード生成、リファクタリング、差分表示（Diff）などの機能を統合しています。

## 2. システムアーキテクチャ
本プロジェクトは、主に以下の 3 つのレイヤーで構成されています。

### 2.1. プレゼンテーション層 (UI)
- **WebView2**: WPF ウィンドウ内で HTML/JS/CSS を使用したリッチなチャット UI を提供します。
- **Resources**: `ChatWindow.html`, `ChatWindow.js`, `ChatWindow.css` が埋め込みリソースとして含まれています。

### 2.2. アプリケーションロジック層
- **ChatWindowControl.xaml.cs**: UI とロジックの仲介役。WebView2 からのメッセージを受信し、適切な C# 処理をディスパッチします。
- **LocalAIContinueVSPackage.cs**: 拡張機能のライフサイクル管理、設定ページ（GeneralOptions）の提供。

### 2.3. インフラストラクチャ・サービス層
- **LlmClient.cs**: Ollama および OpenAI 互換 API (LM Studio) との HTTP ストリーミング通信を実装。
- **EditorHelper.cs**: Visual Studio SDK を使用したエディタ操作（テキスト置換、差分表示）の実装。

## 3. 主要コンポーネントの詳細解析

### 3.1. LLM 連携 (LlmClient.cs)
- **プロバイダー対応**: `Ollama` と `LmStudio` の 2 種類をサポート。
- **通信仕様**:
  - `HttpClient` を使用し、タイムアウトを無制限に設定（生成 AI の特性に対応）。
  - ストリーミングレスポンスを逐次パースし、コールバックを通じて UI に通知。
  - Ollama の場合は `/api/chat`、OpenAI 互換の場合は `/v1/chat/completions` を使用。

### 3.2. Visual Studio 統合 (EditorHelper.cs / ChatWindowControl.xaml.cs)
- **エディタ操作**: `IWpfTextView` を取得し、`ITextBuffer` に対して直接編集（FastReplaceSelection）を行います。
- **差分表示**: `IVsDifferenceService` を使用して、現在編集中のファイルと AI 生成コードを比較する標準の Diff ウィンドウを表示します。
- **プロジェクトスキャン**: DTE オブジェクトを使用してソリューション内のファイルを再帰的に走査し、オートコンプリート用のリストを作成。
- **コンテキスト解決**: プロンプト内の `@filename` 記法を解析し、該当するファイルの内容を自動的にプロンプトに埋め込みます。

## 4. 通信シーケンス
1. **ユーザー入力**: JS 側で入力を受け取り、`window.chrome.webview.postMessage` で C# へ送信。
2. **コンテキスト収集**: C# 側でエディタの選択範囲と `@参照` ファイルの内容を取得。
3. **LLM リクエスト**: `LlmClient` がサーバーへ POST。
4. **ストリーミング**: サーバーからの断片（Chunk）を逐次 JS 側の `streamChunk` 関数へ転送。
5. **UI 更新**: JS 側で Markdown 描画（リソース内 JS で実装）。
6. **コマンド実行**: 「Apply」や「Insert」ボタンが押されると、JS から特定のプレフィックス（`INSERT:`, `APPLY:` 等）を付けて C# へ通知され、エディタ操作が実行される。

## 5. 技術的特徴と工夫
- **WebView2 の分離**: 各セッションごとにテンポラリフォルダを作成し、ユーザーデータを分離。
- **スレッド管理**: `JoinableTaskFactory` を使用して、UI スレッドとバックグラウンドスレッドを適切に切り替え、デッドロックを防止。
- **履歴管理**: `%TEMP%` フォルダに JSON 形式で会話履歴を保存し、ウィンドウを開き直しても会話を継続可能。

## 6. 結論
このプロジェクトは、Visual Studio SDK の高度な機能（DTE, Editor Services, WebView2 統合）をバランスよく組み合わせ、ローカル LLM という外部サービスをシームレスに IDE に統合しています。特に、差分表示によるレビュー機能や、`@` 参照によるコンテキスト注入は、実用的なコーディング支援を実現するための重要な要素となっています。
