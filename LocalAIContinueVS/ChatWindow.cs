using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LocalAIContinueVS
{
    /// <summary>
    /// Visual Studio のツールウィンドウを定義するクラスです。
    /// このウィンドウは ChatWindowControl (WPF) をホストします。
    /// </summary>
    [Guid("2AF46DE1-3419-407F-A458-6448CCB8C432")]
    public class ChatWindow : ToolWindowPane
    {
        /// <summary>
        /// クラスの新しいインスタンスを初期化します。
        /// </summary>
        public ChatWindow() : base(null)
        {
            // ツールウィンドウのタイトルバーに表示されるテキスト
            this.Caption = "Local AI Assistant";

            // ウィンドウのコンテンツとして WPF の ChatWindowControl を設定
            this.Content = new ChatWindowControl();
        }
    }
}
