// AI Translator — front-end SPA
// Vanilla ES modules; no build step. Integrates Azure Speech SDK for STT+MT
// and Neural TTS, and the Azure Web PubSub client for meeting fan-out.

const LANGUAGES = [
  { code: 'en', name: 'English',  stt: 'en-US', voice: 'en-US-EmmaMultilingualNeural' },
  { code: 'ru', name: 'Русский',   stt: 'ru-RU', voice: 'ru-RU-SvetlanaNeural' },
  { code: 'es', name: 'Español',   stt: 'es-ES', voice: 'es-ES-XimenaNeural' },
  { code: 'fr', name: 'Français',  stt: 'fr-FR', voice: 'fr-FR-DeniseNeural' },
  { code: 'de', name: 'Deutsch',   stt: 'de-DE', voice: 'de-DE-KatjaNeural' },
  { code: 'it', name: 'Italiano',  stt: 'it-IT', voice: 'it-IT-ElsaNeural' },
  { code: 'pt', name: 'Português', stt: 'pt-PT', voice: 'pt-PT-RaquelNeural' },
  { code: 'zh', name: '中文',       stt: 'zh-CN', voice: 'zh-CN-XiaoxiaoNeural' },
  { code: 'ja', name: '日本語',     stt: 'ja-JP', voice: 'ja-JP-NanamiNeural' },
  { code: 'ar', name: 'العربية',   stt: 'ar-SA', voice: 'ar-SA-ZariyahNeural' },
  { code: 'uk', name: 'Українська', stt: 'uk-UA', voice: 'uk-UA-PolinaNeural' },
];
const langMap = new Map(LANGUAGES.map(l => [l.code, l]));

// -------- Test personas (dev only) --------
// Pick with ?dev=<key> in the URL. Persisted to localStorage so subsequent
// requests keep the identity. Backend accepts X-Dev-* headers only when
// running in the Development environment.
const DEV_PERSONAS = {
  sergey: { oid: '7bc3b904-679f-44a7-b0bf-fb6faf468a45', name: 'Sergey', email: 'sergeype@OopsLab.in', lang: 'en' },
  anna:   { oid: 'aaaa0000-0000-4000-8000-000000000002', name: 'Anna',   email: 'anna@example.com',   lang: 'ru' },
  john:   { oid: 'aaaa0000-0000-4000-8000-000000000003', name: 'John',   email: 'john@example.com',   lang: 'en' },
};
(() => {
  const url = new URL(location.href);
  const dev = url.searchParams.get('dev');
  if (dev !== null) {
    if (dev === '' || !DEV_PERSONAS[dev]) localStorage.removeItem('devUser');
    else localStorage.setItem('devUser', dev);
    url.searchParams.delete('dev');
    history.replaceState({}, '', url.toString());
  }
})();
const currentDevPersona = () => {
  const key = localStorage.getItem('devUser');
  return key && DEV_PERSONAS[key] ? { key, ...DEV_PERSONAS[key] } : null;
};

// -------- API client --------
const api = {
  async _req(path, opts = {}) {
    const headers = { 'Content-Type': 'application/json', ...opts.headers };
    const dev = currentDevPersona();
    if (dev) {
      headers['X-Dev-Oid'] = dev.oid;
      headers['X-Dev-Name'] = dev.name;
      headers['X-Dev-Email'] = dev.email;
      headers['X-Dev-Lang'] = dev.lang;
    }
    const res = await fetch(path, {
      headers,
      ...opts,
      body: opts.body ? JSON.stringify(opts.body) : undefined,
    });
    if (res.status === 401) throw new AuthError('Not signed in');
    if (!res.ok) {
      let msg = res.statusText;
      try { const j = await res.json(); msg = j.error || j.title || msg; } catch {}
      throw new ApiError(res.status, msg);
    }
    return res.status === 204 ? null : res.json();
  },
  me:          ()               => api._req('/api/me'),
  updateMe:    (b)              => api._req('/api/me', { method: 'PATCH', body: b }),
  users:       (q)              => api._req('/api/users' + (q ? `?query=${encodeURIComponent(q)}` : '')),
  conversations:()              => api._req('/api/conversations'),
  createConv:  (peerId)         => api._req('/api/conversations', { method: 'POST', body: { peerId } }),
  messages:    (id)             => api._req(`/api/conversations/${id}/messages`),
  sendMessage: (id, b)          => api._req(`/api/conversations/${id}/messages`, { method: 'POST', body: b }),
  meetings:    ()               => api._req('/api/meetings/mine'),
  createMeeting:(b)             => api._req('/api/meetings', { method: 'POST', body: b }),
  meeting:     (id)             => api._req(`/api/meetings/${id}`),
  endMeeting:  (id)             => api._req(`/api/meetings/${id}/end`, { method: 'POST' }),
  joinMeeting: (token, b)       => api._req(`/api/meetings/join?token=${encodeURIComponent(token)}`, { method: 'POST', body: b }),
  leaveMeeting:(id)             => api._req(`/api/meetings/${id}/leave`, { method: 'POST' }),
  turns:       (id, lang)       => api._req(`/api/meetings/${id}/turns` + (lang ? `?lang=${lang}` : '')),
  postTurn:    (id, b)          => api._req(`/api/meetings/${id}/turns`, { method: 'POST', body: b }),
  summary:     (id, lang)       => api._req(`/api/meetings/${id}/summary` + (lang ? `?lang=${lang}` : '')),
  makeSummary: (id, b)          => api._req(`/api/meetings/${id}/summary`, { method: 'POST', body: b }),
  inboxToken:  ()               => api._req('/api/wps/inbox'),
  speechToken: ()               => api._req('/api/speech/token'),
};

class AuthError extends Error {}
class ApiError extends Error { constructor(status, msg) { super(msg); this.status = status; } }

// -------- Toasts --------
const toast = (msg, kind = 'info') => {
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  el.textContent = msg;
  document.getElementById('toasts').appendChild(el);
  setTimeout(() => el.remove(), 4000);
};

// -------- State --------
const state = {
  me: null,
  meeting: null,          // current meeting details
  meetingWps: null,       // active WPS client
  meetingSpeech: null,    // Speech Translation state
  currentPeerId: null,
  currentConversationId: null,
};

// -------- Router --------
const routes = [];
const addRoute = (regex, handler) => routes.push({ regex, handler });

addRoute(/^\/join\/(.+)$/, (m) => showJoin(decodeURIComponent(m[1])));
addRoute(/^#\/chat(?:\/([0-9a-f-]+))?$/i, (m) => showChats(m[1] || null));
addRoute(/^#\/meetings$/, () => showMeetings());
addRoute(/^#\/meetings\/([0-9a-f-]+)$/i, (m) => showMeetingRoom(m[1]));
addRoute(/^#?\/?$/, () => showMeetings());

const currentPath = () => window.location.hash || window.location.pathname;
async function route() {
  const path = currentPath();
  for (const r of routes) {
    const m = path.match(r.regex);
    if (m) { try { await r.handler(m); } catch (e) { handleError(e); } return; }
  }
  showMeetings();
}
window.addEventListener('hashchange', route);
window.addEventListener('popstate', route);

// -------- Screens --------
const screens = ['loading', 'signin', 'chats', 'meetings', 'meeting-room', 'join'];
function show(name) {
  screens.forEach(s => document.getElementById('screen-' + s).hidden = s !== name);
  document.querySelectorAll('#tabs a').forEach(a => {
    a.classList.toggle('active', (name === 'chats' && a.dataset.tab === 'chat')
                              || (name.startsWith('meeting') && a.dataset.tab === 'meetings'));
  });
}

// -------- Chats --------
async function showChats(peerId = null) {
  show('chats');
  const list = document.getElementById('peer-list');
  list.innerHTML = '<li class="muted">Loading peers…</li>';
  const users = await api.users();
  if (!users.length) { list.innerHTML = '<li class="muted">No other users yet. Ask someone to sign in.</li>'; return; }
  list.innerHTML = '';
  users.forEach(u => {
    const li = document.createElement('li');
    li.dataset.peerId = u.id;
    li.innerHTML = `<span>${escapeHtml(u.displayName)}</span><span class="peer-lang">${escapeHtml(langName(u.preferredLanguage))}</span>`;
    if (u.id === peerId) li.classList.add('active');
    li.addEventListener('click', () => location.hash = '#/chat/' + u.id);
    list.appendChild(li);
  });
  if (peerId) await openConversation(peerId, users.find(u => u.id === peerId));
}

async function openConversation(peerId, peer) {
  state.currentPeerId = peerId;
  const conv = await api.createConv(peerId);
  state.currentConversationId = conv.id;
  document.getElementById('chat-peer-name').textContent = peer?.displayName ?? conv.peer.displayName;
  document.getElementById('chat-peer-lang').textContent = langName(peer?.preferredLanguage ?? conv.peer.preferredLanguage);
  document.getElementById('chat-form').hidden = false;
  await renderMessages(conv.id);
}

async function renderMessages(convId) {
  const thread = document.getElementById('chat-thread');
  const msgs = await api.messages(convId);
  thread.innerHTML = '';
  if (!msgs.length) { thread.innerHTML = '<div class="muted center">Send the first message.</div>'; return; }
  msgs.forEach(m => thread.appendChild(renderMsg(m)));
  thread.scrollTop = thread.scrollHeight;
}

function renderMsg(m) {
  const d = document.createElement('div');
  d.className = 'msg' + (m.fromUserId === state.me.id ? ' mine' : '');
  d.innerHTML = `
    <div class="meta">${escapeHtml(langName(m.sourceLang))} → ${escapeHtml(langName(m.targetLang))} · ${formatTime(m.createdAt)}</div>
    <div class="original">${escapeHtml(m.original)}</div>
    <div class="translated">${escapeHtml(m.translated)}</div>`;
  return d;
}

document.getElementById('chat-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const text = document.getElementById('chat-input').value.trim();
  if (!text || !state.currentConversationId) return;
  document.getElementById('chat-input').value = '';
  try {
    const msg = await api.sendMessage(state.currentConversationId, { text });
    const thread = document.getElementById('chat-thread');
    if (thread.querySelector('.muted')) thread.innerHTML = '';
    thread.appendChild(renderMsg(msg));
    thread.scrollTop = thread.scrollHeight;
  } catch (e) { handleError(e); }
});

document.getElementById('chat-mic').addEventListener('click', async () => {
  const btn = document.getElementById('chat-mic');
  const input = document.getElementById('chat-input');
  try {
    const speech = await ensureSpeechSdk();
    const cfg = await getSpeechConfig(speech, false);
    cfg.speechRecognitionLanguage = langMap.get(state.me.preferredLanguage)?.stt ?? 'en-US';
    const rec = new speech.SpeechRecognizer(cfg, speech.AudioConfig.fromDefaultMicrophoneInput());
    btn.classList.add('active');
    rec.recognizeOnceAsync(r => {
      btn.classList.remove('active');
      if (r?.text) input.value = (input.value ? input.value + ' ' : '') + r.text;
      rec.close();
    });
  } catch (e) { handleError(e); }
});

// -------- Meetings --------
async function showMeetings() {
  show('meetings');
  const list = document.getElementById('meeting-list');
  list.innerHTML = '<li class="muted">Loading…</li>';
  const meetings = await api.meetings();
  list.innerHTML = '';
  if (!meetings.length) { list.innerHTML = '<li class="muted">No meetings yet — start one.</li>'; return; }
  meetings.forEach(m => {
    const li = document.createElement('li');
    li.innerHTML = `
      <div>
        <strong>${escapeHtml(m.title ?? 'Untitled meeting')}</strong>
        <div class="meta">${m.status} · ${m.role} · ${formatDate(m.createdAt)}</div>
      </div>
      <div class="row-gap">
        <button class="btn secondary" data-id="${m.id}" data-act="open">Open</button>
      </div>`;
    list.appendChild(li);
  });
  list.querySelectorAll('[data-act="open"]').forEach(btn =>
    btn.addEventListener('click', () => location.hash = '#/meetings/' + btn.dataset.id));
}

document.getElementById('meeting-new').addEventListener('click', async () => {
  const title = prompt('Meeting title (optional):') ?? undefined;
  try {
    const m = await api.createMeeting({ title: title || null });
    openQrDialog(m.joinUrl, m.qrPngDataUrl);
    toast('Meeting created', 'success');
    // Preload participants panel by navigating to room too
    setTimeout(() => (location.hash = '#/meetings/' + m.meetingId), 300);
  } catch (e) { handleError(e); }
});

document.getElementById('join-open').addEventListener('click', () => {
  document.getElementById('join-input').value = '';
  document.getElementById('join-dialog').showModal();
});
document.getElementById('join-go').addEventListener('click', () => {
  const raw = document.getElementById('join-input').value.trim();
  document.getElementById('join-dialog').close();
  if (!raw) return;
  const token = raw.includes('/join/') ? raw.split('/join/')[1] : raw;
  location.pathname = '/join/' + encodeURIComponent(token);
});

function openQrDialog(url, dataUrl) {
  document.getElementById('qr-img').src = dataUrl;
  document.getElementById('qr-url').value = url;
  document.getElementById('qr-dialog').showModal();
}
document.getElementById('qr-copy').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(document.getElementById('qr-url').value); toast('Link copied', 'success'); }
  catch { toast('Copy failed — select and copy manually.', 'error'); }
});

// -------- Meeting room --------
async function showMeetingRoom(id) {
  show('meeting-room');
  const room = await api.meeting(id);
  state.meeting = room;
  document.getElementById('room-title').textContent = room.title ?? 'Meeting';
  document.getElementById('room-status').textContent = room.status;
  document.getElementById('room-end').hidden = !(room.initiator.id === state.me.id && room.status === 'active');
  renderParticipants();
  document.getElementById('subtitles').innerHTML = '<div class="muted center">Waiting for the first turn…</div>';
  document.getElementById('summary-box').hidden = true;

  // Load existing turns in caller's language
  const turns = await api.turns(id);
  const container = document.getElementById('subtitles');
  if (turns.length) { container.innerHTML = ''; turns.forEach(renderSubtitle); }

  // Attempt to auto-connect to WPS as an existing participant. Failures are
  // non-fatal — we can still see history and (once mic is enabled) send turns.
  const me = room.participants.find(p => p.id === state.me.id);
  if (me) {
    try { await connectWps(id, me.language); }
    catch (e) { console.warn('WPS connect skipped:', e); toast('Live subtitles disabled — reopen via the join link to receive them.', 'error'); }
  } else {
    document.getElementById('mic-hint').textContent = 'You need to open this meeting from a join link to send speech.';
  }

  // Mic enablement is independent of WPS; run it even if WPS bootstrap failed.
  await enableMicIfPossible();
}

function renderParticipants() {
  const ul = document.getElementById('participant-list');
  ul.innerHTML = '';
  state.meeting.participants.forEach(p => {
    const li = document.createElement('li');
    li.dataset.userId = p.id;
    li.innerHTML = `<span>${escapeHtml(p.displayName)}</span><span class="pill">${escapeHtml(langName(p.language))}</span>`;
    ul.appendChild(li);
  });
}

function renderSubtitle(t) {
  const container = document.getElementById('subtitles');
  // Guard against duplicates: WPS may echo a turn we already rendered
  // optimistically after our own speak.
  if (t.id && container.querySelector(`[data-turn-id="${t.id}"]`)) return;
  const el = document.createElement('div');
  if (t.id) el.dataset.turnId = t.id;
  el.className = 'subtitle' + (t.speakerId === state.me.id ? ' mine' : '');
  const speaker = state.meeting?.participants.find(p => p.id === t.speakerId);
  el.innerHTML = `
    <div class="who">${escapeHtml(speaker?.displayName ?? 'Someone')} — ${escapeHtml(langName(t.sourceLang))} → ${escapeHtml(langName(t.callerLang))} · ${formatTime(t.createdAt)}</div>
    <div class="translated">${escapeHtml(t.translatedForCaller ?? t.original)}</div>
    <div class="original">${escapeHtml(t.original)}</div>`;
  if (container.querySelector('.muted')) container.innerHTML = '';
  container.appendChild(el);
  container.scrollTop = container.scrollHeight;
}

async function connectWps(meetingId, language) {
  const token = currentJoinToken(meetingId);
  if (!token) {
    document.getElementById('mic-hint').textContent = 'Reconnect via the join link to receive live subtitles.';
    return;
  }
  await ensureWpsSdk();
  const r = await api.joinMeeting(token, { language });
  const url = r?.wps?.url;
  if (!url) return;
  await bootstrapMeetingWps(url, meetingId, language, r.meeting?.participants?.find(p => p.id === state.me.id)?.id ?? state.me.id);
}

async function bootstrapMeetingWps(url, meetingId, language, myUserId) {
  const client = new WebPubSubClient.WebPubSubClient({
    getClientAccessUrl: async () => url,
    protocol: WebPubSubClient.WebPubSubJsonProtocol(),
  });
  client.on('group-message', (e) => handleWps(e.message.data));
  client.on('server-message', (e) => handleWps(e.message.data));
  await client.start();
  // Auto-join all three meeting groups so we receive presence updates,
  // language-scoped turn broadcasts, and private events targeted at us.
  await Promise.all([
    client.joinGroup(`m:${meetingId}:presence`),
    client.joinGroup(`m:${meetingId}:lang:${language}`),
    client.joinGroup(`m:${meetingId}:u:${myUserId}`),
  ]);
  state.meetingWps = client;
}

function currentJoinToken() { return null; /* tokens live on /join/{token}; not persisted client-side */ }

function handleWps(payload) {
  let msg;
  try { msg = typeof payload === 'string' ? JSON.parse(payload) : payload; }
  catch { return; }
  switch (msg.type) {
    case 'turn.translated':
      renderSubtitle({
        id: msg.turnId, speakerId: msg.speaker.id,
        sourceLang: msg.sourceLang, callerLang: msg.targetLang,
        original: msg.original, translatedForCaller: msg.translated,
        createdAt: msg.ts,
      });
      // Speak the incoming turn (only for others, not our own echo)
      if (msg.speaker.id !== state.me.id) speak(msg.translated, msg.targetLang).catch(() => {});
      break;
    case 'presence.joined':
      if (!state.meeting.participants.some(p => p.id === msg.participant.id))
        state.meeting.participants.push({ ...msg.participant, role: 'listener', joinedAt: msg.ts });
      renderParticipants();
      toast(`${msg.participant.displayName} joined (${langName(msg.participant.language)})`);
      break;
    case 'presence.left':
      state.meeting.participants = state.meeting.participants.filter(p => p.id !== msg.participantId);
      renderParticipants();
      break;
    case 'meeting.ended':
      document.getElementById('room-status').textContent = 'ended';
      toast('Meeting ended', 'success');
      break;
    case 'summary.ready':
      toast(`Summary available (${langName(msg.language)})`);
      break;
  }
}

document.getElementById('room-end').addEventListener('click', async () => {
  if (!confirm('End the meeting for everyone?')) return;
  try { await api.endMeeting(state.meeting.id); toast('Meeting ended', 'success'); }
  catch (e) { handleError(e); }
});
document.getElementById('room-leave').addEventListener('click', async () => {
  try { await api.leaveMeeting(state.meeting.id); }
  catch { /* best-effort */ }
  location.hash = '#/meetings';
});
document.getElementById('room-summary').addEventListener('click', async () => {
  try {
    const s = await api.makeSummary(state.meeting.id, { language: state.me.preferredLanguage });
    document.getElementById('summary-lang').textContent = langName(s.language);
    document.getElementById('summary-body').textContent = s.summary;
    document.getElementById('summary-box').hidden = false;
  } catch (e) { handleError(e); }
});

// -------- Speech: STT + MT via TranslationRecognizer, TTS via SpeechSynthesizer --------
let speechSdkLoad;
function ensureSpeechSdk() {
  if (window.SpeechSDK) return Promise.resolve(window.SpeechSDK);
  if (speechSdkLoad) return speechSdkLoad;
  speechSdkLoad = new Promise((resolve, reject) => {
    const start = Date.now();
    const check = () => {
      if (window.SpeechSDK) return resolve(window.SpeechSDK);
      if (Date.now() - start > 60000) return reject(new Error('Speech SDK failed to load'));
      setTimeout(check, 200);
    };
    check();
  });
  return speechSdkLoad;
}
let wpsSdkLoad;
function ensureWpsSdk() {
  if (window.WebPubSubClient?.WebPubSubClient) return Promise.resolve(window.WebPubSubClient);
  if (!wpsSdkLoad) {
    // @azure/web-pubsub-client ships as ESM only. esm.sh transforms it into a
    // browser-friendly ES module we can dynamic-import.
    wpsSdkLoad = import('https://esm.sh/@azure/web-pubsub-client@1.1.0')
      .then((mod) => {
        // mod is a module namespace object with { WebPubSubClient, ... }.
        window.WebPubSubClient = mod;
        return mod;
      });
  }
  return wpsSdkLoad;
}

let cachedSpeechToken = null;
async function getSpeechToken() {
  if (cachedSpeechToken && new Date(cachedSpeechToken.expiresAt).getTime() - Date.now() > 60000)
    return cachedSpeechToken;
  cachedSpeechToken = await api.speechToken();
  return cachedSpeechToken;
}
async function getSpeechConfig(sdk, forTranslation) {
  const { token, region } = await getSpeechToken();
  return forTranslation
    ? sdk.SpeechTranslationConfig.fromAuthorizationToken(token, region)
    : sdk.SpeechConfig.fromAuthorizationToken(token, region);
}

async function enableMicIfPossible() {
  const btn = document.getElementById('mic-toggle');
  const hint = document.getElementById('mic-hint');
  btn.disabled = true;
  if (!navigator.mediaDevices?.getUserMedia) { hint.textContent = 'Microphone not supported by this browser.'; return; }
  try {
    await ensureSpeechSdk();
    btn.disabled = false;
    hint.textContent = 'Press to speak. Everyone hears you in their language.';
  } catch { hint.textContent = 'Speech SDK not available yet.'; }
}

let activeRecognizer = null;
document.getElementById('mic-toggle').addEventListener('click', async () => {
  const btn = document.getElementById('mic-toggle');
  const hint = document.getElementById('mic-hint');
  if (activeRecognizer) { await stopSpeaking(); return; }

  // Pre-warm mic permission with getUserMedia so the browser prompt fires on
  // a real user gesture. The Speech SDK opens the mic internally via
  // AudioConfig.fromDefaultMicrophoneInput, but permission denial there is
  // opaque — this way we get a clear failure before starting the recognizer.
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    stream.getTracks().forEach(t => t.stop()); // release; Speech SDK will re-open
  } catch (err) {
    toast('Microphone permission denied: ' + (err.message || err.name), 'error');
    hint.textContent = 'Microphone permission denied.';
    return;
  }

  try {
    const sdk = await ensureSpeechSdk();
    const cfg = await getSpeechConfig(sdk, true);
    const mine = state.meeting.participants.find(p => p.id === state.me.id)?.language ?? state.me.preferredLanguage;
    cfg.speechRecognitionLanguage = langMap.get(mine)?.stt ?? 'en-US';
    const targets = [...new Set(state.meeting.participants.map(p => p.language))];
    targets.forEach(l => cfg.addTargetLanguage(l));
    const audio = sdk.AudioConfig.fromDefaultMicrophoneInput();
    const rec = new sdk.TranslationRecognizer(cfg, audio);
    // Debug handle so we can inspect the last event tree from DevTools.
    window.__recognizer = rec;

    rec.recognizing = (_s, e) => {
      // Interim (partial) result — great UX signal that the mic is picking
      // up audio at all.
      if (e.result.text) hint.textContent = '🎧 …' + e.result.text.slice(-60);
    };
    rec.recognized = async (_s, e) => {
      window.__lastRecognized = e.result;
      const reasonName = Object.entries(sdk.ResultReason).find(([, v]) => v === e.result.reason)?.[0] ?? String(e.result.reason);
      console.log('[Speech] recognized', reasonName, 'text=', e.result.text, 'translations=', e.result.translations && Array.from(e.result.translations.languages));
      if (e.result.reason === sdk.ResultReason.NoMatch) {
        hint.textContent = 'No speech detected — try again.';
        return;
      }
      const original = e.result.text?.trim();
      if (!original) { hint.textContent = 'Empty result (' + reasonName + ').'; return; }
      // Collect translations. If the service returned only RecognizedSpeech
      // (no translation), fall back to sending the original for every target.
      const translations = {};
      if (e.result.translations) {
        for (const lang of e.result.translations.languages) {
          const t = e.result.translations.get(lang);
          if (t) translations[lang] = t;
        }
      }
      for (const lang of targets) {
        if (!translations[lang]) translations[lang] = original;
      }
      if (!translations[mine]) translations[mine] = original;
      hint.textContent = '🎙️ Sent: ' + original.slice(0, 60);
      try {
        const { turnId, createdAt } = await api.postTurn(state.meeting.id, {
          original, sourceLang: mine, translations, addresseeId: null,
        });
        // Render locally right away — the WPS fan-out will echo the same
        // turn back but that's fine (renderSubtitle guards against dupes
        // via turnId below).
        renderSubtitle({
          id: turnId,
          speakerId: state.me.id,
          sourceLang: mine,
          callerLang: mine,
          original,
          translatedForCaller: translations[mine] ?? original,
          createdAt,
        });
      } catch (err) { handleError(err); }
    };
    rec.canceled = (_s, e) => {
      const detail = e.errorDetails || (e.reason === sdk.CancellationReason.EndOfStream ? 'end of stream' : String(e.reason));
      toast('Speech recognition canceled: ' + detail, 'error');
      hint.textContent = 'Recognition stopped: ' + detail;
      stopSpeaking();
    };
    rec.sessionStopped = () => { hint.textContent = 'Session stopped.'; stopSpeaking(); };

    rec.startContinuousRecognitionAsync(
      () => {
        activeRecognizer = rec;
        btn.textContent = '⏸ Stop';
        btn.classList.add('recording');
        hint.textContent = '🎙️ Listening… speak now.';
      },
      (err) => {
        toast('Failed to start speech: ' + err, 'error');
        hint.textContent = 'Failed to start: ' + err;
      }
    );
  } catch (e) { handleError(e); hint.textContent = 'Error: ' + (e.message ?? e); }
});
async function stopSpeaking() {
  if (!activeRecognizer) return;
  const rec = activeRecognizer;
  activeRecognizer = null;
  const btn = document.getElementById('mic-toggle');
  btn.textContent = '🎙️ Start speaking';
  btn.classList.remove('recording');
  try { rec.stopContinuousRecognitionAsync(() => rec.close(), () => rec.close()); } catch {}
}

async function speak(text, lang) {
  const voice = langMap.get(lang)?.voice;
  if (!voice) return;
  const sdk = await ensureSpeechSdk();
  const cfg = await getSpeechConfig(sdk, false);
  cfg.speechSynthesisVoiceName = voice;
  const synth = new sdk.SpeechSynthesizer(cfg);
  synth.speakTextAsync(text, () => synth.close(), () => synth.close());
}

// -------- Join screen --------
async function showJoin(token) {
  show('join');
  const select = document.getElementById('join-language');
  select.innerHTML = LANGUAGES.map(l => `<option value="${l.code}">${l.name}</option>`).join('');
  select.value = state.me?.preferredLanguage ?? 'en';
  const btn = document.getElementById('join-submit');
  btn.onclick = async () => {
    btn.disabled = true;
    try {
      const r = await api.joinMeeting(token, { language: select.value });
      state.meeting = r.meeting;
      // Update the user's preferred language server-side too.
      if (state.me.preferredLanguage !== select.value) {
        await api.updateMe({ preferredLanguage: select.value });
        state.me.preferredLanguage = select.value;
        updateMeUi();
      }
      // Navigate via SPA to the room, then bootstrap WPS from the URL we got.
      history.replaceState({}, '', '/');
      await showMeetingRoom(r.meetingId);
      await ensureWpsSdk();
      await bootstrapMeetingWps(r.wps.url, r.meetingId, select.value, state.me.id);
      await enableMicIfPossible();
      toast('You joined the meeting', 'success');
    } catch (e) { handleError(e); }
    finally { btn.disabled = false; }
  };
}

document.addEventListener('click', (e) => {
  const badge = e.target.closest('#dev-badge');
  if (badge) {
    e.preventDefault();
    const keys = Object.keys(DEV_PERSONAS);
    const choice = prompt(`Switch to test persona (${keys.join(', ')}). Blank = clear override.`, currentDevPersona()?.key ?? '');
    if (choice === null) return;
    if (!choice) localStorage.removeItem('devUser');
    else if (DEV_PERSONAS[choice]) localStorage.setItem('devUser', choice);
    else { toast('Unknown persona: ' + choice, 'error'); return; }
    location.reload();
  }
});

// -------- Personal inbox (real-time chat delivery via Web PubSub) --------
let inboxClient = null;
async function connectInbox() {
  try {
    await ensureWpsSdk();
    const { url } = await api.inboxToken();
    // Use the non-reliable JSON protocol — the default Reliable protocol
    // requires message-acking that our server-side SendToGroup doesn't
    // participate in, so subscriptions silently drop messages.
    inboxClient = new WebPubSubClient.WebPubSubClient({
      getClientAccessUrl: async () => url,
      protocol: WebPubSubClient.WebPubSubJsonProtocol(),
    });
    inboxClient.on('group-message', (e) => handleInboxMessage(e.message.data));
    inboxClient.on('server-message', (e) => handleInboxMessage(e.message.data));
    await inboxClient.start();
    // The `groups` param on the client-access URL only permits joining; the
    // client must explicitly subscribe after connect for group-message
    // delivery to fire.
    await inboxClient.joinGroup(`inbox:${state.me.id}`);
  } catch (e) {
    console.warn('Inbox WPS unavailable:', e);
  }
}
function handleInboxMessage(payload) {
  let msg;
  try { msg = typeof payload === 'string' ? JSON.parse(payload) : payload; }
  catch { return; }
  if (msg.type !== 'message.new') return;
  // If the thread for this conversation is open, append the new message live.
  if (state.currentConversationId === msg.conversationId) {
    const thread = document.getElementById('chat-thread');
    if (thread.querySelector('.muted')) thread.innerHTML = '';
    thread.appendChild(renderMsg(msg.message));
    thread.scrollTop = thread.scrollHeight;
  } else {
    toast(`New message from ${msg.fromUser.displayName}`, 'success');
  }
}

// -------- Boot --------
async function boot() {
  populateLangSelect();
  try {
    state.me = await api.me();
  } catch (e) {
    if (e instanceof AuthError) { show('signin'); return; }
    show('signin'); return;
  }
  updateMeUi();
  document.getElementById('tabs').hidden = false;
  document.getElementById('me').hidden = false;
  connectInbox(); // fire-and-forget; live push works when it connects.
  await route();
}
boot();

function populateLangSelect() {
  const sel = document.getElementById('me-language');
  sel.innerHTML = LANGUAGES.map(l => `<option value="${l.code}">${l.name}</option>`).join('');
  sel.addEventListener('change', async () => {
    try {
      const updated = await api.updateMe({ preferredLanguage: sel.value });
      state.me = updated;
      toast('Language updated', 'success');
    } catch (e) { handleError(e); }
  });
}
function updateMeUi() {
  document.getElementById('me-name').textContent = state.me.displayName;
  document.getElementById('me-language').value = state.me.preferredLanguage;
  const dev = currentDevPersona();
  const badge = document.getElementById('dev-badge');
  if (dev) {
    badge.hidden = false;
    badge.textContent = `dev: ${dev.key}`;
    badge.title = `Impersonating ${dev.name} (${dev.email}). Click to switch.`;
  } else {
    badge.hidden = true;
  }
}

function handleError(e) {
  console.error(e);
  const msg = e instanceof ApiError ? `${e.status}: ${e.message}` : (e.message ?? String(e));
  toast(msg, 'error');
}

// -------- Utils --------
function escapeHtml(s) { return String(s ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch])); }
function langName(code) { return langMap.get(code)?.name ?? code; }
function formatTime(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }
function formatDate(iso) { const d = new Date(iso); return d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ' ' + formatTime(iso); }
