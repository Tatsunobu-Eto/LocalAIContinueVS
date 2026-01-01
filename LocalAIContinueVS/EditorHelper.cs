using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System.IO;
using System;
using System.Windows;

namespace LocalAIContinueVS
{
    /// <summary>
    /// 高速なエディタ操作を提供するヘルパークラス
    /// </summary>
    /// <summary>
    /// Visual Studio のエディタ操作を支援するヘルパークラスです。
    /// テキストの挿入、選択範囲の取得、差分表示などの機能を提供します。
    /// </summary>
    internal static class EditorHelper
    {
        /// <summary>
        /// Diff（差分）表示ウィンドウを開いている間に、AIが生成したコードを一時的に保持するための変数。
        /// ユーザーが適用（Apply）を選択した際に、このコードをエディタに反映させます。
        /// </summary>
        private static string _pendingCode = null;

        /// <summary>
        /// 現在アクティブなエディタのWPFビューを取得します。
        /// </summary>
        /// <returns>アクティブな IWpfTextView オブジェクト。取得できない場合は null。</returns>
        public static IWpfTextView GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var componentModel = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));
            if (componentModel == null) return null;

            var editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
            if (textManager == null) return null;

            textManager.GetActiveView(1, null, out var ivsTextView);
            if (ivsTextView == null) return null;

            return editorAdapter.GetWpfTextView(ivsTextView);
        }

        /// <summary>
        /// 選択範囲、またはカーソル位置にテキストを高速挿入/置換します。
        /// </summary>
        public static void FastReplaceSelection(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = GetActiveTextView();
            if (view == null) return;

            using (var edit = view.TextBuffer.CreateEdit())
            {
                if (view.Selection.IsEmpty)
                {
                    edit.Insert(view.Caret.Position.BufferPosition, text);
                }
                else
                {
                    edit.Replace(view.Selection.SelectedSpans[0], text);
                }
                edit.Apply();
            }
        }

        /// <summary>
        /// 現在のファイルの内容と、AIが生成したコードを比較するための差分（Diff）ウィンドウを表示します。
        /// </summary>
        /// <param name="generatedCode">AIによって提案された新しいコード内容。</param>
        /// <param name="dte">Visual Studio のオートメーションオブジェクト。</param>
        public static void ShowDiffWindowWithConfirm(string generatedCode, EnvDTE.DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte.ActiveDocument == null) return;

            // 1. 保留中のコードを保存
            _pendingCode = generatedCode;

            // 2. 現在のファイルのパスを取得
            string currentFilePath = dte.ActiveDocument.FullName;
            string ext = Path.GetExtension(currentFilePath);

            // 3. 比較用にAIのコードを一時ファイルに保存
            string tempFile = Path.GetTempFileName();
            string tempFileWithExt = Path.ChangeExtension(tempFile, ext);
            if (File.Exists(tempFile)) File.Delete(tempFile);
            File.WriteAllText(tempFileWithExt, generatedCode);

            // 4. VSの差分サービスを取得してウィンドウを開く
            var diffService = (IVsDifferenceService)ServiceProvider.GlobalProvider.GetService(typeof(SVsDifferenceService));
            if (diffService != null)
            {
                diffService.OpenComparisonWindow2(
                    currentFilePath,
                    tempFileWithExt,
                    "AI Code Suggestion - Review Changes",
                    "Compare your code with AI suggestion",
                    "Current Code",
                    "AI Suggestion",
                    null,
                    null,
                    0
                );
            }
        }
        /// <summary>
        /// 差分確認で保留中（_pendingCode）となっていたコードを、エディタのアクティブな位置に適用します。
        /// </summary>
        public static void ApplyPendingChanges()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 適用すべきコードがない場合は中断
            if (string.IsNullOrEmpty(_pendingCode))
            {
                MessageBox.Show("No pending changes to apply.");
                return;
            }

            // アクティブなビュー、または直前に操作していたビューを取得して置換
            var view = GetActiveTextView();
            if (view != null)
            {
                // 選択範囲があればそこを、なければ全体を置換するなど、要件に合わせて調整
                // ここでは FastReplaceSelection と同じロジックを流用しますが、
                // Diff対象がファイル全体だった場合を考慮し、全置換するロジックにするのが一般的です。

                using (var edit = view.TextBuffer.CreateEdit())
                {
                    // 安全のため、選択範囲がある場合のみ置換、あるいはファイル全体を置換
                    // ここではシンプルに選択範囲またはカーソル位置への挿入とします
                    if (view.Selection.IsEmpty)
                    {
                        // 全置換を行いたい場合はこちら
                        // var span = new SnapshotSpan(view.TextBuffer.CurrentSnapshot, 0, view.TextBuffer.CurrentSnapshot.Length);
                        // edit.Replace(span, _pendingCode);

                        // 現在の実装（挿入）に合わせる場合:
                        edit.Insert(view.Caret.Position.BufferPosition, _pendingCode);
                    }
                    else
                    {
                        edit.Replace(view.Selection.SelectedSpans[0], _pendingCode);
                    }
                    edit.Apply();
                }

                // 適用後はクリア
                _pendingCode = null;
            }
        }
    }
}
