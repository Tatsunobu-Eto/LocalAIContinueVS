using Microsoft.VisualStudio.Shell;
using System.ComponentModel;

namespace LocalAIContinueVS
{
    /// <summary>
    /// Visual Studio の「ツール」>「オプション」メニューに表示される設定ページを定義するクラスです。
    /// ローカルLLMサーバーの接続情報を保持します。
    /// </summary>
    public class GeneralOptions : DialogPage
    {
        /// <summary>
        /// ローカルLLMサーバーのベースURL。
        /// </summary>
        [Category("Local LLM Settings")]
        [DisplayName("Base URL")]
        [Description("ローカルLLMサーバーのエンドポイント (例: http://localhost:11434)")]
        [DefaultValue("http://localhost:11434")]
        public string BaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>
        /// チャットで使用するモデル名。
        /// </summary>
        [Category("Local LLM Settings")]
        [DisplayName("Chat Model")]
        [Description("チャットに使用するモデル名 (例: gemma3:1b, mistral)")]
        [DefaultValue("gemma3:1b")]
        public string ChatModel { get; set; } = "gemma3:1b";
    }
}
