const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '..', 'chatui', 'wwwroot', 'app.js'), 'utf8');
const start = source.slice(source.indexOf('async function startTalkS2S('), source.indexOf('function renderS2SBubble('));
const stop = source.slice(source.indexOf('async function stopTalk('), source.indexOf("document.addEventListener('visibilitychange'"));

function setup() {
  const elements = new Map();
  const gains = [];
  const element = id => {
    if (!elements.has(id)) elements.set(id, {
      textContent: '', classList: { add() {}, remove() {} },
      querySelector() { return element('mic-label'); },
    });
    return elements.get(id);
  };
  class AudioContext {
    audioWorklet = { addModule: async () => {} };
    destination = {};
    createMediaStreamSource() { return { connect() {} }; }
    createGain() {
      const gain = { gain: { value: 1 }, connect(node) { this.connectedTo = node; return node; }, disconnect() {} };
      gains.push(gain);
      return gain;
    }
    async close() {}
  }
  class Socket {
    static OPEN = 1;
    static CONNECTING = 0;
    readyState = Socket.CONNECTING;
    close() { this.readyState = 3; }
    send() {}
  }
  const track = { stopped: false, stop() { this.stopped = true; } };
  const toasts = [];
  const context = vm.createContext({
    state: { me: { preferredLanguage: 'en' } },
    loadMeetingPrefs: () => ({}), currentDevPersona: () => null,
    navigator: { mediaDevices: { getUserMedia: async () => ({ getTracks: () => [track] }) } },
    window: { AudioContext }, AudioWorkletNode: class { port = { postMessage() {} }; connect(node) { return node; } disconnect() {} },
    WebSocket: Socket, URLSearchParams, location: { protocol: 'https:', host: 'test.invalid' },
    document: { getElementById: element }, console: { warn() {} },
    talkSessionId: 1, talkStarting: true, talkActive: false,
    talkWs: null, talkRecognizer: null, talkAudioCtx: null, talkAudioStream: null,
    talkWorkletNode: null, talkSilentSink: null, s2sQueues: new Map(), talkSpeakerColors: new Map(),
    stopPitchTracker() {}, releaseTalkWakeLock: async () => {},
    requestTalkWakeLock: async () => {}, resumeTalkAudioContexts: async () => {},
    langName: value => value, toast: (...args) => toasts.push(args),
    clearTimeout,
  });
  vm.runInContext(start + '\n' + stop, context);
  return { context, element, track, toasts, Socket, gains };
}

test('capture worklet stays connected through a muted output', async () => {
  const { context, element, gains } = setup();
  await context.startTalkS2S(element('talk-mic'), element('talk-hint'), element('talk-status'), 1);
  assert.equal(gains.length, 1);
  assert.equal(gains[0].gain.value, 0);
  assert.ok(gains[0].connectedTo);
  await context.stopTalk();
});

for (const opened of [false, true]) {
  test(`upstream error survives cleanup (browser socket opened=${opened})`, async () => {
    const { context, element, track, toasts, Socket } = setup();
    await context.startTalkS2S(element('talk-mic'), element('talk-hint'), element('talk-status'), 1);
    const socket = context.talkWs;
    if (opened) {
      socket.readyState = Socket.OPEN;
      socket.onopen();
    }
    await socket.onmessage({ data: JSON.stringify({ type: 'error', message: 'Missing translation deployment.' }) });
    socket.onclose({ code: 1000, reason: 'done' });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(element('talk-hint').textContent, 'Missing translation deployment.');
    assert.equal(element('talk-status').textContent, 'Connection failed');
    assert.equal(track.stopped, true);
    assert.equal(context.talkStarting, false);
    assert.equal(context.talkActive, false);
    assert.equal(toasts.length, 1);
  });
}

test('handshake failure without a server message still releases the microphone', async () => {
  const { context, element, track } = setup();
  await context.startTalkS2S(element('talk-mic'), element('talk-hint'), element('talk-status'), 1);
  context.talkWs.onclose({ code: 1006, reason: '' });
  await new Promise(resolve => setImmediate(resolve));
  assert.match(element('talk-hint').textContent, /Translation connection closed/);
  assert.equal(track.stopped, true);
});

test('user stop remains idle and detaches the unexpected-close handler', async () => {
  const { context, element, track, toasts } = setup();
  await context.startTalkS2S(element('talk-mic'), element('talk-hint'), element('talk-status'), 1);
  const socket = context.talkWs;
  await context.stopTalk();
  assert.equal(element('talk-status').textContent, 'Idle');
  assert.equal(socket.onclose, null);
  assert.equal(track.stopped, true);
  assert.equal(toasts.length, 0);
});
