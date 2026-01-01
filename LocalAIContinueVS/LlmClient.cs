using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LocalAIContinueVS
{
    /// <summary>
    /// 対応するローカルLLMプロバイダーの種類
    /// </summary>
    public enum LlmProvider
    {
        /// <summary>Ollama (デフォルトポート: 11434)</summary>
        Ollama,
        /// <summary>LM Studio または OpenAI 互換サーバー (デフォルトポート: 1234)</summary>
        LmStudio
    }
    /// <summary>
    /// チャットのメッセージ内容を保持するデータクラスです。
    /// </summary>
    public class ChatMessage
    {
        /// <summary>役割（"user", "assistant", "system"）</summary>
        public string Role { get; set; }
        /// <summary>メッセージの本文</summary>
        public string Content { get; set; }
    }
    /// <summary>
    /// ローカルLLMサーバーとHTTP通信を行うクライアントクラス
    /// </summary>
    public class LlmClient
    {
        /// <summary>HTTP通信用のクライアントインスタンス</summary>
        private readonly HttpClient _httpClient;

        /// <summary>サーバーのベースURL</summary>
        public string BaseUrl { get; private set; }

        /// <summary>
        /// コンストラクタ。HTTPクライアントの初期化とセキュリティ設定を行います。
        /// </summary>
        /// <param name="baseUrl">サーバーのベースURL（例: http://localhost:11434）</param>
        public LlmClient(string baseUrl)
        {
            // URL末尾の不要なスラッシュを削除して正規化
            BaseUrl = baseUrl.TrimEnd('/');

            // セキュリティおよびネットワーク設定のカスタマイズ
            var handler = new HttpClientHandler
            {
                // エンタープライズ環境でのセキュリティ対策：
                // 社内プロキシ経由でローカルホストのデータが外部へ漏洩するのを防ぐため、プロバイダーを無効化
                UseProxy = false,
                Proxy = null
            };

            // HttpClientの初期化
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl),
                // 生成AIは巨大な回答やモデルのロードに時間がかかるため、
                // クライアントレベルのタイムアウトは無制限に設定し、キャンセルトークンで制御する
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        /// <summary>
        /// サーバーへの接続確認テストを行います。
        /// </summary>
        /// <param name="provider">プロバイダーの種類</param>
        /// <returns>接続成功時に true</returns>
        public async Task<bool> TestConnectionAsync(LlmProvider provider)
        {
            try
            {
                // 各プロバイダーが生存確認に使用する軽量なパスを選択
                string path = (provider == LlmProvider.Ollama) ? "/" : "/v1/models";
                if (provider == LlmProvider.LmStudio && BaseUrl.EndsWith("/v1"))
                {
                    path = "v1/models";
                }

                // ユーザー体験のため、接続確認は5秒でタイムアウトさせる
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    using (var response = await _httpClient.GetAsync(path, cts.Token))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                // タイムアウトや接続拒否が発生した場合は失敗として扱う
                return false;
            }
        }

        /// <summary>
        /// LLMにプロンプトを送信し、回答をリアルタイムでストリーミング受信します。
        /// </summary>
        /// <param name="provider">プロバイダーの種類（Ollama または LmStudio/OpenAI互換）</param>
        /// <param name="model">使用するモデル名</param>
        /// <param name="currentEnrichedPrompt">ファイル情報などが付与された現在のユーザー入力</param>
        /// <param name="history">過去の会話履歴。最新の入力は含まれません。</param>
        /// <param name="onChunkReceived">ストリーミング中に新しいテキスト断片を受信した際に実行されるアクション</param>
        /// <param name="cancellationToken">通信のキャンセルを制御するトークン</param>
        /// <returns>非同期処理のタスク</returns>
        public async Task ChatStreamAsync(
            LlmProvider provider,
            string model,
            string currentEnrichedPrompt,
            List<ChatMessage> history,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // システムプロンプト
            var systemPrompt = "You are an expert coding assistant integrated into Visual Studio. " +
                               "Provide concise, correct code snippets. " +
                               "When asked to refactor, output only the improved code block if possible.";

            string jsonRequestBody;
            string endpoint;

            // --- 送信メッセージの構築 ---
            // 履歴を元にAPIへ送る messages リストを作成
            var messages = new List<object>();

            // 1. システムプロンプト
            messages.Add(new { role = "system", content = systemPrompt });

            // 2. 過去の履歴（最後の1件＝現在のユーザー入力 は除く。これはenrichedPromptを使うため）
            // historyには既に「今回のユーザー入力」が追加されている前提の呼び出し元ロジックなので、
            // 最後の一つを除外してループさせます。
            for (int i = 0; i < history.Count - 1; i++)
            {
                messages.Add(new { role = history[i].Role, content = history[i].Content });
            }

            // 3. 今回の入力（ファイルコンテキストなどが付与された enrichedPrompt を使用）
            messages.Add(new { role = "user", content = currentEnrichedPrompt });

            // --- プロバイダーごとのリクエスト作成 ---
            if (provider == LlmProvider.Ollama)
            {
                // ★重要: 会話履歴を扱うため、Ollamaも /api/generate ではなく /api/chat を使用するように変更
                endpoint = "/api/chat";
                var requestBody = new
                {
                    model = model,
                    messages = messages, // リストを渡す
                    stream = true
                };
                jsonRequestBody = JsonConvert.SerializeObject(requestBody);
            }
            else
            {
                // LM Studio / OpenAI互換形式
                endpoint = "/v1/chat/completions";
                var requestBody = new
                {
                    model = model,
                    messages = messages, // リストを渡す
                    stream = true
                };
                jsonRequestBody = JsonConvert.SerializeObject(requestBody);
            }

            var content = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");

            // URLのパス補正
            string requestUri = endpoint;
            if (BaseUrl.EndsWith("/v1") && endpoint.StartsWith("/v1"))
            {
                requestUri = endpoint.Substring(3);
            }

            // --- 送信処理 ---
            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content })
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errorJson = await response.Content.ReadAsStringAsync();
                    // ... エラーハンドリング (元のコード同様) ...
                    throw new Exception($"API Error: {response.StatusCode} - {errorJson}");
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                using (cancellationToken.Register(() => response.Dispose()))
                {
                    try
                    {
                        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            string chunk = null;

                            if (provider == LlmProvider.Ollama)
                            {
                                // Ollama (/api/chat) のレスポンス形式
                                try
                                {
                                    var json = JObject.Parse(line);
                                    // /api/generate は "response" ですが、/api/chat は "message.content" です
                                    chunk = json["message"]?["content"]?.ToString();
                                    if (json["done"]?.Value<bool>() == true) break;
                                }
                                catch { }
                            }
                            else
                            {
                                // LM Studio / OpenAI (変更なし)
                                if (line.StartsWith("data: "))
                                {
                                    var data = line.Substring(6).Trim();
                                    if (data == "[DONE]") break;
                                    try
                                    {
                                        var json = JObject.Parse(data);
                                        chunk = json["choices"]?[0]?["delta"]?["content"]?.ToString();
                                    }
                                    catch { }
                                }
                            }

                            if (!string.IsNullOrEmpty(chunk))
                            {
                                onChunkReceived(chunk);
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                        throw;
                    }
                    catch (IOException) // Broken pipe対策
                    {
                        if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();
                        throw;
                    }
                }
            }
        }

    }
}
