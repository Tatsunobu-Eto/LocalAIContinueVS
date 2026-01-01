# LocalAIContinueVS

LocalAIContinueVS は、Visual Studio 内でローカル LLM（Ollama, LM Studio など）を直接利用できるようにする拡張機能です。チャットインターフェースを通じて、コードの生成、リファクタリング、解説をローカル環境で完結させることができます。

## 概要

このプロジェクトは、開発者がプライバシーを保ちつつ AI の力を活用できるように設計されています。外部の API を使用せず、ローカルで動作する LLM サーバーと通信するため、機密性の高いコードも安心して扱うことができます。

## 主な機能

-   **ローカル LLM 連携**: Ollama ( `/api/chat` ) や LM Studio ( OpenAI 互換 API ) をサポート。
-   **インラインコード挿入**: AI が提案したコードを現在のカーソル位置にワンクリックで挿入。
-   **差分表示 (Diff)**: 現在のコードと AI の提案を Visual Studio 標準の比較ウィンドウで確認し、納得した上で適用可能。
-   **ファイルコンテキスト参照**: `@ファイル名` と入力することで、プロジェクト内のファイルを自動的に読み込み、AI へのコンテキストとして提供。
-   **エディタ選択範囲の取得**: コードを選択した状態でチャットを送ると、その範囲を文脈として自動的にプロンプトに付与。
-   **チャット履歴の保存**: 会話内容は自動的に保存され、次回起動時に復元されます。

## 技術スタック

-   **Language**: C# 10.0+
-   **Framework**: .NET Framework 4.7.2 (Visual Studio 拡張機能の制約による)
-   **UI**: WPF + WebView2 (Microsoft Edge WebView2 Runtime)
-   **Frontend**: HTML5, CSS3, JavaScript (Vanilla JS)
-   **Libraries**: 
    -   Newtonsoft.Json (JSON のシリアライズ/デシリアライズ)
    -   Microsoft.VisualStudio.SDK (VS 拡張機能開発用)
-   **Supported Backends**: 
    -   Ollama
    -   LM Studio (またはその他 OpenAI 互換サーバー)

## セットアップと使用方法

1.  **前提条件**: 
    -   Visual Studio 2022 以降
    -   [Ollama](https://ollama.com/) または [LM Studio](https://lmstudio.ai/) がインストール・起動されていること。
2.  **設定**:
    -   Visual Studio の `ツール > オプション > Continue > General` から、サーバーの Base URL と使用するモデル名を設定します。
3.  **チャットの開始**:
    -   `表示 > その他のウィンドウ > Local AI Assistant` からチャットウィンドウを開きます。
    -   「Connect」ボタンを押してサーバーとの接続を確立します。
4.  **ファイル参照の使用**:
    -   入力欄で `@` を入力すると、ソリューション内のファイルが候補として表示されます。これを選択することで、ファイルの中身を AI に伝えることができます。

## プロジェクト構造

-   `ChatWindowControl.xaml.cs`: メインの UI ロジックと WebView2 の管理。
-   `LlmClient.cs`: ローカル LLM サーバーとの通信を抽象化。
-   `EditorHelper.cs`: Visual Studio エディタ操作（挿入、整形、差分表示）をカプセル化。
-   `Resources/`: チャット UI を構成する HTML, CSS, JavaScript ファイル。

## ライセンス

[MIT License](LICENSE)
