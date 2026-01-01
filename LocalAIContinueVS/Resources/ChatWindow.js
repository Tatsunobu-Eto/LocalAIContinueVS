let currentModel = '';
let isStreaming = false;
let streamingMessageDiv = null;
let fullStreamingContent = '';
let startTime = 0;
let charCount = 0;

// C#から置換される定数
const CMD_CONNECT = "__CMD_CONNECT__";
const CMD_CANCEL = "__CMD_CANCEL__";
const CMD_INSERT = "__CMD_INSERT__";
const CMD_REPLACE = "__CMD_REPLACE__";
const CMD_APPLY = "__CMD_APPLY__"; // ★新規追加
const CMD_NEWFILE = "__CMD_NEWFILE__";
const CMD_SEPARATOR = "__CMD_SEPARATOR__";
const CMD_CLEAR = "__CMD_CLEAR__";

// 要素の参照
const history = document.getElementById('chat-history');
const input = document.getElementById('prompt');
const sendBtn = document.getElementById('send-btn');
const stopBtn = document.getElementById('stop-btn');
const statusDiv = document.getElementById('stream-status');

function updateDefaultPort() {
    const provider = document.getElementById('config-provider').value;
    const urlInput = document.getElementById('config-url');
    if (provider === 'lmstudio') {
        if (urlInput.value.includes('11434')) urlInput.value = 'http://localhost:1234';
    } else {
        if (urlInput.value.includes('1234')) urlInput.value = 'http://localhost:11434';
    }
}

function tryConnect() {
    const provider = document.getElementById('config-provider').value;
    const url = document.getElementById('config-url').value;
    const model = document.getElementById('config-model').value;
    const btn = document.getElementById('connect-btn');

    if (!url || !model) { document.getElementById('error-msg').innerText = 'Please enter URL and Model.'; return; }

    document.getElementById('error-msg').innerText = '';
    btn.disabled = true; btn.innerText = 'Connecting...';
    window.chrome.webview.postMessage(CMD_CONNECT + provider + CMD_SEPARATOR + url + CMD_SEPARATOR + model);
}

function returnToSetup() {
    document.getElementById('chat-screen').style.display = 'none';
    document.getElementById('setup-screen').style.display = 'flex';
    const btn = document.getElementById('connect-btn');
    btn.disabled = false; btn.innerText = 'Update Connection';
    document.getElementById('error-msg').innerText = '';
}

function onConnectionResult(success, modelName, errorMessage) {
    const btn = document.getElementById('connect-btn');
    if (success) {
        currentModel = modelName;
        document.getElementById('display-model-name').innerText = modelName;
        document.getElementById('setup-screen').style.display = 'none';
        document.getElementById('chat-screen').style.display = 'flex';
        btn.disabled = false; btn.innerText = 'Connect & Start';
    } else {
        document.getElementById('error-msg').innerText = errorMessage || 'Connection failed. Check Server.';
        btn.disabled = false; btn.innerText = 'Connect & Start';
    }
}

function checkEnter(e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
}

function setUiState(isGenerating) {
    if (isGenerating) {
        input.disabled = true;
        sendBtn.style.display = 'none'; stopBtn.style.display = 'block';
    } else {
        input.disabled = false;
        sendBtn.disabled = false; sendBtn.style.display = 'block'; stopBtn.style.display = 'none';
        input.focus();
    }
}

function sendMessage() {
    const text = input.value.trim();
    if (!text || isStreaming) return;

    addMessage(text, 'user', false);

    input.value = '';
    setUiState(true);
    window.chrome.webview.postMessage(currentModel + CMD_SEPARATOR + text);
}

function cancelGeneration() {
    if (isStreaming) {
        window.chrome.webview.postMessage(CMD_CANCEL);
        statusDiv.innerText = 'Cancelling...';
    }
}

function startStream() {
    isStreaming = true;
    fullStreamingContent = '';
    charCount = 0;
    startTime = Date.now();

    const div = document.createElement('div');
    div.className = 'message ai';
    history.appendChild(div);
    streamingMessageDiv = div;

    statusDiv.style.display = 'block';
    updateStatus();
}

function streamChunk(text) {
    if (!streamingMessageDiv) return;
    fullStreamingContent += text;
    charCount += text.length;

    let renderText = fullStreamingContent;
    const backtickCount = (renderText.match(/```/g) || []).length;
    if (backtickCount % 2 !== 0) renderText += '\n```';

    streamingMessageDiv.innerHTML = formatResponse(renderText);
    history.scrollTop = history.scrollHeight;
    updateStatus();
}

function endStream() {
    if (!streamingMessageDiv) return;
    streamingMessageDiv.innerHTML = formatResponse(fullStreamingContent);
    finishGeneration('Done');
}

function cancelStreamUI() {
    if (streamingMessageDiv) streamingMessageDiv.innerHTML = formatResponse(fullStreamingContent + ' [Cancelled]');
    finishGeneration('Generation Stopped');
}

function showError(text) {
    if (!streamingMessageDiv) {
        const div = document.createElement('div');
        div.className = 'message error';
        history.appendChild(div);
        streamingMessageDiv = div;
    } else {
        streamingMessageDiv.classList.add('error');
    }
    streamingMessageDiv.innerText = '⚠️ ' + text;
    finishGeneration('Error Occurred');
}

function finishGeneration(statusMsg) {
    isStreaming = false;
    streamingMessageDiv = null;
    const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
    statusDiv.innerText = `${statusMsg}. (${charCount} chars in ${elapsed}s)`;
    setTimeout(() => { if (!isStreaming) statusDiv.style.display = 'none'; }, 3000);
    setUiState(false);
    history.scrollTop = history.scrollHeight;
}

function updateStatus() {
    const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
    statusDiv.innerText = `Generating... | ${charCount} chars | ${elapsed}s elapsed`;
}

function addMessage(content, type, isHtml = false) {
    const div = document.createElement('div');
    div.className = 'message ' + type;
    if (isHtml) div.innerHTML = content;
    else div.innerText = content;
    history.appendChild(div);
    history.scrollTop = history.scrollHeight;
}

function formatResponse(text) {
    let safeText = escapeHtml(text);
    const codeBlocks = [];

    safeText = safeText.replace(/```(?:[a-zA-Z0-9#+\-\.]*)?(?:\r?\n)?([\s\S]*?)```/g, function (match, code) {
        code = code.replace(/^\n+|\n+$/g, '');
        codeBlocks.push(code);
        return '___CODE_BLOCK_' + (codeBlocks.length - 1) + '___';
    });

    safeText = safeText.replace(/`([^`\n]+)`/g, '<code>$1</code>');
    safeText = safeText.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
    safeText = safeText.replace(/^#### (.*$)/gm, '<h4>$1</h4>');
    safeText = safeText.replace(/^### (.*$)/gm, '<h3>$1</h3>');
    safeText = safeText.replace(/^## (.*$)/gm, '<h2>$1</h2>');
    safeText = safeText.replace(/^# (.*$)/gm, '<h1>$1</h1>');
    safeText = safeText.replace(/^\s*[\-\*] (.*$)/gm, '&#8226; $1<br>');
    safeText = safeText.replace(/\n/g, '<br>');

    // ★変更: Replaceボタンのラベルを「Show Diff」に変更して意図を明確化
    safeText = safeText.replace(/___CODE_BLOCK_(\d+)___/g, function (match, index) {
        const code = codeBlocks[index];
        return `<pre><div class='code-actions'>
                    <button class='action-btn' onclick='postAction(CMD_INSERT, this)'>Insert</button>
                    <button class='action-btn' onclick='postAction(CMD_REPLACE, this)'>Show Diff</button>
                    <button class='action-btn' onclick='postAction(CMD_APPLY, this)'>Apply</button>
                    <button class='action-btn' onclick='createFile(this)'>New File</button>
                </div><code>${code}</code></pre>`;
    });
    return safeText;
}

function escapeHtml(text) {
    if (!text) return text;
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
}

function createFile(btn) {
    const code = getCodeFromBtn(btn);
    if (code) {
        const defaultName = 'NewFile_' + Date.now() + '.cs';
        let fileName = defaultName;
        try {
            const inputName = prompt('Filename:', defaultName);
            if (inputName) fileName = inputName;
        } catch (e) { }

        window.chrome.webview.postMessage(CMD_NEWFILE + fileName + CMD_SEPARATOR + code);
    }
}

// ★新規追加: Diff確認後に適用するためのグローバル関数
window.applyDiffCode = function () {
    window.chrome.webview.postMessage(CMD_APPLY);
}

window.postAction = function (prefix, btn) {
    const code = getCodeFromBtn(btn);
    if (code) window.chrome.webview.postMessage(prefix + code);
}

function getCodeFromBtn(btn) {
    const pre = btn.parentElement.parentElement;
    const codeElem = pre.querySelector('code');
    return codeElem ? codeElem.innerText : null;
}

function restoreHistory(historyArray) {
    if (!historyArray || !Array.isArray(historyArray)) return;
    if (historyArray.length === 0) return;

    history.innerHTML = '';

    historyArray.forEach(msg => {
        if (msg.Role === 'user') {
            addMessage(msg.Content, 'user', false);
        } else if (msg.Role === 'assistant') {
            const formatted = formatResponse(msg.Content);
            addMessage(formatted, 'ai', true);
        }
    });

    history.scrollTop = history.scrollHeight;
}

function clearHistory() {
    if (!confirm('Are you sure you want to delete all chat history?')) {
        return;
    }

    const historyDiv = document.getElementById('chat-history');
    if (historyDiv) {
        historyDiv.innerHTML = '<div class="message ai">History cleared.</div>';
    }

    window.chrome.webview.postMessage(CMD_CLEAR);
}


let allFiles = []; // C#から受け取った全ファイルリスト
const suggestionBox = document.getElementById('suggestion-box');
const inputArea = document.getElementById('prompt');

// C#から呼ばれる関数
function updateFileList(files) {
    allFiles = files;
    console.log("Files loaded:", allFiles.length);
}

// 入力イベントの監視
inputArea.addEventListener('input', function (e) {
    const val = this.value;
    const cursorPos = this.selectionStart;

    // カーソル直前の単語を取得
    const textBeforeCursor = val.substring(0, cursorPos);
    const lastAtPos = textBeforeCursor.lastIndexOf('@');

    if (lastAtPos !== -1) {
        // @以降のテキスト（検索クエリ）を取得
        // 例: "Check @Use" -> query = "Use"
        const query = textBeforeCursor.substring(lastAtPos + 1);

        // スペースが含まれていたらキャンセル（次の単語に移ったとみなす）
        if (query.includes(' ') || query.includes('\n')) {
            hideSuggestions();
            return;
        }

        showSuggestions(query, lastAtPos);
    } else {
        hideSuggestions();
    }
});

function showSuggestions(query, atIndex) {
    // フィルタリング（大文字小文字無視）
    const matches = allFiles.filter(f => f.toLowerCase().includes(query.toLowerCase()));

    if (matches.length === 0) {
        hideSuggestions();
        return;
    }

    suggestionBox.innerHTML = '';
    suggestionBox.style.display = 'block';

    matches.slice(0, 10).forEach(file => { // 最大10件表示
        const div = document.createElement('div');
        div.className = 'suggestion-item';
        div.innerText = file;

        div.onclick = () => {
            selectSuggestion(file, atIndex, query.length);
        };

        suggestionBox.appendChild(div);
    });
}

function selectSuggestion(fileName, atIndex, queryLen) {
    const val = inputArea.value;
    const before = val.substring(0, atIndex); // @の前まで
    const after = val.substring(atIndex + 1 + queryLen); // 入力中のクエリの後ろ

    // @filename 形式で挿入
    inputArea.value = before + '@' + fileName + ' ' + after;

    hideSuggestions();
    inputArea.focus();
}

function hideSuggestions() {
    suggestionBox.style.display = 'none';
}