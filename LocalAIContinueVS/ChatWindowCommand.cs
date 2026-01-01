using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace LocalAIContinueVS
{
    /// <summary>
    /// Visual Studio のメニュー（表示 > その他のウィンドウ など）に表示されるコマンドを管理するクラスです。
    /// ユーザーがメニュー項目を選択した際にツールウィンドウを表示する役割を担います。
    /// </summary>
    internal sealed class ChatWindowCommand
    {
        /// <summary>
        /// コマンドのIDです（.vsctファイル内のIDと一致させる必要があります）。
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// コマンドセットのGUIDです（.vsctファイル内のGuidと一致させる必要があります）。
        /// [Guid("2B7E8BC4-D6A5-46BF-A5E6-2ADDCC111E72")]
        /// パッケージ作成時に生成されたGUIDをここに設定してください。
        /// </summary>
        public static readonly Guid CommandSet = new Guid("2B7E8BC4-D6A5-46BF-A5E6-2ADDCC111E72");

        /// <summary>
        /// VS Package (拡張機能本体) への参照。
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// コンストラクタ。
        /// ここでコマンドサービスにメニュー項目を登録します。
        /// </summary>
        private ChatWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            // コマンドIDとGUIDを紐付けて、実行時の動作(Execute)を登録
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// シングルトンインスタンス。
        /// </summary>
        public static ChatWindowCommand Instance { get; private set; }

        /// <summary>
        /// この拡張機能のサービスプロバイダ。
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get { return this.package; }
        }

        /// <summary>
        /// コマンドの初期化を行います。PackageクラスのInitializeAsyncから呼び出されます。
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // メインスレッド（UIスレッド）に切り替える
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            // コマンドサービス（メニュー管理機能）を取得
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            // インスタンス作成
            Instance = new ChatWindowCommand(package, commandService);
        }

        /// <summary>
        /// ユーザーがメニュー項目をクリックしたときに実行されます。
        /// チャットウィンドウを表示し、フォーカスを合わせます。
        /// </summary>
        /// <param name="sender">イベントの送信元（メニュー項目）</param>
        /// <param name="e">イベントデータ</param>
        private void Execute(object sender, EventArgs e)
        {
            // 非同期でツールウィンドウを表示
            _ = this.package.JoinableTaskFactory.RunAsync(async delegate
            {
                // メインスレッドでツールウィンドウを検索、存在しない場合は作成する
                ToolWindowPane window = await this.package.ShowToolWindowAsync(typeof(ChatWindow), 0, true, this.package.DisposalToken);

                if ((null == window) || (null == window.Frame))
                {
                    throw new NotSupportedException("ツールウィンドウを作成できませんでした。");
                }

                // ここまでは「ウィンドウオブジェクトの作成」まで。
                // 実際にVS上で表示・フォーカスするには IVsWindowFrame を操作する必要があるが、
                // ShowToolWindowAsync が内部でやってくれるため、通常はこれだけでOK。
            });
        }
    }
}
