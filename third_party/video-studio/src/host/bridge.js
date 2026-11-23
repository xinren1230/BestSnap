/**
 * BestSnap host bridge. Talks to the WPF WebView2 (or Electron) host over
 * postMessage. No network, no cloud, no sign-in.
 */

const pending = new Map();
let nextId = 1;
let sourceUrl = null;
let sourceName = null;
const openWaiters = [];

function transport() {
  if (typeof chrome !== 'undefined' && chrome.webview?.postMessage) return 'webview2';
  if (typeof window !== 'undefined' && window.bestsnapHost?.postMessage) return 'electron';
  return null;
}

function send(msg) {
  const kind = transport();
  if (kind === 'webview2') chrome.webview.postMessage(JSON.stringify(msg));
  else if (kind === 'electron') window.bestsnapHost.postMessage(msg);
}

function onMessage(data) {
  if (!data || typeof data !== 'object') return;
  if (data.type === 'response' && pending.has(data.id)) {
    const { resolve, reject } = pending.get(data.id);
    pending.delete(data.id);
    if (data.error) reject(new Error(data.error));
    else resolve(data.result);
    return;
  }
  if (data.type === 'openMedia') {
    sourceUrl = data.url || '';
    sourceName = data.name || 'recording.mp4';
    const waiters = openWaiters.splice(0);
    for (const w of waiters) w({ url: sourceUrl, name: sourceName });
  }
}

if (typeof chrome !== 'undefined' && chrome.webview?.addEventListener) {
  chrome.webview.addEventListener('message', (e) => {
    let data = e.data;
    if (typeof data === 'string') {
      try { data = JSON.parse(data); } catch { return; }
    }
    onMessage(data);
  });
}
if (typeof window !== 'undefined') {
  window.addEventListener('message', (e) => {
    if (e.source !== window) return;
    onMessage(e.data);
  });
  window.addEventListener('bestsnap-host', (e) => onMessage(e.detail));
}

export function isHosted() {
  return Boolean(transport()) || /[?&]host=1(?:&|$)/.test(location.search);
}

export function callHost(method, params = {}) {
  if (!transport()) return Promise.reject(new Error('BestSnap host is not available.'));
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    send({ type: 'request', id, method, params });
    setTimeout(() => {
      if (!pending.has(id)) return;
      pending.delete(id);
      reject(new Error(`${method} timed out`));
    }, 120_000);
  });
}

export function waitForOpenMedia(timeoutMs = 8000) {
  if (sourceUrl) return Promise.resolve({ url: sourceUrl, name: sourceName });
  return new Promise((resolve) => {
    const t = setTimeout(() => resolve(null), timeoutMs);
    openWaiters.push((info) => {
      clearTimeout(t);
      resolve(info);
    });
  });
}

export async function fetchOpenFile() {
  const info = await waitForOpenMedia();
  if (!info) return null;
  const res = await fetch(info.url);
  if (!res.ok) throw new Error(`Could not read ${info.name}`);
  const blob = await res.blob();
  const type = blob.type || guessMime(info.name);
  return new File([blob], info.name, { type });
}

function guessMime(name) {
  const n = String(name || '').toLowerCase();
  if (n.endsWith('.mp4')) return 'video/mp4';
  if (n.endsWith('.webm')) return 'video/webm';
  if (n.endsWith('.mov')) return 'video/quicktime';
  if (n.endsWith('.gif')) return 'image/gif';
  if (n.endsWith('.mp3')) return 'audio/mpeg';
  if (n.endsWith('.wav')) return 'audio/wav';
  if (n.endsWith('.png')) return 'image/png';
  if (n.endsWith('.jpg') || n.endsWith('.jpeg')) return 'image/jpeg';
  return '';
}

export async function saveExport(fileName, blob, mime) {
  const kind = transport();
  if (!kind) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
    return { path: fileName, downloaded: true };
  }
  const started = await callHost('saveAs', { fileName, mime: mime || blob.type || 'video/mp4', size: blob.size });
  if (!started?.accepted) return { canceled: true };
  const buf = new Uint8Array(await blob.arrayBuffer());
  const chunk = 256 * 1024;
  for (let i = 0; i < buf.length; i += chunk) {
    const slice = buf.subarray(i, Math.min(i + chunk, buf.length));
    let binary = '';
    for (let j = 0; j < slice.length; j++) binary += String.fromCharCode(slice[j]);
    await callHost('writeChunk', {
      token: started.token,
      index: Math.floor(i / chunk),
      last: i + chunk >= buf.length,
      data: btoa(binary),
    });
  }
  return { path: started.path, downloaded: false };
}

export async function transcribeWav(file, { language = 'auto', signal } = {}) {
  const buf = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  const step = 0x8000;
  for (let i = 0; i < buf.length; i += step) {
    binary += String.fromCharCode(...buf.subarray(i, i + step));
  }
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError');
  return callHost('transcribe', { wavBase64: btoa(binary), language, fileName: file.name || 'captions.wav' });
}

export function sttAvailable() {
  return isHosted();
}
