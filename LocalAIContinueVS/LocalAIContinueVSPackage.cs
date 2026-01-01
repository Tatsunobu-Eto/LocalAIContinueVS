using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using static LocalAIContinueVS.ChatWindow;
using Task = System.Threading.Tasks.Task;

namespace LocalAIContinueVS
{
    /// <summary>
    /// Visual Studio 拡張機能のメインパッケージクラスです。
    /// このクラスは拡張機能のライフサイクル（ロード、初期化、アンロード）を管理します。
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid("30F397B7-F8DB-4C72-AF6A-DECCE08EA7E2")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ChatWindow))]
    [ProvideOptionPage(typeof(GeneralOptions), "Continue", "General", 0, 0, true)]
    public sealed class LocalAIContinueVSPackage : AsyncPackage
    {
        /// <summary>
        /// パッケージのシングルトンインスタンスを保持します。
        /// 他のコンポーネントからパッケージの機能（オプションページの取得など）にアクセスするために使用されます。
        /// </summary>
        public static LocalAIContinueVSPackage Instance { get; private set; }

        /// <summary>
        /// パッケージが初期化される際に呼ばれる非同期メソッドです。
        /// Visual Studio の UI スレッドに切り替えて、コマンドやツールの初期化を行います。
        /// </summary>
        /// <param name="cancellationToken">初期化処理をキャンセルするためのトークン。</param>
        /// <param name="progress">初期化の進行状況を報告するためのオブジェクト。</param>
        /// <returns>初期化処理を表すタスク。</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // 他のクラスからアクセスできるように、初期化の早い段階でインスタンスを保持
            Instance = this;

            // Visual Studio のメイン（UI）スレッドへ切り替え
            // コマンドの初期化や UI 関連のサービス取得には UI スレッドが必要
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // チャットウィンドウを表示するためのコマンドを初期化
            await ChatWindowCommand.InitializeAsync(this);
        }
    }
}
