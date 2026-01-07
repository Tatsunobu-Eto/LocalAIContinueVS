using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Task = System.Threading.Tasks.Task;

namespace LocalAIContinueVS
{
    /// <summary>
    /// チャットウィンドウの UI ロジックを管理するクラスです。
    /// WebView2 をホストし、VS エディタと LLM クライアント間の通信を仲介します。
    /// </summary>
    public partial class ChatWindowControl : UserControl
    {
        /// <summary>LLMサーバーと通信するクライアント</summary>
        private LlmClient _client;
        /// <summary>Visual Studio のオートメーションオブジェクト (DTE)</summary>
        private DTE2 _dte;
        /// <summary>現在選択されているプロバイダー (Ollama or LmStudio)</summary>
        private LlmProvider _currentProvider = LlmProvider.Ollama;
        /// <summary>AI生成をキャンセルするためのソース</summary>
        private CancellationTokenSource _generationCts;

        /// <summary>会話履歴を保持するリスト</summary>
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        /// <summary>履歴保存用のファイルパス</summary>
        private string _historyFilePath;

        /// <summary>
        /// クラスの新しいインスタンスを初期化します。
        /// </summary>
        public ChatWindowControl()
        {
            InitializeComponent();

            _historyFilePath = Path.Combine(Path.GetTempPath(), "LocalAIContinueVS_History.json");

#pragma warning disable VSSDK006
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await InitializeWebViewAsync();
            }).FileAndForget("LocalAIContinueVS/InitializeWebView");
#pragma warning restore VSSDK006
        }

        /// <summary>
        /// WebView2 エディタ環境を初期化し、HTML コンテンツをロードします。
        /// </summary>
        private async Task InitializeWebViewAsync()
        {
            try
            {
                // UI スレッドでの実行を保証（WebView2 や VS API の操作に必要）
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // VS のコアサービス (DTE) を取得。エディタ操作やプロジェクト参照に使用
                _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;

                // WebView2 のユーザーデータフォルダを一意に作成（複数インスタンス時の干渉防止）
                string uniqueId = Guid.NewGuid().ToString();
                string userDataFolder = Path.Combine(Path.GetTempPath(), $"LocalAIContinueVS_Temp_{uniqueId}");

                // WebView2 環境の構築とコントロールの初期化
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await ChatWebView.EnsureCoreWebView2Async(env);

                // デフォルトの接続設定
                string initUrl = "http://localhost:11434";
                string initModel = "llama3";

                // パッケージのインスタンスが存在する場合、VS のオプション設定から値を読み込む
                if (LocalAIContinueVSPackage.Instance != null)
                {
                    try
                    {
                        // 「ツール > オプション」でユーザーが設定した値を取得
                        var options = (GeneralOptions)LocalAIContinueVSPackage.Instance.GetDialogPage(typeof(GeneralOptions));
                        
                        // 設定値が空でない場合のみ、初期値を上書き
                        if (!string.IsNullOrEmpty(options.BaseUrl)) initUrl = options.BaseUrl;
                        if (!string.IsNullOrEmpty(options.ChatModel)) initModel = options.ChatModel;
                    }
                    catch { 
                        // オプション取得失敗時はデフォルト値を使用
                    }
                }

                // ページロード完了時のイベントを登録
                ChatWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                // チャットUIのHTMLを生成して WebView2 に表示
                ChatWebView.NavigateToString(ChatUiGenerator.GetHtml(initUrl, initModel));

                // JavaScript からのメッセージ受信イベントを登録
                ChatWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }
            catch (Exception ex)
            {
                // 初期化失敗時はユーザーに通知
                MessageBox.Show($"WebView2 Init Error: {ex.Message}");
            }
        }

        /// <summary>
        /// WebView2 のナビゲーション（ロード）完了時に呼び出されます。
        /// 保存された履歴の読み込みと、JavaScript 側へのファイルリスト送信を開始します。
        /// </summary>
        /// <param name="sender">イベント送信元</param>
        /// <param name="e">ナビゲーション完了イベント引数</param>
        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // 非同期で初期化後のデータロードを実行
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                // 保存されている過去のチャット履歴をWebView2に復元
                await LoadHistoryAsync();
                // ソリューション内の全ファイルをスキャンし、JS側のオートコンプリート用に送信
                await SendFileListToJsAsync();
            }).FileAndForget("LocalAIContinueVS/LoadHistoryAndFiles");
        }
        /// <summary>
        /// 現在のソリューションに含まれる全ファイルのリストを再帰的に取得し、
        /// JavaScript 側のオートコンプリート機能（@参照用）に送信します。
        /// </summary>
        private async Task SendFileListToJsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var fileList = new List<string>();

            // ソリューションが開かれており、プロジェクトが存在するか確認
            if (_dte.Solution != null && _dte.Solution.Projects != null)
            {
                // ソリューション内の各プロジェクトを走査
                foreach (Project project in _dte.Solution.Projects)
                {
                    // プロジェクト内のアイテム（ファイル/フォルダ）を再帰的にスキャン
                    ScanProjectItems(project.ProjectItems, fileList, project.Name);
                }
            }

            // 取得したファイル名リストを JSON 文字列に変換
            var json = JsonConvert.SerializeObject(fileList);
            
            // JavaScript 側の updateFileList 関数を呼び出してデータを渡す
            ExecuteJs($"updateFileList({json})");
        }
        /// <summary>
        /// プロジェクト内のアイテム（ファイル、フォルダ、サブプロジェクト）を再帰的にスキャンし、
        /// 有効なファイルのリストを作成します。
        /// </summary>
        /// <param name="items">スキャン対象のアイテムコレクション</param>
        /// <param name="fileList">ファイル名を格納するリスト</param>
        /// <param name="parentPath">現在のスキャン対象までの論理パス（ログ用）</param>
        private void ScanProjectItems(ProjectItems items, List<string> fileList, string parentPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return;

            foreach (ProjectItem item in items)
            {
                // --- 1. ファイルとしての登録処理 ---
                string filePath = null;
                try
                {
                    // ItemOperationsなどではなく、実ファイルとしてのパスを取得を試みる
                    if (item.FileCount > 0) filePath = item.FileNames[1];
                }
                catch { }

                // 実ファイルが存在する場合のみリストに追加
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    fileList.Add(item.Name);
                }

                // --- 2. 再帰的なフォルダ探索 ---

                // ソリューションフォルダやサブプロジェクトが含まれる場合
                if (item.SubProject != null)
                {
                    ScanProjectItems(item.SubProject.ProjectItems, fileList, parentPath + "/" + item.Name);
                }
                // 通常のプロジェクト内フォルダの場合
                else if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                {
                    ScanProjectItems(item.ProjectItems, fileList, parentPath + "/" + item.Name);
                }
            }
        }
        /// <summary>
        /// WebView2 内の JavaScript から window.chrome.webview.postMessage を通じてメッセージが送信された際に呼び出されます。
        /// </summary>
        /// <param name="sender">イベント送信元</param>
        /// <param name="e">受信したメッセージを含むイベント引数</param>
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string rawMsg;
            try
            {
                // メッセージを文字列として取得。JSON形式などの解析は HandleWebMessageAsync 内で行う。
                rawMsg = e.TryGetWebMessageAsString();
            }
            catch { return; }

            // VS の非同期機構を使用してメッセージハンドラを実行
#pragma warning disable VSSDK006
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await HandleWebMessageAsync(rawMsg);
            }).FileAndForget("LocalAIContinueVS/WebMessageReceived");
#pragma warning restore VSSDK006
        }

        /// <summary>
        /// WebView2 内の JavaScript から送信されたメッセージ（コマンド）を解析し、対応する C# 処理を実行します。
        /// </summary>
        /// <param name="rawMsg">JavaScript から送られた生の文字列データ</param>
        private async Task HandleWebMessageAsync(string rawMsg)
        {
            try
            {
                // 1. 生成キャンセル要求のチェック
                if (rawMsg == ChatCommands.Cancel)
                {
                    // 実行中の非同期タスクをキャンセル
                    _generationCts?.Cancel();
                    return;
                }

                // 2. 履歴クリア要求のチェック
                if (rawMsg == ChatCommands.Clear)
                {
                    // 履歴リストの初期化とファイル保存
                    await ClearHistoryAsync();
                    return;
                }

                // 3. LLM サーバーへの接続要求のチェック
                if (rawMsg.StartsWith(ChatCommands.Connect))
                {
                    // ペイロードを分離 (Provider, URL, Model)
                    var parts = SplitPayload(rawMsg, ChatCommands.Connect);
                    // 必要な引数が揃っている場合に接続確認を実行
                    if (parts.Length >= 3) await CheckConnectionAsync(parts[0], parts[1], parts[2]);
                }
                // 4. コード挿入要求のチェック
                else if (rawMsg.StartsWith(ChatCommands.Insert))
                {
                    // プレフィックスを除去した純粋なコードをエディタに挿入
                    await InsertCodeToEditorAsync(RemovePrefix(rawMsg, ChatCommands.Insert));
                }
                // 5. 差分（Diff）表示要求のチェック
                else if (rawMsg.StartsWith(ChatCommands.Replace))
                {
                    // VS の比較ウィンドウを起動
                    await ShowDiffCheckAsync(RemovePrefix(rawMsg, ChatCommands.Replace));
                }
                // 6. 差分適用（Apply）要求のチェック
                else if (rawMsg.StartsWith(ChatCommands.Apply))
                {
                    // 保留中のコードをエディタに反映
                    await InsertCodeToEditorAsync(RemovePrefix(rawMsg, ChatCommands.Apply));
                }
                // 7. 新規ファイル作成要求のチェック
                else if (rawMsg.StartsWith(ChatCommands.NewFile))
                {
                    // ペイロードを分離 (FileName, Code)
                    var parts = SplitPayload(rawMsg, ChatCommands.NewFile);
                    // ファイル名と内容が揃っている場合に作成実行
                    if (parts.Length >= 2) await CreateNewFileAsync(parts[0], parts[1]);
                }
                // 8. 上記以外は通常のチャットメッセージとして処理
                else
                {
                    await HandleChatAsync(rawMsg);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Message Handler Error: {ex.Message}");
            }
        }

        #region Logic Methods

        /// <summary>
        /// 提案されたコードを現在のエディタ上のコードと比較するための差分ウィンドウを表示します。
        /// </summary>
        /// <param name="code">AIが生成した新しいコード</param>
        private async Task ShowDiffCheckAsync(string code)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // EditorHelper を介して VS の差分表示サービスを呼び出す
            EditorHelper.ShowDiffWindowWithConfirm(code, _dte as EnvDTE.DTE);
        }
        /// <summary>
        /// ユーザーからのチャット入力を処理し、LLM へリクエストを送信します。
        /// コンテキスト（選択コードや @ファイル参照）の解決もここで行います。
        /// </summary>
        /// <param name="userPrompt">ユーザーが入力したプロンプト文字列</param>
        private async Task HandleChatAsync(string userPrompt)
        {
            string modelName = "llama3";
            string prompt = userPrompt;

            // プロンプトにセパレータが含まれている場合、モデル名と質問内容を分離
            if (userPrompt.Contains(ChatCommands.Separator))
            {
                var parts = userPrompt.Split(new[] { ChatCommands.Separator }, 2, StringSplitOptions.None);
                modelName = parts[0]; // JS側から送られた選択中のモデル名
                prompt = parts[1];    // ユーザーの質問本文
            }

            // ユーザーのメッセージを履歴に追加して保存
            _chatHistory.Add(new ChatMessage { Role = "user", Content = prompt });
            await SaveHistoryAsync();

            // プロンプト内の @ファイル参照 を解決して内容を展開
            string enrichedPrompt = await ResolveFileContextAsync(prompt);

            string selectedCode = "";
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // 現在エディタで選択されているテキスト（コード）を取得
            if (_dte != null && _dte.ActiveDocument != null && _dte.ActiveDocument.Selection is TextSelection selection)
            {
                selectedCode = selection.Text;
            }

            // 選択中のコードがある場合は、文脈としてプロンプトの先頭に付与
            if (!string.IsNullOrWhiteSpace(selectedCode))
            {
                enrichedPrompt = $"Context Code:\n```\n{selectedCode}\n```\n\nQuestion: {enrichedPrompt}";
            }

            // 新しいキャンセル用トークンを作成
            _generationCts = new CancellationTokenSource();
            var token = _generationCts.Token;

            // AIの回答を逐次蓄積するためのビルダー
            var aiResponseBuilder = new StringBuilder();

            // 非同期で LLM への通信を開始
            _ = Task.Run(async () =>
            {
                // クライアントが初期化されていない（未接続）場合はエラー表示して終了
                if (_client == null)
                {
                    ExecuteJs("showError('Not connected!')");
                    return;
                }

                // UI 側をストリーミング開始状態に移行
                ExecuteJs("startStream()");

                try
                {
                    // LLM クライアントを介してストリーミングリクエストを実行
                    await _client.ChatStreamAsync(
                       _currentProvider,
                       modelName,
                       enrichedPrompt,
                       _chatHistory, // 会話の文脈を維持するために履歴を渡す
                       (chunk) =>
                       {
                           // キャンセル要求があれば中断
                           if (token.IsCancellationRequested) return;
                           
                           // 受信したテキスト断片を蓄積
                           aiResponseBuilder.Append(chunk);
                           
                           // JS 内で安全に扱えるように文字列をエスケープして送信
                           string sanitized = System.Web.HttpUtility.JavaScriptStringEncode(chunk);
                           ExecuteJs($"streamChunk('{sanitized}')");

                       }, token
                    );

                    // 正常に終了した場合（キャンセルされていない場合）
                    if (!token.IsCancellationRequested)
                    {
                        // UI 側に完了を通知
                        ExecuteJs("endStream()");

                        // AI の最終回答を履歴に追加して保存
                        _chatHistory.Add(new ChatMessage { Role = "assistant", Content = aiResponseBuilder.ToString() });
                        await SaveHistoryAsync();
                    }
                    else
                    {
                        // キャンセルされた場合の UI 表示
                        ExecuteJs("cancelStreamUI()");
                    }
                }
                catch (Exception ex)
                {
                    // キャンセル例外か、それ以外の通信エラーかを判定
                    bool isCancelled = ex is OperationCanceledException || token.IsCancellationRequested;
                    if (isCancelled)
                    {
                        ExecuteJs("cancelStreamUI()");
                    }
                    else
                    {
                        // エラーメッセージを JS に送って表示
                        string errMsg = System.Web.HttpUtility.JavaScriptStringEncode(ex.Message);
                        ExecuteJs($"showError('{errMsg}')");
                    }
                }
                finally
                {
                    // 成功・失敗に関わらず、入力欄などの UI 制限を解除
                    ExecuteJs("setUiState(false)");
                }
            });
        }

        /// <summary>
        /// プロンプト内の @ファイル名 記述を検索し、該当するファイルの内容をコンテキストとして展開します。
        /// </summary>
        /// <param name="prompt">解析対象のプロンプト</param>
        /// <returns>ファイル内容が付与された展開後のプロンプト</returns>
        private async Task<string> ResolveFileContextAsync(string prompt)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // 正規表現で @ファイルを抽出
            // 元: @"@([\w\d\._-]+\.[a-zA-Z0-9]+)"
            var regex = new System.Text.RegularExpressions.Regex(@"@([\w\d\._\-\\/]+\.[a-zA-Z0-9]+)");

            var matches = regex.Matches(prompt);

            if (matches.Count == 0) return prompt;

            StringBuilder contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Below are the referenced files for context:\n");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string rawMatch = match.Groups[1].Value; // 例: "Folder/File.cs"

                // ▼ 修正2: FindProjectItem は「ファイル名のみ」で検索するため、パス部分を除去
                string searchFileName = System.IO.Path.GetFileName(rawMatch); // "File.cs" に変換

                ProjectItem item = null;
                try
                {
                    // ソリューション全体からファイル名で検索
                    item = _dte.Solution.FindProjectItem(searchFileName);
                }
                catch { }

                if (item != null)
                {
                    string path = null;
                    try
                    {
                        // ファイルの実パスを取得
                        path = item.FileNames[1];
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        // ★ 補足: もし厳密にフォルダパスも一致させたい場合は、ここで path と rawMatch を比較するロジックを追加可能
                        // 現状は「同名のファイル」が見つかればそれを採用する動きになります

                        string content = File.ReadAllText(path);
                        contextBuilder.AppendLine($"--- File: {rawMatch} ---");
                        contextBuilder.AppendLine(content);
                        contextBuilder.AppendLine("------------------------\n");
                    }
                }
            }

            contextBuilder.AppendLine("\nUser Question:");
            contextBuilder.AppendLine(prompt);

            return contextBuilder.ToString();
        }

        #region History Management

        /// <summary>
        /// 現在のチャット履歴（_chatHistory）をテンポラリディレクトリの JSON ファイルに保存します。
        /// </summary>
        private async Task SaveHistoryAsync()
        {
            try
            {
                // I/O 処理のため、バックグラウンドスレッドで実行
                await Task.Run(() =>
                {
                    string json = JsonConvert.SerializeObject(_chatHistory);
                    File.WriteAllText(_historyFilePath, json);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save history: {ex.Message}");
            }
        }

        /// <summary>
        /// JSON ファイルから過去のチャット履歴を読み込み、WebView2 側の UI に反映させます。
        /// </summary>
        private async Task LoadHistoryAsync()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    string json = "";
                    // バックグラウンドスレッドでファイル読み込みとパースを行う
                    await Task.Run(() =>
                    {
                        json = File.ReadAllText(_historyFilePath);
                        _chatHistory = JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
                    });

                    // 読み込んだ履歴を WebView2 に送信して表示を更新
                    string safeJson = JsonConvert.SerializeObject(_chatHistory);
                    ExecuteJs($"restoreHistory({safeJson})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        /// <summary>
        /// チャット履歴をメモリ上および保存ファイルから削除します。
        /// </summary>
        private async Task ClearHistoryAsync()
        {
            _chatHistory.Clear();
            await SaveHistoryAsync();
        }

        #endregion

        /// <summary>
        /// 指定された LLM サーバーへの接続確認を行い、成功した場合はクライアントを初期化します。
        /// </summary>
        /// <param name="providerStr">プロバイダー名 ("ollama" または "lmstudio")</param>
        /// <param name="url">サーバーのベースURL</param>
        /// <param name="model">使用するモデル名</param>
        /// <summary>
        /// 指定された LLM サーバーへの接続確認を行い、成功した場合はクライアントを初期化します。
        /// </summary>
        /// <param name="providerStr">プロバイダー名 ("ollama" または "lmstudio")</param>
        /// <param name="url">サーバーのベースURL</param>
        /// <param name="model">使用するモデル名</param>
        private async Task CheckConnectionAsync(string providerStr, string url, string model)
        {
            try
            {
                // 接続確認時に最新のファイルリストを再取得して JS に送る（同期的な利便性のため）
                await SendFileListToJsAsync();

                // バックグラウンドで接続テストを実行
                await TaskScheduler.Default;

                // 文字列からプロバイダー列挙型を判定
                LlmProvider provider = (providerStr == "lmstudio") ? LlmProvider.LmStudio : LlmProvider.Ollama;
                
                // URL の形式が正しいか検証
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri _)) throw new Exception("Invalid URL format.");

                // 一時的にクライアントを作成して疎通確認
                var tempClient = new LlmClient(url);
                bool isConnected = await tempClient.TestConnectionAsync(provider);

                if (isConnected)
                {
                    // 接続成功時、メインのクライアントとして確定
                    _client = tempClient;
                    _currentProvider = provider;
                    // JS 側に成功とモデル名を通知
                    ExecuteJs($"onConnectionResult(true, '{model}')");
                }
                else
                {
                    // サーバーからの応答がない場合
                    ExecuteJs($"onConnectionResult(false, '{model}', 'Connection timed out or refused.')");
                }
            }
            catch (Exception ex)
            {
                string safeMsg = System.Web.HttpUtility.JavaScriptStringEncode(ex.Message);
                ExecuteJs($"onConnectionResult(false, '{model}', 'Error: {safeMsg}')");
            }
        }

        /// <summary>
        /// AIが生成したコードを現在のアクティブなエディタの選択範囲（またはカーソル位置）に挿入します。
        /// 挿入後、Visual Studio の標準コマンドを使用してドキュメントをフォーマットします。
        /// </summary>
        /// <param name="code">挿入するコード文字列</param>
        private async Task InsertCodeToEditorAsync(string code)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // エディタのテキストバッファを直接書き換え
            EditorHelper.FastReplaceSelection(code);
            if (_dte != null)
            {
                // 挿入後にエディタにフォーカスを戻し、自動整形を実行
                _dte.ActiveDocument.Activate();
                _dte.ExecuteCommand("Edit.FormatSelection");
            }
        }

        /// <summary>
        /// 新しいファイルをプロジェクトに追加し、AIが生成したコードを書き込みます。
        /// </summary>
        /// <param name="fileName">作成するファイルの名前</param>
        /// <param name="code">ファイルに書き込む内容</param>
        private async Task CreateNewFileAsync(string fileName, string code)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                // 現在アクティブなプロジェクトを取得
                var projects = _dte != null ? _dte.ActiveSolutionProjects as Array : null;
                if (projects == null || projects.Length == 0)
                {
                    // プロジェクトが選択されていない、またはソリューションが開かれていない場合
                    MessageBox.Show("Project not found. Please select a project in Solution Explorer.");
                    return;
                }

                var activeProject = projects.GetValue(0) as Project;
                if (activeProject == null)
                {
                    MessageBox.Show("Project not found.");
                    return;
                }

                // プロジェクトのルートディレクトリを取得し、新しいファイルの絶対パスを生成
                string projectPath = Path.GetDirectoryName(activeProject.FullName);
                string fullPath = Path.Combine(projectPath, fileName);

                // ディスク上にファイルを書き込み
                File.WriteAllText(fullPath, code);
                
                // プロジェクトにファイルを登録（Solution Explorer に表示されるようになる）
                activeProject.ProjectItems.AddFromFile(fullPath);
                
                // 作成したファイルを VS エディタで開く
                _dte.ItemOperations.OpenFile(fullPath);
            }
            catch (Exception ex)
            {
                // パーミッションエラーやファイル名不正などの例外をキャッチ
                MessageBox.Show("Error creating file: " + ex.Message);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// WebView2 上で指定された JavaScript 文字列を実行します。
        /// </summary>
        /// <param name="script">実行する JS コード</param>
        private void ExecuteJs(string script)
        {
#pragma warning disable VSSDK006
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (ChatWebView != null && ChatWebView.CoreWebView2 != null)
                {
                    await ChatWebView.CoreWebView2.ExecuteScriptAsync(script);
                }
            }).FileAndForget("LocalAIContinueVS/ExecuteJs");
#pragma warning restore VSSDK006
        }

        /// <summary>
        /// 文字列から特定のプレフィックス（コマンド名）を削除します。
        /// </summary>
        private static string RemovePrefix(string msg, string prefix)
        {
            return msg.Substring(prefix.Length);
        }

        /// <summary>
        /// JavaScript から送られたペイロード（例: "CMD:Data1|||Data2"）をパースし、
        /// 各パラメータを配列として取得します。
        /// </summary>
        private static string[] SplitPayload(string msg, string prefix)
        {
            return RemovePrefix(msg, prefix)
                .Split(new[] { ChatCommands.Separator }, StringSplitOptions.None);
        }

        #endregion
    }

    internal static class ChatCommands
    {
        public const string Cancel = "CANCEL:";
        public const string Connect = "CONNECT:";
        public const string Insert = "INSERT:";
        public const string Replace = "REPLACE:";
        public const string Apply = "APPLY:"; // ★新規追加
        public const string NewFile = "NEWFILE:";
        public const string Clear = "CLEAR:";
        public const string Separator = "|||";
    }

    internal static class ChatUiGenerator
    {
        public static string GetHtml(string initUrl, string initModel)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceNamespace = "LocalAIContinueVS.Resources";

            string html = ReadResource(assembly, $"{resourceNamespace}.ChatWindow.html");
            string css = ReadResource(assembly, $"{resourceNamespace}.ChatWindow.css");
            string js = ReadResource(assembly, $"{resourceNamespace}.ChatWindow.js");
            string markedJs = ReadResource(assembly, $"{resourceNamespace}.marked.min.js");

            html = html.Replace("__CSS_CONTENT__", css);
            html = html.Replace("__JS_CONTENT__", js);
            html = html.Replace("__MARKED_JS__", markedJs);

            html = html.Replace("__INIT_URL__", initUrl);
            html = html.Replace("__INIT_MODEL__", initModel);

            html = html.Replace("__CMD_CONNECT__", ChatCommands.Connect);
            html = html.Replace("__CMD_CANCEL__", ChatCommands.Cancel);
            html = html.Replace("__CMD_INSERT__", ChatCommands.Insert);
            html = html.Replace("__CMD_REPLACE__", ChatCommands.Replace);
            html = html.Replace("__CMD_APPLY__", ChatCommands.Apply); // ★新規追加
            html = html.Replace("__CMD_NEWFILE__", ChatCommands.NewFile);
            html = html.Replace("__CMD_SEPARATOR__", ChatCommands.Separator);
            html = html.Replace("__CMD_CLEAR__", ChatCommands.Clear);

            return html;
        }

        private static string ReadResource(Assembly assembly, string resourceName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return "";
                using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }
    }
}
