// AI Translator — front-end SPA
// Vanilla ES modules; no build step. Integrates Azure Speech SDK for STT+MT
// and Neural TTS, and the Azure Web PubSub client for meeting fan-out.

import { GuidedTraining } from './training-guided.js?v=1';

const LANGUAGES = [
  { code: 'en', name: 'English',           stt: 'en-US', voice: 'en-US-EmmaMultilingualNeural', voices: { female: 'en-US-EmmaMultilingualNeural',  male: 'en-US-AndrewMultilingualNeural' } },
  { code: 'ru', name: 'Russian',           stt: 'ru-RU', voice: 'ru-RU-SvetlanaNeural',       voices: { female: 'ru-RU-SvetlanaNeural',    male: 'ru-RU-DmitryNeural' } },
  { code: 'uk', name: 'Ukrainian',         stt: 'uk-UA', voice: 'uk-UA-PolinaNeural',         voices: { female: 'uk-UA-PolinaNeural',      male: 'uk-UA-OstapNeural' } },
  { code: 'es', name: 'Spanish',           stt: 'es-ES', voice: 'es-ES-XimenaNeural',         voices: { female: 'es-ES-ElviraNeural',      male: 'es-ES-AlvaroNeural' } },
  { code: 'pt', name: 'Portuguese',        stt: 'pt-PT', voice: 'pt-PT-RaquelNeural',         voices: { female: 'pt-PT-RaquelNeural',      male: 'pt-PT-DuarteNeural' } },
  { code: 'fr', name: 'French',            stt: 'fr-FR', voice: 'fr-FR-DeniseNeural',         voices: { female: 'fr-FR-DeniseNeural',      male: 'fr-FR-HenriNeural' } },
  { code: 'de', name: 'German',            stt: 'de-DE', voice: 'de-DE-KatjaNeural',          voices: { female: 'de-DE-KatjaNeural',       male: 'de-DE-ConradNeural' } },
  { code: 'it', name: 'Italian',           stt: 'it-IT', voice: 'it-IT-ElsaNeural',           voices: { female: 'it-IT-ElsaNeural',        male: 'it-IT-DiegoNeural' } },
  { code: 'nl', name: 'Dutch',             stt: 'nl-NL', voice: 'nl-NL-FennaNeural',          voices: { female: 'nl-NL-FennaNeural',       male: 'nl-NL-MaartenNeural' } },
  { code: 'pl', name: 'Polish',            stt: 'pl-PL', voice: 'pl-PL-AgnieszkaNeural',      voices: { female: 'pl-PL-AgnieszkaNeural',   male: 'pl-PL-MarekNeural' } },
  { code: 'cs', name: 'Czech',             stt: 'cs-CZ', voice: 'cs-CZ-VlastaNeural',         voices: { female: 'cs-CZ-VlastaNeural',      male: 'cs-CZ-AntoninNeural' } },
  { code: 'sv', name: 'Swedish',           stt: 'sv-SE', voice: 'sv-SE-SofieNeural',          voices: { female: 'sv-SE-SofieNeural',       male: 'sv-SE-MattiasNeural' } },
  { code: 'no', name: 'Norwegian',         stt: 'nb-NO', voice: 'nb-NO-PernilleNeural',       voices: { female: 'nb-NO-PernilleNeural',    male: 'nb-NO-FinnNeural' } },
  { code: 'da', name: 'Danish',            stt: 'da-DK', voice: 'da-DK-ChristelNeural',       voices: { female: 'da-DK-ChristelNeural',    male: 'da-DK-JeppeNeural' } },
  { code: 'fi', name: 'Finnish',           stt: 'fi-FI', voice: 'fi-FI-SelmaNeural',          voices: { female: 'fi-FI-SelmaNeural',       male: 'fi-FI-HarriNeural' } },
  { code: 'ga', name: 'Irish',             stt: 'ga-IE', voice: 'ga-IE-OrlaNeural',           voices: { female: 'ga-IE-OrlaNeural',        male: 'ga-IE-ColmNeural' } },
  { code: 'tr', name: 'Turkish',           stt: 'tr-TR', voice: 'tr-TR-EmelNeural',           voices: { female: 'tr-TR-EmelNeural',        male: 'tr-TR-AhmetNeural' } },
  { code: 'el', name: 'Greek',             stt: 'el-GR', voice: 'el-GR-AthinaNeural',         voices: { female: 'el-GR-AthinaNeural',      male: 'el-GR-NestorasNeural' } },
  { code: 'he', name: 'Hebrew',            stt: 'he-IL', voice: 'he-IL-HilaNeural',           voices: { female: 'he-IL-HilaNeural',        male: 'he-IL-AvriNeural' } },
  { code: 'ar', name: 'Arabic',            stt: 'ar-SA', voice: 'ar-SA-ZariyahNeural',        voices: { female: 'ar-SA-ZariyahNeural',     male: 'ar-SA-HamedNeural' } },
  { code: 'fa', name: 'Persian',           stt: 'fa-IR', voice: 'fa-IR-DilaraNeural',         voices: { female: 'fa-IR-DilaraNeural',      male: 'fa-IR-FaridNeural' } },
  { code: 'hi', name: 'Hindi',             stt: 'hi-IN', voice: 'hi-IN-SwaraNeural',          voices: { female: 'hi-IN-SwaraNeural',       male: 'hi-IN-MadhurNeural' } },
  { code: 'bn', name: 'Bengali',           stt: 'bn-IN', voice: 'bn-IN-TanishaaNeural',       voices: { female: 'bn-IN-TanishaaNeural',    male: 'bn-IN-BashkarNeural' } },
  { code: 'ta', name: 'Tamil',             stt: 'ta-IN', voice: 'ta-IN-PallaviNeural',        voices: { female: 'ta-IN-PallaviNeural',     male: 'ta-IN-ValluvarNeural' } },
  { code: 'te', name: 'Telugu',            stt: 'te-IN', voice: 'te-IN-ShrutiNeural',         voices: { female: 'te-IN-ShrutiNeural',      male: 'te-IN-MohanNeural' } },
  { code: 'th', name: 'Thai',              stt: 'th-TH', voice: 'th-TH-PremwadeeNeural',      voices: { female: 'th-TH-PremwadeeNeural',   male: 'th-TH-NiwatNeural' } },
  { code: 'vi', name: 'Vietnamese',        stt: 'vi-VN', voice: 'vi-VN-HoaiMyNeural',         voices: { female: 'vi-VN-HoaiMyNeural',      male: 'vi-VN-NamMinhNeural' } },
  { code: 'id', name: 'Indonesian',        stt: 'id-ID', voice: 'id-ID-GadisNeural',          voices: { female: 'id-ID-GadisNeural',       male: 'id-ID-ArdiNeural' } },
  { code: 'ms', name: 'Malay',             stt: 'ms-MY', voice: 'ms-MY-YasminNeural',         voices: { female: 'ms-MY-YasminNeural',      male: 'ms-MY-OsmanNeural' } },
  { code: 'fil', name: 'Filipino',         stt: 'fil-PH', voice: 'fil-PH-BlessicaNeural',     voices: { female: 'fil-PH-BlessicaNeural',   male: 'fil-PH-AngeloNeural' } },
  { code: 'zh', name: 'Chinese (Simplified)',  stt: 'zh-CN', voice: 'zh-CN-XiaoxiaoNeural',   voices: { female: 'zh-CN-XiaoxiaoNeural',    male: 'zh-CN-YunxiNeural' } },
  { code: 'zh-Hant', name: 'Chinese (Traditional)', stt: 'zh-HK', voice: 'zh-HK-HiuMaanNeural', voices: { female: 'zh-HK-HiuMaanNeural',   male: 'zh-HK-WanLungNeural' } },
  { code: 'ja', name: 'Japanese',          stt: 'ja-JP', voice: 'ja-JP-NanamiNeural',         voices: { female: 'ja-JP-NanamiNeural',      male: 'ja-JP-KeitaNeural' } },
  { code: 'ko', name: 'Korean',            stt: 'ko-KR', voice: 'ko-KR-SunHiNeural',          voices: { female: 'ko-KR-SunHiNeural',       male: 'ko-KR-InJoonNeural' } },
  { code: 'hu', name: 'Hungarian',         stt: 'hu-HU', voice: 'hu-HU-NoemiNeural',          voices: { female: 'hu-HU-NoemiNeural',       male: 'hu-HU-TamasNeural' } },
  { code: 'sk', name: 'Slovak',            stt: 'sk-SK', voice: 'sk-SK-ViktoriaNeural',       voices: { female: 'sk-SK-ViktoriaNeural',    male: 'sk-SK-LukasNeural' } },
  { code: 'sl', name: 'Slovenian',         stt: 'sl-SI', voice: 'sl-SI-PetraNeural',          voices: { female: 'sl-SI-PetraNeural',       male: 'sl-SI-RokNeural' } },
  { code: 'ro', name: 'Romanian',          stt: 'ro-RO', voice: 'ro-RO-AlinaNeural',          voices: { female: 'ro-RO-AlinaNeural',       male: 'ro-RO-EmilNeural' } },
  { code: 'bg', name: 'Bulgarian',         stt: 'bg-BG', voice: 'bg-BG-KalinaNeural',         voices: { female: 'bg-BG-KalinaNeural',      male: 'bg-BG-BorislavNeural' } },
  { code: 'hr', name: 'Croatian',          stt: 'hr-HR', voice: 'hr-HR-GabrijelaNeural',      voices: { female: 'hr-HR-GabrijelaNeural',   male: 'hr-HR-SreckoNeural' } },
  { code: 'sr', name: 'Serbian',           stt: 'sr-RS', voice: 'sr-RS-SophieNeural',         voices: { female: 'sr-RS-SophieNeural',      male: 'sr-RS-NicholasNeural' } },
  { code: 'et', name: 'Estonian',          stt: 'et-EE', voice: 'et-EE-AnuNeural',            voices: { female: 'et-EE-AnuNeural',         male: 'et-EE-KertNeural' } },
  { code: 'lv', name: 'Latvian',           stt: 'lv-LV', voice: 'lv-LV-EveritaNeural',        voices: { female: 'lv-LV-EveritaNeural',     male: 'lv-LV-NilsNeural' } },
  { code: 'lt', name: 'Lithuanian',        stt: 'lt-LT', voice: 'lt-LT-OnaNeural',            voices: { female: 'lt-LT-OnaNeural',         male: 'lt-LT-LeonasNeural' } },
  { code: 'ca', name: 'Catalan',           stt: 'ca-ES', voice: 'ca-ES-JoanaNeural',          voices: { female: 'ca-ES-JoanaNeural',       male: 'ca-ES-EnricNeural' } },
  { code: 'sw', name: 'Swahili',           stt: 'sw-KE', voice: 'sw-KE-ZuriNeural',           voices: { female: 'sw-KE-ZuriNeural',        male: 'sw-KE-RafikiNeural' } },
  { code: 'af', name: 'Afrikaans',         stt: 'af-ZA', voice: 'af-ZA-AdriNeural',           voices: { female: 'af-ZA-AdriNeural',        male: 'af-ZA-WillemNeural' } },
  { code: 'am', name: 'Amharic',           stt: 'am-ET', voice: 'am-ET-MekdesNeural',         voices: { female: 'am-ET-MekdesNeural',      male: 'am-ET-AmehaNeural' } },
  { code: 'km', name: 'Khmer',             stt: 'km-KH', voice: 'km-KH-SreymomNeural',        voices: { female: 'km-KH-SreymomNeural',     male: 'km-KH-PisethNeural' } },
  { code: 'kk', name: 'Kazakh',            stt: 'kk-KZ', voice: 'kk-KZ-AigulNeural',          voices: { female: 'kk-KZ-AigulNeural',       male: 'kk-KZ-DauletNeural' } },
  { code: 'uz', name: 'Uzbek',             stt: 'uz-UZ', voice: 'uz-UZ-MadinaNeural',         voices: { female: 'uz-UZ-MadinaNeural',      male: 'uz-UZ-SardorNeural' } },
  { code: 'mn', name: 'Mongolian',         stt: 'mn-MN', voice: 'mn-MN-YesuiNeural',          voices: { female: 'mn-MN-YesuiNeural',       male: 'mn-MN-BataaNeural' } },
  { code: 'zu', name: 'Zulu',              stt: 'zu-ZA', voice: 'zu-ZA-ThandoNeural',         voices: { female: 'zu-ZA-ThandoNeural',      male: 'zu-ZA-ThembaNeural' } },
];
const langMap = new Map(LANGUAGES.map(l => [l.code, l]));

// -------- Meeting local preferences (per user, per device) --------
const meetingPrefsKey = () => `meetingPrefs:${state.me?.id ?? 'anon'}`;
function loadMeetingPrefs() {
  try {
    const raw = localStorage.getItem(meetingPrefsKey());
    if (!raw) return { ttsEnabled: true, micDeviceId: null, recognitionMode: 's2s-realtime' };
    const parsed = JSON.parse(raw);
    // Migrate legacy recognitionMode values — only S2S is supported now.
    parsed.recognitionMode = 's2s-realtime';
    delete parsed.candidates;
    return { ttsEnabled: true, micDeviceId: null, ...parsed };
  } catch { return { ttsEnabled: true, micDeviceId: null, recognitionMode: 's2s-realtime' }; }
}
function saveMeetingPrefs(patch) {
  const merged = { ...loadMeetingPrefs(), ...patch };
  localStorage.setItem(meetingPrefsKey(), JSON.stringify(merged));
}
// Last language actually heard from another speaker in the current meeting —
// used to decide which language to translate the local user's speech into
// when the other side is a listener with no explicit "target lang" set.
let lastForeignLang = null;

// -------- Test personas (dev only) --------
// Pick with ?dev=<key> in the URL. The selection lasts for this page load only,
// so every new demo launch must name a valid persona. Backend accepts X-Dev-*
// headers only when running in the Development environment.
const DEV_PERSONAS = {
  sergey: { oid: '7bc3b904-679f-44a7-b0bf-fb6faf468a45', name: 'Sergey', email: 'sergeype@OopsLab.in', lang: 'en' },
  anna:   { oid: 'aaaa0000-0000-4000-8000-000000000002', name: 'Anna',   email: 'anna@example.com',   lang: 'ru' },
  john:   { oid: 'aaaa0000-0000-4000-8000-000000000003', name: 'John',   email: 'john@example.com',   lang: 'en' },
  indre:  { oid: 'a28b7a25-c533-4f0a-bd5f-42927c43a72d', name: 'Indre',  email: 'indre@example.com',  lang: 'en' },
  hossam: { oid: 'c8019d2e-b983-4395-a436-bc0e5fa7ba14', name: 'Hossam', email: 'hossam@example.com', lang: 'en' },
  rob:    { oid: 'eb80423c-f260-42d1-9f08-ba4e2c6ad83b', name: 'Rob',    email: 'rob@example.com',    lang: 'en' },
  yasser: { oid: 'aaaa0000-0000-4000-8000-000000000004', name: 'Yasser', email: 'yasser@example.com', lang: 'en' },
  oilbrk1: { oid: 'aaaa0000-0000-4000-8000-000000000005', name: 'Oilbrk 1', email: 'oilbrk1@example.com', lang: 'en' },
  oilbrk2: { oid: 'aaaa0000-0000-4000-8000-000000000006', name: 'Oilbrk 2', email: 'oilbrk2@example.com', lang: 'en' },
  oilbrk3: { oid: 'aaaa0000-0000-4000-8000-000000000007', name: 'Oilbrk 3', email: 'oilbrk3@example.com', lang: 'en' },
  wren_user1: { oid: '421567e0-f620-44be-aee1-09c971e8787c', name: 'Wren User 1', email: 'wren_user1@example.com', lang: 'en' },
  wren_user2: { oid: '39b62a04-a69b-46ec-b3a3-6dee84170bf3', name: 'Wren User 2', email: 'wren_user2@example.com', lang: 'en' },
  wren_user3: { oid: '31d6210f-465d-4a51-bf9f-12b28c040d75', name: 'Wren User 3', email: 'wren_user3@example.com', lang: 'en' },
  wren_user4: { oid: '69aa4d52-d0f8-4ee5-8e31-f1cc58bd3f9d', name: 'Wren User 4', email: 'wren_user4@example.com', lang: 'en' },
  wren_user5: { oid: '9f575678-fce6-4a08-ae54-6e08378166d5', name: 'Wren User 5', email: 'wren_user5@example.com', lang: 'en' },
  wren_user6: { oid: '3e6dcd79-3e02-40b0-ab1e-4bfe1e7cdc95', name: 'Wren User 6', email: 'wren_user6@example.com', lang: 'en' },
  wren_user7: { oid: 'b04c5908-3c68-4493-af8f-2f4b17051fbf', name: 'Wren User 7', email: 'wren_user7@example.com', lang: 'en' },
  wren_user8: { oid: 'fb1bc624-deda-47cc-9039-53d0835785ef', name: 'Wren User 8', email: 'wren_user8@example.com', lang: 'en' },
  wren_user9: { oid: '061ba746-89c1-475a-a804-4dbfc6e65e21', name: 'Wren User 9', email: 'wren_user9@example.com', lang: 'en' },
  wren_user10: { oid: '1e27bc4b-f14b-4402-8b3b-7dcbc4efa3f6', name: 'Wren User 10', email: 'wren_user10@example.com', lang: 'en' },
};
let selectedDevPersonaKey = null;
(() => {
  const url = new URL(location.href);
  const dev = url.searchParams.get('dev');
  if (dev && DEV_PERSONAS[dev]) selectedDevPersonaKey = dev;
})();
const currentDevPersona = () => {
  const key = selectedDevPersonaKey;
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
      let code = null;
      try {
        const j = await res.json();
        msg = j.error || j.title || msg;
        code = j.code || null;
      } catch {}
      throw new ApiError(res.status, msg, code);
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
  translate:   (text, to, from, soften) => api._req('/api/translate', { method: 'POST', body: { text, to, from: from ?? null, soften: soften ?? false } }),
  ttsSynth:    (text, voice, signal)    => {
    const headers = { 'Content-Type': 'application/json' };
    const dev = currentDevPersona();
    if (dev) {
      headers['X-Dev-Oid'] = dev.oid;
      headers['X-Dev-Name'] = dev.name;
      headers['X-Dev-Email'] = dev.email;
      headers['X-Dev-Lang'] = dev.lang;
    }
    return fetch('/api/speech/tts', {
      method: 'POST',
      headers,
      signal,
      body: JSON.stringify({ text, voice }),
    });
  },
};

class AuthError extends Error {}
class ApiError extends Error {
  constructor(status, msg, code = null) {
    super(msg);
    this.status = status;
    this.code = code;
  }
}

// -------- Toasts --------
const toast = (msg, kind = 'info', detail = '') => {
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  const main = document.createElement('div');
  main.textContent = msg;
  el.appendChild(main);
  if (detail) {
    const d = document.createElement('div');
    d.className = 'toast-detail';
    d.textContent = detail;
    el.appendChild(d);
  }
  document.getElementById('toasts').appendChild(el);
  // Longer stay-on-screen for errors with detail so people can read the code.
  const ttl = kind === 'error' ? (detail ? 12000 : 6000) : 4000;
  setTimeout(() => el.remove(), ttl);
  el.addEventListener('click', () => el.remove());
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

addRoute(/^#\/talk$/, () => showTalk());
addRoute(/^#\/training$/, () => showTraining());
addRoute(/^#?\/?$/, () => showTalk());

const currentPath = () => window.location.hash || window.location.pathname;
async function route() {
  const path = currentPath();
  for (const r of routes) {
    const m = path.match(r.regex);
    if (m) { try { await r.handler(m); } catch (e) { handleError(e); } return; }
  }
  location.hash = '#/talk';
}
window.addEventListener('hashchange', route);
window.addEventListener('popstate', route);

// -------- Screens --------
const screens = ['loading', 'signin', 'access-denied', 'load-error', 'talk', 'training', 'chats', 'meetings', 'meeting-room', 'join'];
function show(name) {
  screens.forEach(s => document.getElementById('screen-' + s).hidden = s !== name);
  document.querySelectorAll('#tabs a').forEach(a => {
    a.classList.toggle('active',
      (name === 'talk' && a.dataset.tab === 'talk') ||
      (name === 'training' && a.dataset.tab === 'training'));
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
  lastForeignLang = null;
  document.getElementById('room-title').textContent = room.title ?? 'Meeting';
  document.getElementById('room-status').textContent = room.status;
  document.getElementById('room-end').hidden = !(room.initiator.id === state.me.id && room.status === 'active');
  document.getElementById('tts-toggle').checked = loadMeetingPrefs().ttsEnabled !== false;
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
    getClientAccessUrl: async () => privatePubSubUrl(url),
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

// TTS toggle
document.getElementById('tts-toggle').addEventListener('change', (e) => {
  saveMeetingPrefs({ ttsEnabled: e.target.checked });
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

async function getSpeechConfig(sdk, forTranslation) {
  const endpoint = new URL(`/api/relay/speech/${forTranslation ? 's2s' : 'stt'}/speech/universal/v2`, location.href);
  endpoint.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  return forTranslation
    ? sdk.SpeechTranslationConfig.fromEndpoint(endpoint, 'app-service-relay')
    : sdk.SpeechConfig.fromEndpoint(endpoint, 'app-service-relay');
}

function privatePubSubUrl(accessUrl) {
  const endpoint = new URL('/api/relay/pubsub', location.href);
  endpoint.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  endpoint.searchParams.set('access_token', new URL(accessUrl).searchParams.get('access_token') ?? '');
  return endpoint.toString();
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

  unlockTtsAudio();

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
    const myLang = state.meeting.participants.find(p => p.id === state.me.id)?.language ?? state.me.preferredLanguage;

    // Per-user candidate list of languages we expect to hear. Defaults to
    // union of participants' languages; user can override in the meeting-room
    // side panel (persisted per user in localStorage).
    const prefs = loadMeetingPrefs();
    const meetingLangs = [...new Set(state.meeting.participants.map(p => p.language))];
    const chosen = (prefs.candidates && prefs.candidates.length > 0)
      ? prefs.candidates
      : meetingLangs;
    // Always include the mic owner's own language in the STT candidate set
    // so their own speech is always recognised even if not in prefs.
    const sourceLangs = [...new Set([myLang, ...chosen])];

    const sttCandidates = sourceLangs
      .map(l => langMap.get(l)?.stt)
      .filter(Boolean)
      .slice(0, 4);
    if (sttCandidates.length === 0) sttCandidates.push(langMap.get(myLang)?.stt ?? 'en-US');

    // Target languages we always request from Speech Translation. Includes:
    //  - our own language (so foreign speech gets translated to us),
    //  - the candidate list (so our speech can be translated to whoever spoke),
    //  - other meeting participants' languages (so fan-out has every listener covered).
    const targets = [...new Set([myLang, ...sourceLangs, ...meetingLangs])];

    const autoDetect = sdk.AutoDetectSourceLanguageConfig.fromLanguages(sttCandidates);
    targets.forEach(l => cfg.addTargetLanguage(l));

    // Enable Continuous Language ID so Speech re-evaluates the language on
    // every utterance instead of locking to whichever it picked first.
    // Property name is a hard-coded string because different Speech SDK
    // builds expose the enum under slightly different names.
    cfg.setProperty('SpeechServiceConnection_LanguageIdMode', 'Continuous');
    cfg.setProperty('SpeechServiceConnection_ContinuousLanguageIdPriority', 'Latency');

    const audio = sdk.AudioConfig.fromDefaultMicrophoneInput();
    const rec = sdk.TranslationRecognizer.FromConfig(cfg, autoDetect, audio);
    window.__recognizer = rec;

    rec.recognizing = (_s, e) => {
      // Interim (partial) result — great UX signal that the mic is picking
      // up audio at all.
      if (e.result.text) hint.textContent = '🎧 …' + e.result.text.slice(-60);
    };
    rec.recognized = async (_s, e) => {
      window.__lastRecognized = e.result;
      const reasonName = Object.entries(sdk.ResultReason).find(([, v]) => v === e.result.reason)?.[0] ?? String(e.result.reason);

      const detectedStt = sdk.AutoDetectSourceLanguageResult.fromResult(e.result)?.language;
      const detectedLang = detectedStt
        ? (LANGUAGES.find(l => l.stt === detectedStt)?.code ?? detectedStt.split('-')[0])
        : myLang;

      console.log('[Speech] recognized', reasonName, 'text=', e.result.text, 'detected=', detectedStt, '->', detectedLang, 'translations=', e.result.translations && Array.from(e.result.translations.languages));

      if (e.result.reason === sdk.ResultReason.NoMatch) {
        hint.textContent = 'No speech detected — try again.';
        return;
      }
      const original = e.result.text?.trim();
      if (!original) { hint.textContent = 'Empty result (' + reasonName + ').'; return; }

      const isMine = detectedLang === myLang;
      // Only remember someone else's language as a reply target.
      if (!isMine) lastForeignLang = detectedLang;

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
      if (!translations[detectedLang]) translations[detectedLang] = original;

      hint.textContent = `🎙️ (${detectedLang}) ${original.slice(0, 60)}`;

      try {
        const { turnId, createdAt } = await api.postTurn(state.meeting.id, {
          original, sourceLang: detectedLang, translations, addresseeId: null,
        });

        // What the mic owner sees on their own timeline:
        //  - If the speaker was somebody else -> show translation in myLang.
        //  - If it was themselves -> show translation in the last foreign
        //    language we heard (so the reply reads in the other side's
        //    language). Falls back to their own language if nobody else has
        //    spoken yet.
        const displayLang = isMine
          ? (lastForeignLang ?? myLang)
          : myLang;

        renderSubtitle({
          id: turnId,
          speakerId: state.me.id, // logical author: whoever spoke into this device
          sourceLang: detectedLang,
          callerLang: displayLang,
          original,
          translatedForCaller: translations[displayLang] ?? original,
          createdAt,
        });

        // Optional TTS. Only speak the foreign side to the mic owner.
        const ttsEnabled = loadMeetingPrefs().ttsEnabled !== false;
        if (ttsEnabled && !isMine && translations[myLang]) {
          speak(translations[myLang], myLang).catch(() => {});
        }
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

// Server-side Neural TTS: POST /api/speech/tts returns MP3, played through
// a single reused <audio> element. Beats client-side Speech SDK because:
//  * Real Azure Neural voices (not robotic system TTS).
//  * Mobile browsers only apply the autoplay policy on the *first* play() of
//    an audio element created outside a user gesture. By reusing one element
//    that was unlocked on the first Start-talking tap, subsequent turns play
//    without gesture required.
//  * MP3 caches at the CDN/HTTP layer, so repeated phrases are instant.
let ttsQueue = [];
let ttsBusy = false;
let ttsAudio = null;
let ttsCache = new Map();       // key: `${voice}|${text}` -> objectURL
let ttsUnlocked = false;

function ensureAudioEl() {
  if (ttsAudio) return ttsAudio;
  ttsAudio = new Audio();
  ttsAudio.preload = 'auto';
  ttsAudio.setAttribute('playsinline', '');
  return ttsAudio;
}

// Call from within a user gesture handler (Start-talking tap, TTS toggle tap)
// so subsequent programmatic play() calls are allowed on iOS/Android.
function unlockTtsAudio() {
  if (ttsUnlocked) return;
  const a = ensureAudioEl();
  // Silent 0.1s WAV data URL (empty PCM) — just to satisfy the autoplay gate.
  a.src = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAgD4AAIA+AAABAAgAZGF0YQAAAAA=';
  const p = a.play();
  if (p && typeof p.catch === 'function') p.catch(() => {});
  ttsUnlocked = true;
}

async function speak(text, lang, gender) {
  if (!text) return;
  const langInfo = langMap.get(lang);
  const voice = (gender === 'male' && langInfo?.voices?.male)
    ? langInfo.voices.male
    : (langInfo?.voices?.female ?? langInfo?.voice);
  if (!voice) {
    console.warn('[TTS] No Neural voice mapped for', lang);
    return;
  }
  ttsQueue.push({ text, voice, lang });
  if (ttsBusy) return;
  ttsBusy = true;
  try {
    while (ttsQueue.length > 0) {
      const item = ttsQueue.shift();
      try {
        await _playNeural(item.text, item.voice);
      } catch (e) {
        console.warn('[TTS] Neural path failed:', e);
        toast(`Voice output error (${item.lang})`, 'error', e.message);
      }
    }
  } finally {
    ttsBusy = false;
  }
}

async function _playNeural(text, voice) {
  const key = `${voice}|${text}`;
  let url = ttsCache.get(key);
  if (!url) {
    const resp = await api.ttsSynth(text, voice);
    if (!resp.ok) {
      const body = await resp.text().catch(() => '');
      throw new Error(`HTTP ${resp.status}: ${body.slice(0, 200)}`);
    }
    const blob = await resp.blob();
    url = URL.createObjectURL(blob);
    ttsCache.set(key, url);
    // Keep the cache from growing unbounded.
    if (ttsCache.size > 40) {
      const oldest = ttsCache.keys().next().value;
      URL.revokeObjectURL(ttsCache.get(oldest));
      ttsCache.delete(oldest);
    }
  }
  const a = ensureAudioEl();
  a.src = url;
  await new Promise((resolve, reject) => {
    let done = false;
    const onEnd = () => { if (done) return; done = true; cleanup(); resolve(); };
    const onErr = (e) => { if (done) return; done = true; cleanup(); reject(new Error('audio play error: ' + (a.error?.message || 'unknown'))); };
    const cleanup = () => {
      a.removeEventListener('ended', onEnd);
      a.removeEventListener('error', onErr);
    };
    a.addEventListener('ended', onEnd);
    a.addEventListener('error', onErr);
    const p = a.play();
    if (p && typeof p.catch === 'function') p.catch(err => {
      if (done) return; done = true; cleanup(); reject(err);
    });
  });
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
    const choice = prompt(`Switch to test persona (${keys.join(', ')}).`, currentDevPersona()?.key ?? '');
    if (choice === null) return;
    if (!DEV_PERSONAS[choice]) { toast('Unknown persona: ' + choice, 'error'); return; }
    const url = new URL(location.href);
    url.searchParams.set('dev', choice);
    location.assign(url);
  }
});

// -------- Talk screen (local live translator, no meeting/backend) --------
let talkRecognizer = null;
let talkLastForeign = null;
let talkActive = false;
let talkStarting = false;
let talkSessionId = 0;
let talkWakeLock = null;
let talkRestartOnVisible = false;
let talkVisibilityStopPromise = Promise.resolve();
let talkRecoveryInProgress = false;

async function requestTalkWakeLock(sessionId) {
  if (!('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
  if (sessionId !== talkSessionId || (!talkStarting && !talkActive)) return;
  try {
    const lock = await navigator.wakeLock.request('screen');
    if (sessionId !== talkSessionId || (!talkStarting && !talkActive)) {
      await lock.release();
      return;
    }
    if (talkWakeLock && talkWakeLock !== lock) {
      try { await talkWakeLock.release(); } catch {}
    }
    talkWakeLock = lock;
    lock.addEventListener('release', () => {
      if (talkWakeLock === lock) talkWakeLock = null;
    });
  } catch (err) {
    console.warn('[Talk] Screen Wake Lock unavailable:', err);
  }
}

async function releaseTalkWakeLock() {
  const lock = talkWakeLock;
  talkWakeLock = null;
  if (lock) {
    try { await lock.release(); } catch {}
  }
}

async function resumeTalkAudioContexts(sessionId) {
  if (sessionId !== talkSessionId || document.visibilityState !== 'visible') return;
  for (const ctx of [talkAudioCtx, s2sPlaybackCtx]) {
    if (ctx?.state === 'suspended') {
      try { await ctx.resume(); } catch (err) {
        console.warn('[Talk] AudioContext resume failed:', err);
      }
    }
  }
}

async function showTalk() {
  await stopTraining();
  show('talk');
  syncTalkControls();
}

// -------- English training --------
let trainingWs = null;
let trainingStream = null;
let trainingCaptureContext = null;
let trainingPlaybackContext = null;
let trainingWorklet = null;
let trainingPlaybackAt = 0;
let trainingCurrent = null;
let trainingSignalTimer = null;
let trainingPeak = 0;
let trainingFramesReceived = 0;
let trainingFinishing = false;
let trainingFlush = null;
let trainingFinishTimer = null;
let trainingResponseDone = false;
let trainingOriginalDone = false;

const guidedTraining = new GuidedTraining({
  createUrl: () => {
    const url = new URL('/api/training/live', location.href);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    url.searchParams.set('mode', 'guided');
    url.searchParams.set('protocol', 'guided.v1');
    const dev = currentDevPersona();
    if (dev) {
      url.searchParams.set('X-Dev-Oid', dev.oid);
      url.searchParams.set('X-Dev-Name', dev.name);
      url.searchParams.set('X-Dev-Email', dev.email);
      url.searchParams.set('X-Dev-Lang', dev.lang);
    }
    return url.href;
  },
  microphoneId: () => loadMeetingPrefs().micDeviceId,
  synthesize: (text, signal) => api.ttsSynth(text, 'en-GB-SoniaNeural', signal),
});
document.getElementById('training-mode').addEventListener('change', async event => {
  await stopTraining();
  const guided = event.target.value === 'guided' && state.me?.canUseGuidedTraining;
  document.getElementById('training-free').hidden = guided;
  document.getElementById('training-guided').hidden = !guided;
  document.getElementById('training-status').hidden = guided;
});
window.addEventListener('pagehide', () => { void stopTraining(); });

async function showTraining() {
  if (!state.me?.canUseTraining) {
    location.hash = '#/talk';
    return;
  }
  if (talkActive || talkStarting) await stopTalk();
  show('training');
  syncTalkControls();
}

async function startTraining() {
  const button = document.getElementById('training-mic');
  const status = document.getElementById('training-status');
  const hint = document.getElementById('training-hint');
  const micId = loadMeetingPrefs().micDeviceId;
  trainingStream = await navigator.mediaDevices.getUserMedia({
    audio: {
      channelCount: 1,
      echoCancellation: true,
      noiseSuppression: true,
      autoGainControl: true,
      ...(micId ? { deviceId: { exact: micId } } : {}),
    },
  });
  const microphoneTrack = trainingStream.getAudioTracks()[0];
  const microphoneName = microphoneTrack?.label || 'system default microphone';
  const AudioContextClass = window.AudioContext || window.webkitAudioContext;
  trainingCaptureContext = new AudioContextClass();
  trainingPlaybackContext = new AudioContextClass({ sampleRate: 24000 });
  if (trainingCaptureContext.state === 'suspended') await trainingCaptureContext.resume();
  if (trainingPlaybackContext.state === 'suspended') await trainingPlaybackContext.resume();
  await trainingCaptureContext.audioWorklet.addModule('/pcm-processor.js?v=2');
  const source = trainingCaptureContext.createMediaStreamSource(trainingStream);
  trainingWorklet = new AudioWorkletNode(trainingCaptureContext, 'pcm-processor');
  trainingWorklet.port.postMessage({ targetRate: 24000 });
  source.connect(trainingWorklet);

  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const query = new URLSearchParams();
  const dev = currentDevPersona();
  if (dev) {
    query.set('X-Dev-Oid', dev.oid);
    query.set('X-Dev-Name', dev.name);
    query.set('X-Dev-Email', dev.email);
    query.set('X-Dev-Lang', dev.lang);
  }
  trainingWs = new WebSocket(`${protocol}//${location.host}/api/training/live?${query}`);
  trainingWs.binaryType = 'arraybuffer';
  const pending = [];
  trainingWorklet.port.onmessage = (event) => {
    if (event.data?.type === 'flushed') { trainingFlush?.(); return; }
    if (!(event.data instanceof ArrayBuffer)) return;
    trainingFramesReceived++;
    const samples = new Int16Array(event.data);
    for (let index = 0; index < samples.length; index++) {
      trainingPeak = Math.max(trainingPeak, Math.abs(samples[index]) / 0x8000);
    }
    if (trainingPeak >= 0.015 && !trainingFinishing) {
      hint.textContent = `Microphone active: ${microphoneName}`;
      if (trainingSignalTimer) clearTimeout(trainingSignalTimer);
      trainingSignalTimer = null;
    }
    if (trainingWs?.readyState === WebSocket.OPEN) trainingWs.send(event.data);
    else if (pending.length < 200) pending.push(event.data);
  };
  trainingWs.onopen = () => {
    trainingCurrent = null;
    ensureTrainingTurn();
    button.classList.add('recording');
    button.querySelector('.mic-label').textContent = 'Stop training';
    status.textContent = 'Listening';
    hint.textContent = `Listening through ${microphoneName}. Speak now…`;
    trainingSignalTimer = setTimeout(() => {
      if (trainingPeak < 0.015) {
        hint.textContent = trainingFramesReceived === 0
          ? `Microphone processing did not start (${trainingCaptureContext?.state || 'unknown'}). Stop and try again.`
          : `No audio signal from ${microphoneName}. Check the Windows input device and its level.`;
      }
    }, 4000);
    while (pending.length) trainingWs.send(pending.shift());
  };
  trainingWs.onmessage = (event) => {
    if (event.data instanceof ArrayBuffer) {
      playTrainingAudio(event.data);
      return;
    }
    let message;
    try { message = JSON.parse(event.data); } catch { return; }
    handleTrainingMessage(message);
  };
  trainingWs.onerror = () => { hint.textContent = 'Training connection failed.'; };
  trainingWs.onclose = () => { if (trainingWs) void stopTraining(); };
}

function handleTrainingMessage(message) {
  const results = document.getElementById('training-results');
  const hint = document.getElementById('training-hint');
  if (message.type === 'turn_started' && !trainingCurrent) {
    if (results.querySelector('.muted')) results.innerHTML = '';
    trainingCurrent = document.createElement('div');
    trainingCurrent.className = 'training-turn';
    trainingCurrent.dataset.original = '';
    trainingCurrent.dataset.correction = '';
    results.appendChild(trainingCurrent);
    renderTrainingTurn();
  } else if (message.type === 'original_delta' || message.type === 'original_done') {
    ensureTrainingTurn();
    trainingCurrent.dataset.original = message.type.endsWith('done')
      ? message.text || trainingCurrent.dataset.original
      : trainingCurrent.dataset.original + (message.text || '');
    hint.textContent = `You: ${trainingCurrent.dataset.original.slice(-80)}`;
    renderTrainingTurn();
    if (message.type === 'original_done') {
      trainingOriginalDone = true;
      completeTrainingPlayback();
    }
  } else if (message.type === 'correction_delta' || message.type === 'correction_done') {
    ensureTrainingTurn();
    trainingCurrent.dataset.correction = message.type.endsWith('done')
      ? message.text || trainingCurrent.dataset.correction
      : trainingCurrent.dataset.correction + (message.text || '');
    renderTrainingTurn();
    results.scrollTop = results.scrollHeight;
  } else if (message.type === 'response_done') {
    trainingResponseDone = true;
    completeTrainingPlayback();
  } else if (message.type === 'error') {
    toast(message.message || 'Training model error', 'error');
    void stopTraining();
  }
}

function ensureTrainingTurn() {
  if (!trainingCurrent) handleTrainingMessage({ type: 'turn_started' });
}

function renderTrainingTurn() {
  if (!trainingCurrent) return;
  const original = trainingCurrent.dataset.original || '';
  const correction = trainingCurrent.dataset.correction || '';
  trainingCurrent.innerHTML = `<div class="training-message training-message-user">${escapeHtml(original || '…')}</div>`
    + (correction ? `<div class="training-message training-message-coach">${escapeHtml(correction)}</div>` : '');
}

function playTrainingAudio(arrayBuffer) {
  if (!trainingPlaybackContext) return;
  const input = new Int16Array(arrayBuffer);
  const buffer = trainingPlaybackContext.createBuffer(1, input.length, 24000);
  const output = buffer.getChannelData(0);
  for (let index = 0; index < input.length; index++) output[index] = input[index] / 0x8000;
  const source = trainingPlaybackContext.createBufferSource();
  const playbackContext = trainingPlaybackContext;
  source.buffer = buffer;
  source.connect(trainingPlaybackContext.destination);
  source.onended = () => {
    source.disconnect();
    if (trainingPlaybackContext === playbackContext) completeTrainingPlayback();
  };
  const startAt = Math.max(trainingPlaybackContext.currentTime, trainingPlaybackAt);
  source.start(startAt);
  trainingPlaybackAt = startAt + buffer.duration;
}

function completeTrainingPlayback() {
  if (!trainingFinishing || !trainingResponseDone || !trainingOriginalDone) return;
  if (trainingFinishTimer) clearTimeout(trainingFinishTimer);
  trainingFinishTimer = null;
  if (trainingPlaybackContext && trainingPlaybackContext.currentTime < trainingPlaybackAt) return;
  trainingFinishing = false;
  void stopTraining();
}

async function finishTraining() {
  if (trainingFinishing || trainingWs?.readyState !== WebSocket.OPEN) return;
  trainingFinishing = true;
  const socket = trainingWs;
  const button = document.getElementById('training-mic');
  button.disabled = true;
  button.classList.remove('recording');
  button.querySelector('.mic-label').textContent = 'Processing';
  document.getElementById('training-status').textContent = 'Translating';
  document.getElementById('training-hint').textContent = 'Processing your recording…';
  if (trainingSignalTimer) clearTimeout(trainingSignalTimer);
  trainingSignalTimer = null;
  await trainingCaptureContext.suspend();
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      trainingFlush = null;
      reject(new Error('Could not finish microphone recording. Please try again.'));
    }, 2000);
    trainingFlush = () => { clearTimeout(timeout); trainingFlush = null; resolve(); };
    trainingWorklet.port.postMessage({ type: 'flush' });
  });
  if (trainingWs !== socket) return;
  trainingWorklet.port.onmessage = null;
  trainingWorklet.disconnect();
  trainingWorklet = null;
  trainingStream?.getTracks().forEach(track => track.stop());
  trainingStream = null;
  await trainingCaptureContext.close();
  trainingCaptureContext = null;
  if (trainingWs !== socket) return;
  socket.send(JSON.stringify({ type: 'recording.finish' }));
  trainingFinishTimer = setTimeout(() => {
    toast('The recording could not be completed in time. Please try again.', 'error');
    void stopTraining();
  }, 180000);
}

async function stopTraining() {
  await guidedTraining.stop();
  const socket = trainingWs;
  trainingWs = null;
  if (socket) {
    socket.onclose = null;
    try { socket.close(1000, 'stop'); } catch {}
  }
  if (trainingWorklet) {
    trainingWorklet.port.onmessage = null;
    try { trainingWorklet.disconnect(); } catch {}
    trainingWorklet = null;
  }
  trainingStream?.getTracks().forEach(track => track.stop());
  trainingStream = null;
  try { await trainingCaptureContext?.close(); } catch {}
  try { await trainingPlaybackContext?.close(); } catch {}
  trainingCaptureContext = null;
  trainingPlaybackContext = null;
  trainingPlaybackAt = 0;
  trainingFinishing = false;
  trainingResponseDone = false;
  trainingOriginalDone = false;
  trainingFlush?.();
  trainingFlush = null;
  if (trainingFinishTimer) clearTimeout(trainingFinishTimer);
  trainingFinishTimer = null;
  if (trainingSignalTimer) clearTimeout(trainingSignalTimer);
  trainingSignalTimer = null;
  trainingPeak = 0;
  trainingFramesReceived = 0;
  trainingCurrent = null;
  const button = document.getElementById('training-mic');
  button.disabled = false;
  button.classList.remove('recording');
  button.querySelector('.mic-label').textContent = 'Start training';
  document.getElementById('training-status').textContent = 'Idle';
  document.getElementById('training-hint').textContent = 'Press Start training and grant microphone access.';
}

document.getElementById('training-mic').addEventListener('click', async () => {
  try { if (trainingWs) await finishTraining(); else await startTraining(); }
  catch (error) { await stopTraining(); handleError(error); }
});
document.getElementById('training-mic-device').addEventListener('click', openMicDeviceDialog);
document.getElementById('training-clear').addEventListener('click', () => {
  document.getElementById('training-results').innerHTML = '<div class="muted center">Nothing yet.</div>';
  trainingCurrent = null;
});

function syncTalkControls() {
  const prefs = loadMeetingPrefs();
  const on = prefs.ttsEnabled !== false;
  const toggle = document.querySelector('#talk-tts .tool-toggle');
  if (toggle) {
    toggle.dataset.on = String(on);
    toggle.textContent = on ? 'On' : 'Off';
  }
  // Mic device description — resolves the stored deviceId to a friendly label
  // if we've seen the device before, otherwise shows "System default".
  const micDesc = document.getElementById('talk-mic-device-desc');
  const trainingMicDesc = document.getElementById('training-mic-device-desc');
  for (const desc of [micDesc, trainingMicDesc]) {
    if (!desc) continue;
    const id = prefs.micDeviceId;
    if (!id) {
      desc.textContent = 'System default (browser chooses)';
    } else {
      const cached = knownMicDevices.get(id);
      desc.textContent = cached || 'Custom microphone';
    }
  }
  // Recognition mode description
  const modeDesc = document.getElementById('talk-mode-desc');
  if (modeDesc) {
    const modeId = prefs.recognitionMode ?? 'gpt-diarize';
    const meta = (typeof RECOGNITION_MODES !== 'undefined' && RECOGNITION_MODES.find(m => m.id === modeId));
    modeDesc.textContent = meta ? meta.title : 'Balanced (recommended)';
  }
}

// Cache label lookups so syncTalkControls doesn't need to await enumerateDevices.
const knownMicDevices = new Map(); // deviceId -> label

async function enumerateMicDevices({ promptForAccess = false } = {}) {
  if (promptForAccess) {
    // Device labels are hidden by browsers until the user has granted mic
    // permission at least once. A one-shot getUserMedia unlocks them.
    try {
      const s = await navigator.mediaDevices.getUserMedia({ audio: true });
      s.getTracks().forEach(t => t.stop());
    } catch (e) {
      console.warn('[Mic] permission request failed:', e);
    }
  }
  const devices = await navigator.mediaDevices.enumerateDevices();
  const mics = devices.filter(d => d.kind === 'audioinput');
  mics.forEach(m => { if (m.deviceId && m.label) knownMicDevices.set(m.deviceId, m.label); });
  return mics;
}

async function openMicDeviceDialog() {
  const list = document.getElementById('mic-device-list');
  const dlg = document.getElementById('mic-device-dialog');
  list.innerHTML = '<div class="muted center">Loading microphones…</div>';
  dlg.showModal();

  let mics = await enumerateMicDevices();
  const hasLabels = mics.some(m => m.label);
  if (!hasLabels) {
    // First time — we need permission for labels. Prompt now.
    list.innerHTML = '<div class="muted center">Grant microphone permission when prompted…</div>';
    mics = await enumerateMicDevices({ promptForAccess: true });
  }

  const current = loadMeetingPrefs().micDeviceId ?? '';
  const options = [
    { id: '', label: 'System default (browser chooses)' },
    ...mics.map(m => ({ id: m.deviceId, label: m.label || `Microphone (${m.deviceId.slice(0, 8)}…)` })),
  ];
  list.innerHTML = options.map(o => `
    <label class="lang-row${o.id === current ? ' selected' : ''}" data-id="${escapeAttr(o.id)}">
      <span class="lang-name">${escapeHtml(o.label)}</span>
      <input type="radio" name="mic-choice" value="${escapeAttr(o.id)}" ${o.id === current ? 'checked' : ''} />
      <span class="lang-check"></span>
    </label>`).join('');

  list.onclick = (e) => {
    const label = e.target.closest('label[data-id]');
    if (!label) return;
    e.preventDefault();
    list.querySelectorAll('label').forEach(l => l.classList.remove('selected'));
    label.classList.add('selected');
    label.querySelector('input').checked = true;
  };

  document.getElementById('mic-device-save').onclick = async () => {
    const chosen = list.querySelector('input[name="mic-choice"]:checked')?.value ?? '';
    saveMeetingPrefs({ micDeviceId: chosen || null });
    dlg.close();
    syncTalkControls();
    if (guidedTraining.active) {
      await guidedTraining.stop('Microphone changed. Start a new attempt.');
    } else if (trainingWs) {
      toast('Restarting training with new microphone…', 'success');
      await stopTraining();
      setTimeout(() => startTraining(), 250);
    } else if (talkActive) {
      toast('Restarting recognition with new microphone…', 'success');
      await stopTalk();
      setTimeout(() => startTalk(), 250);
    } else {
      toast('Microphone updated.', 'success');
    }
  };
}

function escapeAttr(s) { return String(s).replace(/"/g, '&quot;'); }

// Turn history — needed for "manual re-tap": user picks a different source
// language, we re-translate the original text and update the bubble in place.
const talkTurns = new Map(); // turnId -> { original, sourceLang, targetLang, isMine, el }
let talkTurnSeq = 0;

function talkRender({ original, translated, sourceLang, targetLang, isMine, uncertain = false, speakerLabel = null, createdAt = new Date().toISOString() }) {
  const container = document.getElementById('talk-timeline');
  if (container.querySelector('.muted')) container.innerHTML = '';
  const turnId = `t-${++talkTurnSeq}`;
  const el = document.createElement('div');
  el.dataset.turnId = turnId;
  el.className = 'subtitle' + (isMine ? '' : ' foreign');
  if (speakerLabel) {
    el.style.setProperty('--speaker-color', colorForSpeaker(speakerLabel));
    el.classList.add('has-speaker');
  }
  el.innerHTML = subtitleInnerHtml({ original, translated, sourceLang, targetLang, isMine, uncertain, speakerLabel, createdAt, turnId });
  wireSubtitleActions(el, turnId);
  container.appendChild(el);
  container.scrollTop = container.scrollHeight;
  talkTurns.set(turnId, { original, sourceLang, targetLang, isMine, el, speakerLabel });
  return turnId;
}

function subtitleInnerHtml({ original, translated, sourceLang, targetLang, isMine, uncertain, speakerLabel, createdAt, turnId }) {
  const sameLang = !translated || translated.trim() === original.trim() || sourceLang === targetLang;
  const srcLabel = escapeHtml(langName(sourceLang)) + (uncertain ? '<span class="uncertain-mark" title="Detection uncertain — tap language to change">?</span>' : '');
  const srcPill = `<button type="button" class="lang-pill${uncertain ? ' uncertain' : ''}" data-action="repick" data-turn="${turnId}" title="Tap to change source language">${srcLabel}</button>`;
  const who = speakerLabel
    ? `<span class="speaker-tag" style="background:var(--speaker-color)"></span>${escapeHtml(speakerLabel)}`
    : (isMine ? 'You' : 'Them');
  const arrow = sameLang ? '' : ` → ${escapeHtml(langName(targetLang))}`;
  const header = `${who} — ${srcPill}${arrow} · ${formatTime(createdAt)}`;
  return sameLang
    ? `<div class="who">${header}</div><div class="translated">${escapeHtml(original)}</div>`
    : `<div class="who">${header}</div>
       <div class="translated">${escapeHtml(translated)}</div>
       <div class="original">${escapeHtml(original)}</div>`;
}

function wireSubtitleActions(el, turnId) {
  const btn = el.querySelector('[data-action="repick"]');
  if (btn) btn.addEventListener('click', (e) => { e.stopPropagation(); openRepickMenu(turnId, btn); });
}

// Popup with candidate languages so the user can force-fix a wrong detection
// for a single bubble. Re-runs /api/translate with `from=...`.
function openRepickMenu(turnId, anchor) {
  const turn = talkTurns.get(turnId);
  if (!turn) return;
  const myLang = state.me?.preferredLanguage ?? 'en';
  const prefs = loadMeetingPrefs();
  const candidates = (prefs.candidates && prefs.candidates.length > 0) ? prefs.candidates : [myLang];
  const options = [...new Set([myLang, ...candidates])];

  document.querySelectorAll('.repick-menu').forEach(m => m.remove());
  const menu = document.createElement('div');
  menu.className = 'repick-menu';
  menu.innerHTML = `<div class="muted small" style="padding:6px 12px;">Actually spoken in…</div>` +
    options.map(code => `<button type="button" class="repick-opt" data-code="${code}">${escapeHtml(langName(code))}</button>`).join('');
  document.body.appendChild(menu);
  const rect = anchor.getBoundingClientRect();
  menu.style.left = `${Math.max(8, rect.left)}px`;
  menu.style.top  = `${rect.bottom + 4}px`;
  const close = (e) => { if (!menu.contains(e.target)) { menu.remove(); document.removeEventListener('click', close, true); } };
  setTimeout(() => document.addEventListener('click', close, true), 0);

  menu.querySelectorAll('.repick-opt').forEach(b => b.addEventListener('click', async (e) => {
    e.stopPropagation();
    const forced = b.dataset.code;
    menu.remove();
    document.removeEventListener('click', close, true);
    await recomputeTurn(turnId, forced);
  }));
}

async function recomputeTurn(turnId, forcedSourceLang) {
  const turn = talkTurns.get(turnId);
  if (!turn) return;
  const myLang = state.me?.preferredLanguage ?? 'en';
  const isMine = forcedSourceLang === myLang;
  // Choose target lang using the same reply-target logic.
  let targetLang;
  if (isMine) {
    targetLang = pickReplyTarget(myLang);
  } else {
    targetLang = myLang;
    talkLastForeign = forcedSourceLang;
  }
  turn.el.classList.add('recomputing');
  try {
    let translated = turn.original;
    if (forcedSourceLang !== targetLang) {
      const r = await api.translate(turn.original, targetLang, forcedSourceLang, true);
      translated = r.translatedText;
    }
    turn.sourceLang = forcedSourceLang;
    turn.targetLang = targetLang;
    turn.isMine = isMine;
    turn.el.className = 'subtitle' + (isMine ? '' : ' foreign');
    turn.el.innerHTML = subtitleInnerHtml({
      original: turn.original, translated,
      sourceLang: forcedSourceLang, targetLang,
      isMine, uncertain: false,
      createdAt: new Date().toISOString(), turnId,
    });
    wireSubtitleActions(turn.el, turnId);
    // Speak the corrected translation if TTS is enabled and it's actually a
    // cross-language turn.
    const ttsEnabled = loadMeetingPrefs().ttsEnabled !== false;
    if (ttsEnabled && translated && translated.trim() !== turn.original.trim()) {
      const gender = talkGenders.get(forcedSourceLang);
      speak(translated, targetLang, gender).catch(() => {});
    }
  } catch (err) {
    console.warn('[Talk] recompute failed:', err);
    toast('Re-translate failed', 'error', err.message ?? String(err));
  } finally {
    turn.el.classList.remove('recomputing');
  }
}

// ---------- Pitch tracker (gender heuristic for TTS voice selection) ----------
// Runs an independent getUserMedia stream in parallel with the Speech SDK's
// microphone so we can estimate the fundamental frequency (F0) of the current
// speaker and cache "male vs female" per detected language. Cheap ACF-based
// pitch detector — accurate enough for the M/F split (~85% correct).
let pitchStream = null;
let pitchCtx = null;
let pitchAnalyser = null;
let pitchTimer = null;
let pitchBuffer = [];            // ring: recent F0 samples with timestamp
const talkGenders = new Map();   // detectedLang -> 'male' | 'female'

async function startPitchTracker() {
  if (pitchStream) return;
  try {
    const micId = loadMeetingPrefs().micDeviceId;
    const audioConstraint = micId
      ? { deviceId: { exact: micId }, echoCancellation: true, noiseSuppression: true }
      : { echoCancellation: true, noiseSuppression: true };
    pitchStream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraint });
    const AC = window.AudioContext || window.webkitAudioContext;
    pitchCtx = new AC();
    const src = pitchCtx.createMediaStreamSource(pitchStream);
    pitchAnalyser = pitchCtx.createAnalyser();
    pitchAnalyser.fftSize = 2048;
    src.connect(pitchAnalyser);
    const buf = new Float32Array(pitchAnalyser.fftSize);
    pitchTimer = setInterval(() => {
      pitchAnalyser.getFloatTimeDomainData(buf);
      const f0 = autoCorrelate(buf, pitchCtx.sampleRate);
      if (f0 > 60 && f0 < 400) {
        pitchBuffer.push({ t: Date.now(), f0 });
        // keep last 3 seconds only
        while (pitchBuffer.length && Date.now() - pitchBuffer[0].t > 3000) pitchBuffer.shift();
      }
    }, 100);
  } catch (e) {
    console.warn('[Pitch] tracker unavailable:', e.message);
    pitchStream = null;
  }
}

function stopPitchTracker() {
  if (pitchTimer) { clearInterval(pitchTimer); pitchTimer = null; }
  if (pitchCtx) { try { pitchCtx.close(); } catch {} pitchCtx = null; }
  if (pitchStream) { pitchStream.getTracks().forEach(t => t.stop()); pitchStream = null; }
  pitchAnalyser = null;
  pitchBuffer = [];
}

// Median F0 of samples in the last `windowMs` milliseconds.
function recentPitchMedian(windowMs = 1500) {
  const cutoff = Date.now() - windowMs;
  const recent = pitchBuffer.filter(p => p.t >= cutoff).map(p => p.f0).sort((a, b) => a - b);
  if (recent.length < 5) return null;
  return recent[Math.floor(recent.length / 2)];
}

// Simple normalised auto-correlation pitch detector. Returns Hz or -1.
function autoCorrelate(buf, sampleRate) {
  let size = buf.length;
  let rms = 0;
  for (let i = 0; i < size; i++) rms += buf[i] * buf[i];
  rms = Math.sqrt(rms / size);
  if (rms < 0.01) return -1;

  let r1 = 0, r2 = size - 1, threshold = 0.2;
  for (let i = 0; i < size / 2; i++) if (Math.abs(buf[i]) < threshold) { r1 = i; break; }
  for (let i = 1; i < size / 2; i++) if (Math.abs(buf[size - i]) < threshold) { r2 = size - i; break; }
  buf = buf.slice(r1, r2);
  size = buf.length;

  const c = new Array(size).fill(0);
  for (let lag = 0; lag < size; lag++) {
    for (let i = 0; i < size - lag; i++) c[lag] += buf[i] * buf[i + lag];
  }
  let d = 0;
  while (c[d] > c[d + 1]) d++;
  let maxVal = -1, maxPos = -1;
  for (let i = d; i < size; i++) if (c[i] > maxVal) { maxVal = c[i]; maxPos = i; }
  if (maxPos < 1) return -1;
  const T = maxPos;
  return sampleRate / T;
}

function pickReplyTarget(myLang) {
  // Choose which language to translate our own speech INTO. Priority:
  //   1. Last foreign speaker's language — if it's still in the current
  //      candidate list. (User may have removed it since we last heard it.)
  //   2. First non-mine language in the current candidate list — so that
  //      even before any foreign voice is heard we translate to a real
  //      peer language rather than falling back to same-language echo.
  //   3. myLang (only 1 language chosen → no translation to do).
  const prefs = loadMeetingPrefs();
  const candidates = (prefs.candidates && prefs.candidates.length > 0) ? prefs.candidates : [myLang];
  if (talkLastForeign && candidates.includes(talkLastForeign)) return talkLastForeign;
  const firstForeign = candidates.find(c => c !== myLang);
  return firstForeign ?? myLang;
}

// State for server-side transcription (Sprint 2).
let talkWs = null;
let talkAudioCtx = null;
let talkAudioStream = null;
let talkWorkletNode = null;
const talkSpeakerColors = new Map(); // speakerId -> palette index

async function startTalk({ recovering = false } = {}) {
  const btn = document.getElementById('talk-mic');
  const hint = document.getElementById('talk-hint');
  const status = document.getElementById('talk-status');
  if (talkActive || talkStarting) {
    if (!recovering) await stopTalk();
    return;
  }

  const sessionId = ++talkSessionId;
  talkStarting = true;

  // Unlock the <audio> element for autoplay while we still have a user gesture.
  unlockTtsAudio();
  await requestTalkWakeLock(sessionId);

  try {
    const micId = loadMeetingPrefs().micDeviceId;
    const audioConstraint = micId ? { deviceId: { exact: micId } } : true;
    const probe = await navigator.mediaDevices.getUserMedia({ audio: audioConstraint });
    probe.getTracks().forEach(t => t.stop());
  } catch (err) {
    if (sessionId !== talkSessionId) return;
    talkStarting = false;
    await releaseTalkWakeLock();
    toast('Microphone permission denied: ' + (err.message || err.name), 'error');
    hint.textContent = 'Microphone permission denied.';
    return;
  }
  if (sessionId !== talkSessionId) return;

  const mode = 's2s-realtime';
  try {
    await startTalkS2S(btn, hint, status, sessionId);
  } catch (e) {
    if (sessionId !== talkSessionId) return;
    await stopTalk();
    if (recovering) {
      hint.textContent = 'Could not resume automatically. Tap Start talking.';
    }
    handleError(e);
  }
}

// Only S2S mode is exposed now — the picker below is kept as a no-op stub in
// case any UI still calls it.
const RECOGNITION_MODES = [
  { id: 's2s-realtime', title: 'Ultra-fast voice (GPT S2S)', desc: 'GPT Realtime Translate — speech-in, speech-out. Bi-directional, sub-second latency.' },
];

async function openModeDialog() {
  const list = document.getElementById('mode-list');
  const dlg = document.getElementById('mode-dialog');
  const current = loadMeetingPrefs().recognitionMode ?? 'gpt-diarize';
  list.innerHTML = RECOGNITION_MODES.map(m => `
    <label class="lang-row${m.id === current ? ' selected' : ''}" data-id="${escapeAttr(m.id)}">
      <span class="lang-name">
        <span style="font-weight:600">${escapeHtml(m.title)}</span>
        <span class="muted small" style="display:block; margin-top:2px;">${escapeHtml(m.desc)}</span>
      </span>
      <input type="radio" name="mode-choice" value="${escapeAttr(m.id)}" ${m.id === current ? 'checked' : ''} />
      <span class="lang-check"></span>
    </label>`).join('');
  list.onclick = (e) => {
    const label = e.target.closest('label[data-id]');
    if (!label) return;
    e.preventDefault();
    list.querySelectorAll('label').forEach(l => l.classList.remove('selected'));
    label.classList.add('selected');
    label.querySelector('input').checked = true;
  };
  document.getElementById('mode-save').onclick = async () => {
    const chosen = list.querySelector('input[name="mode-choice"]:checked')?.value ?? 'gpt-diarize';
    saveMeetingPrefs({ recognitionMode: chosen });
    dlg.close();
    syncTalkControls();
    if (talkActive) {
      toast('Restarting recognition with new mode…', 'success');
      await stopTalk();
      setTimeout(() => startTalk(), 250);
    } else {
      toast('Recognition mode updated.', 'success');
    }
  };
  dlg.showModal();
}

// ---------- Sprint 2: server-side ConversationTranscriber ----------
async function startTalkServer(btn, hint, status, endpointPath = '/api/speech/transcribe', extraQuery = {}) {
  const myLang = state.me.preferredLanguage;
  const prefs = loadMeetingPrefs();
  const chosen = (prefs.candidates && prefs.candidates.length > 0) ? prefs.candidates : [myLang];
  const sourceLangs = [...new Set([myLang, ...chosen])];
  const sttCandidates = sourceLangs.map(l => langMap.get(l)?.stt).filter(Boolean).slice(0, 4);
  if (sttCandidates.length === 0) sttCandidates.push(langMap.get(myLang)?.stt ?? 'en-US');

  // The GPT Realtime API requires 24 kHz PCM; Azure Speech
  // ConversationTranscriber uses 16 kHz. Pick per endpoint.
  const targetSampleRate = endpointPath.includes('transcribe-gpt') ? 24000 : 16000;

  const micId = prefs.micDeviceId;
  const constraints = { audio: {
    channelCount: 1,
    echoCancellation: true, noiseSuppression: true, autoGainControl: true,
    ...(micId ? { deviceId: { exact: micId } } : {}),
  } };
  talkAudioStream = await navigator.mediaDevices.getUserMedia(constraints);

  const AC = window.AudioContext || window.webkitAudioContext;
  talkAudioCtx = new AC(); // let the browser choose its default (44.1 or 48 kHz)
  await talkAudioCtx.audioWorklet.addModule('/pcm-processor.js?v=2');
  const src = talkAudioCtx.createMediaStreamSource(talkAudioStream);
  talkWorkletNode = new AudioWorkletNode(talkAudioCtx, 'pcm-processor');
  // Configure the resampler for the endpoint's expected sample rate.
  talkWorkletNode.port.postMessage({ targetRate: targetSampleRate });
  src.connect(talkWorkletNode);
  // Don't connect worklet to destination — no monitor loopback.

  // Buffer any PCM frames that arrive BEFORE the WebSocket completes its
  // opening handshake. Otherwise the first ~200ms of audio (i.e. the very
  // first spoken syllable) is lost, which is why users see "not hearing me".
  let wsReady = false;
  const pending = [];
  talkWorkletNode.port.onmessage = (e) => {
    if (wsReady && talkWs && talkWs.readyState === WebSocket.OPEN) {
      talkWs.send(e.data);
    } else if (pending.length < 200) { // ~4 seconds max before we drop
      pending.push(e.data);
    }
  };

  // Open the WebSocket. Dev headers are passed via query string because
  // browsers don't allow custom headers on WebSocket handshakes.
  const wsProto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const dev = currentDevPersona();
  const qs = new URLSearchParams({ langs: sttCandidates.join(',') });
  for (const [k, v] of Object.entries(extraQuery)) qs.set(k, v);
  if (dev) {
    qs.set('X-Dev-Oid', dev.oid);
    qs.set('X-Dev-Name', dev.name);
    qs.set('X-Dev-Email', dev.email);
    qs.set('X-Dev-Lang', dev.lang);
  }
  const url = `${wsProto}//${location.host}${endpointPath}?${qs.toString()}`;
  talkWs = new WebSocket(url);
  talkWs.binaryType = 'arraybuffer';

  talkWs.onopen = () => {
    talkActive = true;
    wsReady = true;
    btn.querySelector('.mic-label').textContent = 'Stop talking';
    btn.classList.add('recording');
    status.textContent = 'Listening';
    hint.textContent = '🎙️ Listening… speak now.';
    startPitchTracker();
    // Flush anything the worklet captured before we finished the handshake.
    while (pending.length) {
      try { talkWs.send(pending.shift()); } catch { break; }
    }
  };
  talkWs.onmessage = async (ev) => {
    let msg;
    try { msg = JSON.parse(ev.data); } catch { return; }
    if (msg.type === 'transcribing') {
      const preview = msg.text?.slice(-80) ?? '';
      hint.textContent = `🎧 ${labelForSpeaker(msg.speakerId)}: …${preview}`;
    } else if (msg.type === 'transcribed') {
      await handleServerTranscribed(msg, myLang, hint);
    } else if (msg.type === 'canceled') {
      // The server only forwards this for real errors — EndOfStream is
      // auto-restarted server-side and we should never see it.
      const detail = msg.errorDetails || msg.reason || 'unknown';
      console.warn('[Talk] transcriber canceled:', detail);
      // Don't tear down for cancellations — the server has already restarted
      // the underlying transcriber if this was a benign event. Show a hint
      // but keep the mic open.
      hint.textContent = '⚠ Recognition hiccup, still listening… (' + detail + ')';
    } else if (msg.type === 'error') {
      toast('Transcription error: ' + (msg.message || 'unknown'), 'error');
      hint.textContent = 'Recognition stopped: ' + (msg.message || 'error');
      stopTalk();
    }
  };
  talkWs.onerror = (e) => {
    console.warn('[Talk] WebSocket error:', e);
    // Fall through — onclose will fire and clean up.
  };
  talkWs.onclose = () => {
    if (talkActive) stopTalk();
  };
}

// Palette of subtle background tints for speaker bubbles. Cycles.
const SPEAKER_PALETTE = ['#059669', '#0369a1', '#b45309', '#7c3aed', '#be185d', '#0891b2', '#65a30d', '#c2410c'];

function labelForSpeaker(rawId) {
  if (!rawId || rawId === '?' || rawId === 'Unknown') return 'Speaker ?';
  // Speech SDK gives "Guest-1", "Guest-2", …  Simplify.
  const m = String(rawId).match(/\d+/);
  return m ? `Speaker ${m[0]}` : `Speaker ${rawId}`;
}

function colorForSpeaker(rawId) {
  const label = labelForSpeaker(rawId);
  if (!talkSpeakerColors.has(label)) {
    talkSpeakerColors.set(label, SPEAKER_PALETTE[talkSpeakerColors.size % SPEAKER_PALETTE.length]);
  }
  return talkSpeakerColors.get(label);
}

// ---------- Sprint 4: full speech-to-speech via gpt-realtime-translate ----------
// Streams mic PCM to /api/speech/translate-live, receives translated audio
// (binary WS frames of 24 kHz PCM) which we schedule into an AudioContext for
// gap-free playback. Sub-second latency, no separate translator/TTS calls.
let s2sPlaybackCtx = null;
let s2sPlaybackTime = 0;
let s2sCurrentBubble = null;
let s2sOriginalText = '';
let s2sTranslatedText = '';
let s2sTranslationLanguage = '';
// Foundry's translations endpoint streams audio continuously (including
// silence). Treat a chunk as "voiced" only when RMS crosses a small threshold.
const S2S_VOICE_RMS = 0.008;
// Same threshold applied to the MIC input to detect whether the SPEAKER is
// currently talking. When the speaker is talking we buffer translated audio
// but do NOT play it; we only start playback after the speaker has been
// silent for S2S_SPEAKER_SILENCE_MS.
const S2S_MIC_VOICE_RMS = 0.012;
const S2S_SPEAKER_SILENCE_MS = 450;
// Minimum pre-buffer of voiced translated audio before playback can start,
// even if the speaker is already silent. Smooths per-chunk jitter.
const S2S_PRELOAD_MS = 250;
// Timestamp (performance.now) of the last mic chunk that was above the
// speaker-voice threshold. Updated in the worklet onmessage handler.
let s2sMicLastVoicedAt = 0;
// Per-language playback queues so bi-directional translation from parallel
// Foundry sessions doesn't collide.
const s2sQueues = new Map();
function getQueue(lang) {
  let q = s2sQueues.get(lang);
  if (!q) {
    q = {
      buffered: [],
      startedAt: 0,
      nextAt: 0,
      bufferedMs: 0,
      bufferStartedAt: 0,
      lastVoicedAt: 0,
      flushTimer: null,
    };
    s2sQueues.set(lang, q);
  }
  return q;
}

async function startTalkS2S(btn, hint, status, sessionId) {
  const myLang = state.me.preferredLanguage;
  const prefs = loadMeetingPrefs();
  const micId = prefs.micDeviceId;

  const constraints = { audio: {
    channelCount: 1,
    echoCancellation: true, noiseSuppression: true, autoGainControl: true,
    ...(micId ? { deviceId: { exact: micId } } : {}),
  } };
  const audioStream = await navigator.mediaDevices.getUserMedia(constraints);
  if (sessionId !== talkSessionId) {
    audioStream.getTracks().forEach(t => t.stop());
    return;
  }
  talkAudioStream = audioStream;

  const AC = window.AudioContext || window.webkitAudioContext;
  talkAudioCtx = new AC();
  await talkAudioCtx.audioWorklet.addModule('/pcm-processor.js?v=2');
  if (sessionId !== talkSessionId) return;
  const src = talkAudioCtx.createMediaStreamSource(talkAudioStream);
  talkWorkletNode = new AudioWorkletNode(talkAudioCtx, 'pcm-processor');
  talkWorkletNode.port.postMessage({ targetRate: 24000 });
  src.connect(talkWorkletNode);

  // Separate AudioContext for playback so its scheduling isn't tied to the
  // capture context (which prevents echo/loop feedback on some devices).
  s2sPlaybackCtx = new AC({ sampleRate: 24000 });
  s2sPlaybackTime = 0;
  s2sCurrentBubble = null;
  s2sOriginalText = '';
  s2sTranslatedText = '';
  s2sTranslationLanguage = '';

  let wsReady = false;
  const pending = [];
  s2sMicLastVoicedAt = 0;
  talkWorkletNode.port.onmessage = (e) => {
    if (sessionId !== talkSessionId) return;
    // Speaker VAD: compute RMS of outgoing PCM (24 kHz int16 mono) and
    // remember the last time the mic was voiced. Playback of translated
    // audio is delayed until this timestamp is stale for a threshold.
    try {
      const arr = new Int16Array(e.data);
      let sumSq = 0;
      const step = Math.max(1, Math.floor(arr.length / 512));
      let n = 0;
      for (let i = 0; i < arr.length; i += step) {
        const s = arr[i] / 0x8000;
        sumSq += s * s;
        n++;
      }
      const rms = Math.sqrt(sumSq / Math.max(1, n));
      if (rms >= S2S_MIC_VOICE_RMS) {
        s2sMicLastVoicedAt = performance.now();
        for (const q of s2sQueues.values()) {
          if (q.buffered.length) requestS2SFlush(q);
        }
      }
    } catch { /* ignore */ }

    if (wsReady && talkWs === ws && ws.readyState === WebSocket.OPEN) {
      ws.send(e.data);
    } else if (pending.length < 200) {
      pending.push(e.data);
    }
  };

  const wsProto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const dev = currentDevPersona();
  // Source language is auto-detected. The backend uses this profile language
  // as "my language" and dynamically chooses the opposite direction.
  const qs = new URLSearchParams({
    targetLang: myLang,
  });
  if (dev) {
    qs.set('X-Dev-Oid', dev.oid);
    qs.set('X-Dev-Name', dev.name);
    qs.set('X-Dev-Email', dev.email);
    qs.set('X-Dev-Lang', dev.lang);
  }
  const url = `${wsProto}//${location.host}/api/speech/translate-live?${qs.toString()}`;
  const ws = new WebSocket(url);
  talkWs = ws;
  ws.binaryType = 'arraybuffer';

  ws.onopen = () => {
    if (sessionId !== talkSessionId || talkWs !== ws) {
      try { ws.close(1000, 'superseded'); } catch {}
      return;
    }
    talkStarting = false;
    talkActive = true;
    wsReady = true;
    btn.querySelector('.mic-label').textContent = 'Stop talking';
    btn.classList.add('recording');
    status.textContent = 'Listening (S2S)';
    hint.textContent = `🎙️ Listening… your language is ${langName(myLang)}.`;
    void requestTalkWakeLock(sessionId);
    void resumeTalkAudioContexts(sessionId);
    while (pending.length) {
      try { ws.send(pending.shift()); } catch { break; }
    }
  };

  ws.onmessage = async (ev) => {
    if (sessionId !== talkSessionId || talkWs !== ws) return;
    // Binary frames = translated audio, framed as [1B langLen][lang UTF-8][PCM 24k int16 mono]
    if (ev.data instanceof ArrayBuffer) {
      const dv = new DataView(ev.data);
      const langLen = dv.getUint8(0);
      const lang = new TextDecoder().decode(new Uint8Array(ev.data, 1, langLen));
      const audio = ev.data.slice(1 + langLen);
      enqueueS2SAudio(lang, audio);
      return;
    }
    let msg;
    try { msg = JSON.parse(ev.data); } catch { return; }
    if (msg.type === 'turn_started') {
      prepareS2SQueuesForTurn();
      s2sCurrentBubble = null;
      s2sOriginalText = '';
      s2sTranslatedText = '';
      s2sTranslationLanguage = '';
      hint.textContent = '🎙️ Listening…';
    } else if (msg.type === 'translation_delta') {
      s2sTranslationLanguage = msg.lang || s2sTranslationLanguage;
      s2sTranslatedText += (msg.text || '');
      renderS2SBubble(hint);
    } else if (msg.type === 'translation_done') {
      s2sTranslationLanguage = msg.lang || s2sTranslationLanguage;
      if (msg.text) s2sTranslatedText = msg.text;
      renderS2SBubble(hint);
    } else if (msg.type === 'original_delta') {
      s2sOriginalText += (msg.text || '');
      hint.textContent = `🎧 ${s2sOriginalText.slice(0, 60)}`;
      renderS2SBubble(hint);
    } else if (msg.type === 'original_done') {
      s2sOriginalText = msg.text || s2sOriginalText;
      hint.textContent = `🎧 ${s2sOriginalText.slice(0, 60)}`;
      renderS2SBubble(hint);
    } else if (msg.type === 'routing_updated') {
      const reply = msg.replyLanguage || myLang;
      hint.textContent = `🎙️ ${langName(myLang)} ↔ ${langName(reply)}`;
    } else if (msg.type === 'error') {
      console.warn('[S2S] upstream error:', msg.message);
      const h = document.getElementById('talk-hint');
      if (h) h.textContent = '⚠️ ' + (msg.message || 'transient error');
    }
  };
  ws.onerror = (e) => {
    if (sessionId === talkSessionId && talkWs === ws) {
      console.warn('[S2S] WebSocket error:', e);
    }
  };
  ws.onclose = () => {
    if (sessionId !== talkSessionId || talkWs !== ws) return;
    talkStarting = false;
    if (talkActive) void stopTalk();
  };
}

function renderS2SBubble(hint) {
  // Don't create a bubble unless we actually have text to show. Foundry's
  // translations endpoint pumps audio chunks continuously (including silence)
  // and doesn't currently emit text deltas, so if we have neither original
  // nor translated text there's nothing meaningful to render yet.
  if (!s2sOriginalText && !s2sTranslatedText) return;
  if (!s2sCurrentBubble) {
    const container = document.getElementById('talk-timeline');
    if (container.querySelector('.muted')) container.innerHTML = '';
    const turnId = `s2s-${++talkTurnSeq}`;
    const el = document.createElement('div');
    el.dataset.turnId = turnId;
    el.className = 'subtitle foreign';
    container.appendChild(el);
    container.scrollTop = container.scrollHeight;
    s2sCurrentBubble = el;
  }
  const targetLang = s2sTranslationLanguage || state.me?.preferredLanguage || 'en';
  const who = targetLang === state.me?.preferredLanguage ? 'Them' : 'You';
  const now = new Date().toISOString();
  const header = `${who} — ${escapeHtml(langName(targetLang))} · ${formatTime(now)}`;
  s2sCurrentBubble.innerHTML = `
    <div class="who">${header}</div>
    ${s2sTranslatedText ? `<div class="translated">${escapeHtml(s2sTranslatedText)}</div>` : ''}
    ${s2sOriginalText ? `<div class="original">${escapeHtml(s2sOriginalText)}</div>` : ''}`;
  const container = document.getElementById('talk-timeline');
  container.scrollTop = container.scrollHeight;
}

function playS2SAudioChunk(arrayBuffer) {
  // Kept for API compatibility; new pipeline routes via enqueueS2SAudio.
  enqueueS2SAudio('_', arrayBuffer);
}

// Convert one incoming 24 kHz int16 mono chunk into a Float32 buffer and add
// it to the per-language playback queue. Only voiced chunks are queued —
// silent chunks (Foundry idles between utterances) are ignored. The backend
// releases audio only after it has classified the completed source phrase;
// this extra client gate preserves smooth playback under network jitter.
function enqueueS2SAudio(lang, arrayBuffer) {
  if (!s2sPlaybackCtx) return;
  const int16 = new Int16Array(arrayBuffer);
  if (int16.length === 0) return;
  const float = new Float32Array(int16.length);
  let sumSq = 0;
  for (let i = 0; i < int16.length; i++) {
    const s = int16[i] / 0x8000;
    float[i] = s;
    sumSq += s * s;
  }
  const rms = Math.sqrt(sumSq / float.length);
  const q = getQueue(lang);
  const nowMs = performance.now();

  if (rms < S2S_VOICE_RMS) {
    // Never discard buffered voiced audio. A silent tail can be the final
    // event for a short phrase, so it must trigger a timed flush instead.
    if (q.buffered.length) requestS2SFlush(q);
    if (q.lastVoicedAt && (nowMs - q.lastVoicedAt) > 1200
        && q.nextAt <= s2sPlaybackCtx.currentTime + 0.05) {
      q.startedAt = 0;
      q.nextAt = 0;
      q.lastVoicedAt = 0;
    }
    return;
  }
  q.lastVoicedAt = nowMs;

  const durMs = (float.length / 24000) * 1000;

  if (q.startedAt === 0) {
    if (!q.bufferStartedAt) q.bufferStartedAt = nowMs;
    q.buffered.push(float);
    q.bufferedMs += durMs;
    requestS2SFlush(q);
    return;
  }

  // Playback is already in progress for this language. Schedule new chunks
  // directly until the next silence in the translation stream resets state.
  scheduleS2SBuffer(q, float);
}

function requestS2SFlush(q) {
  if (!s2sPlaybackCtx || !q.buffered.length) return;
  if (q.flushTimer) clearTimeout(q.flushTimer);

  const nowMs = performance.now();
  const speakerSilentMs = s2sMicLastVoicedAt === 0
    ? Number.POSITIVE_INFINITY
    : nowMs - s2sMicLastVoicedAt;
  const pauseWait = Math.max(0, S2S_SPEAKER_SILENCE_MS - speakerSilentMs);
  const preloadWait = q.bufferedMs >= S2S_PRELOAD_MS
    ? 0
    : Math.max(0, S2S_PRELOAD_MS - (nowMs - q.bufferStartedAt));
  const waitMs = Math.max(pauseWait, preloadWait);

  if (waitMs === 0) {
    flushS2SQueue(q);
    return;
  }
  q.flushTimer = setTimeout(() => {
    q.flushTimer = null;
    requestS2SFlush(q);
  }, waitMs + 5);
}

function flushS2SQueue(q) {
  if (!s2sPlaybackCtx || !q.buffered.length) return;
  q.startedAt = Math.max(s2sPlaybackCtx.currentTime, q.nextAt);
  q.nextAt = q.startedAt;
  for (const float of q.buffered) scheduleS2SBuffer(q, float);
  q.buffered.length = 0;
  q.bufferedMs = 0;
  q.bufferStartedAt = 0;
  const hint = document.getElementById('talk-hint');
  if (hint) hint.textContent = '🔊 Playing translation…';
}

function prepareS2SQueuesForTurn() {
  for (const q of s2sQueues.values()) {
    const playbackFinished = !s2sPlaybackCtx
      || q.nextAt <= s2sPlaybackCtx.currentTime + 0.05;
    if (playbackFinished && q.buffered.length === 0) {
      q.startedAt = 0;
      q.nextAt = 0;
      q.lastVoicedAt = 0;
    } else if (q.buffered.length) {
      // A new transcript turn can arrive before the previous translation's
      // final audio frames are played. Keep those frames instead of clipping
      // the previous phrase in the middle of a word.
      requestS2SFlush(q);
    }
  }
}

function scheduleS2SBuffer(q, float) {
  if (!s2sPlaybackCtx) return;
  const buffer = s2sPlaybackCtx.createBuffer(1, float.length, 24000);
  buffer.getChannelData(0).set(float);
  const startAt = Math.max(s2sPlaybackCtx.currentTime, q.nextAt);
  const src = s2sPlaybackCtx.createBufferSource();
  src.buffer = buffer;
  src.connect(s2sPlaybackCtx.destination);
  src.start(startAt);
  q.nextAt = startAt + buffer.duration;
  // Keep the global scheduling clock in sync (used elsewhere for stopTalk).
  if (q.nextAt > s2sPlaybackTime) s2sPlaybackTime = q.nextAt;
}

async function handleServerTranscribed(msg, myLang, hint) {
  const original = msg.text?.trim();
  if (!original) return;
  const detectedStt = msg.language || '';
  const sdkGuess = detectedStt
    ? (LANGUAGES.find(l => l.stt === detectedStt)?.code ?? detectedStt.split('-')[0])
    : myLang;
  const speakerLabel = labelForSpeaker(msg.speakerId);

  hint.textContent = `🎙️ (${sdkGuess}?) ${speakerLabel}: ${original.slice(0, 60)}`;

  const pitchNow = recentPitchMedian(1500);
  const speakerGender = pitchNow == null ? null : (pitchNow < 155 ? 'male' : 'female');

  try {
    let firstResp = await api.translate(original, myLang, null, true);
    let detectedLang = (firstResp.detectedSourceLanguage || sdkGuess).split('-')[0];
    let isMine = detectedLang === myLang;
    let targetLang, translated;
    if (speakerGender) talkGenders.set(detectedLang, speakerGender);

    const currentPrefs = loadMeetingPrefs();
    const currentCandidates = (currentPrefs.candidates && currentPrefs.candidates.length > 0) ? currentPrefs.candidates : [myLang];
    if (talkLastForeign && !currentCandidates.includes(talkLastForeign)) talkLastForeign = null;

    if (!isMine) {
      if (currentCandidates.includes(detectedLang)) talkLastForeign = detectedLang;
      targetLang = myLang;
      translated = firstResp.translatedText;
    } else {
      targetLang = pickReplyTarget(myLang);
      if (targetLang === myLang) translated = original;
      else {
        const r2 = await api.translate(original, targetLang, detectedLang, true);
        translated = r2.translatedText;
      }
    }

    const uncertain = (sdkGuess && detectedLang && sdkGuess !== detectedLang);
    talkRender({ original, translated, sourceLang: detectedLang, targetLang, isMine, uncertain, speakerLabel });

    const ttsEnabled = loadMeetingPrefs().ttsEnabled !== false;
    const spokenText = translated && translated.trim() !== original.trim() ? translated : null;
    if (ttsEnabled && spokenText) {
      const genderSource = isMine ? talkLastForeign : detectedLang;
      const gender = genderSource ? talkGenders.get(genderSource) : null;
      speak(spokenText, targetLang, gender).catch(err => console.warn('[TTS] speak() rejected:', err));
    }
  } catch (err) {
    console.warn('[Talk] translate failed:', err);
    talkRender({ original, translated: original, sourceLang: sdkGuess, targetLang: sdkGuess, isMine: true, speakerLabel });
  }
}

// ---------- Client-side Speech SDK (legacy fallback) ----------
async function startTalkClientSdk(btn, hint, status) {
    const sdk = await ensureSpeechSdk();
    const myLang = state.me.preferredLanguage;

    const prefs = loadMeetingPrefs();
    const chosen = (prefs.candidates && prefs.candidates.length > 0) ? prefs.candidates : [myLang];
    const sourceLangs = [...new Set([myLang, ...chosen])];
    const sttCandidates = sourceLangs.map(l => langMap.get(l)?.stt).filter(Boolean).slice(0, 4);
    if (sttCandidates.length === 0) sttCandidates.push(langMap.get(myLang)?.stt ?? 'en-US');

    // Continuous speech-to-text with continuous LID. Translation and TTS are
    // handled server-side once we have the recognised text — this gives us:
    //  * Reliable per-utterance language detection (SpeechRecognizer supports
    //    Continuous LID, unlike TranslationRecognizer which is at-start only).
    //  * Access to the Translator "auto-detect" mode as a second opinion when
    //    the SDK's LID picks a wrong candidate.
    //  * A single long-lived microphone session (no per-turn mic re-open).
    const cfg = await getSpeechConfig(sdk, false);
    cfg.setProperty('SpeechServiceConnection_LanguageIdMode', 'Continuous');
    cfg.setProperty('SpeechServiceConnection_ContinuousLanguageIdPriority', 'Accuracy');
    // Give the user room to breathe mid-sentence. Default STT segmentation
    // silence timeout is ~500ms which cuts long thoughts in two. 1200ms keeps
    // "Hello... this is a longer sentence with pauses" as ONE turn.
    cfg.setProperty('SpeechServiceConnection_EndSilenceTimeoutMs', '1200');
    // Get the raw text — the STT default masks profanity with ***, which then
    // gets read aloud by TTS as "star star star". We ask for raw here and
    // sanitise semantically server-side via /api/translate?soften=true.
    if (sdk.ProfanityOption?.Raw !== undefined) {
      cfg.setProfanity(sdk.ProfanityOption.Raw);
    }
    const autoDetect = sdk.AutoDetectSourceLanguageConfig.fromLanguages(sttCandidates);
    const micId = prefs.micDeviceId;
    const audio = micId
      ? sdk.AudioConfig.fromMicrophoneInput(micId)
      : sdk.AudioConfig.fromDefaultMicrophoneInput();
    const rec = sdk.SpeechRecognizer.FromConfig(cfg, autoDetect, audio);
    talkRecognizer = rec;
    talkActive = true;

    btn.querySelector('.mic-label').textContent = 'Stop talking';
    btn.classList.add('recording');
    status.textContent = 'Listening';
    hint.textContent = '🎙️ Listening… speak now.';

    rec.recognizing = (_s, e) => {
      if (e.result.text) hint.textContent = '🎧 …' + e.result.text.slice(-80);
    };
    rec.recognized = async (_s, e) => {
      if (e.result.reason === sdk.ResultReason.NoMatch) return;
      const original = e.result.text?.trim();
      if (!original) return;

      // Prefer the Translator server's auto-detect over the SDK's LID for
      // accuracy — Translator sees the full transcript, not just the first
      // half-second of audio. But use the SDK's guess as the initial hint
      // and update the row when the server responds.
      const detectedStt = sdk.AutoDetectSourceLanguageResult.fromResult(e.result)?.language;
      const sdkGuess = detectedStt
        ? (LANGUAGES.find(l => l.stt === detectedStt)?.code ?? detectedStt.split('-')[0])
        : myLang;

      hint.textContent = `🎙️ (${sdkGuess}?) ${original.slice(0, 60)}`;

      // Capture pitch RIGHT NOW while the buffer is still fresh from this
      // utterance — even one recognition-round of latency is enough to lose it.
      const pitchNow = recentPitchMedian(1500);
      const speakerGender = pitchNow == null ? null : (pitchNow < 155 ? 'male' : 'female');

      try {
        // Round 1: figure out which direction to translate. Ask Translator
        // to detect + translate to myLang. If detected = myLang, we translate
        // instead to the reply target (either the last foreign speaker or,
        // if none has spoken yet, the first foreign candidate in prefs).
        let firstResp = await api.translate(original, myLang, null, true);
        let detectedLang = (firstResp.detectedSourceLanguage || sdkGuess).split('-')[0];
        let isMine = detectedLang === myLang;
        let targetLang, translated;

        if (speakerGender) talkGenders.set(detectedLang, speakerGender);

        // If the last-foreign language is no longer in the user's chosen
        // candidate set (they removed it mid-session), drop the memory.
        const currentPrefs = loadMeetingPrefs();
        const currentCandidates = (currentPrefs.candidates && currentPrefs.candidates.length > 0) ? currentPrefs.candidates : [myLang];
        if (talkLastForeign && !currentCandidates.includes(talkLastForeign)) {
          talkLastForeign = null;
        }

        if (!isMine) {
          // Only record as "last foreign speaker" if that language is in the
          // active candidate list — otherwise it was just noise/misdetection.
          if (currentCandidates.includes(detectedLang)) {
            talkLastForeign = detectedLang;
          }
          targetLang = myLang;
          translated = firstResp.translatedText;
        } else {
          // Round 2: translate my own words back into the peer's language.
          targetLang = pickReplyTarget(myLang);
          if (targetLang === myLang) {
            // Only 1 candidate == myLang. No translation possible.
            translated = original;
          } else {
            const r2 = await api.translate(original, targetLang, detectedLang, true);
            translated = r2.translatedText;
          }
        }

        // Uncertain when SDK's LID and Translator disagree on the source
        // language — user can tap the language chip in the bubble to force-fix.
        const uncertain = (sdkGuess && detectedLang && sdkGuess !== detectedLang);

        talkRender({ original, translated, sourceLang: detectedLang, targetLang, isMine, uncertain });

        const ttsEnabled = loadMeetingPrefs().ttsEnabled !== false;
        const spokenText = translated && translated.trim() !== original.trim() ? translated : null;
        if (ttsEnabled && spokenText) {
          // Match the target-lang voice's gender to the *source* speaker: if a
          // deep-voiced Russian speaker just talked, play the English
          // translation in Andrew (male), not Emma. For your own replies,
          // match the last foreign speaker's gender.
          const genderSource = isMine ? talkLastForeign : detectedLang;
          const gender = genderSource ? talkGenders.get(genderSource) : null;
          speak(spokenText, targetLang, gender).catch(err => console.warn('[TTS] speak() rejected:', err));
        }
      } catch (err) {
        console.warn('[Talk] translate failed:', err);
        talkRender({ original, translated: original, sourceLang: sdkGuess, targetLang: sdkGuess, isMine: true });
      }
    };
    rec.canceled = (_s, e) => {
      const detail = e.errorDetails || (e.reason === sdk.CancellationReason.EndOfStream ? 'end of stream' : String(e.reason));
      if (e.reason === sdk.CancellationReason.Error) {
        toast('Speech canceled: ' + detail, 'error');
      }
      hint.textContent = 'Recognition stopped: ' + detail;
      stopTalk();
    };
    rec.sessionStopped = () => { hint.textContent = 'Session stopped.'; stopTalk(); };

    rec.startContinuousRecognitionAsync(
      () => { startPitchTracker(); },
      (err) => {
        toast('Failed to start speech: ' + err, 'error');
        hint.textContent = 'Failed to start: ' + err;
        talkActive = false;
      }
    );
}

async function stopTalk({ interrupted = false } = {}) {
  ++talkSessionId;
  talkStarting = false;
  talkActive = false;
  stopPitchTracker();
  const rec = talkRecognizer;
  talkRecognizer = null;
  const btn = document.getElementById('talk-mic');
  btn.querySelector('.mic-label').textContent = 'Start talking';
  btn.classList.remove('recording');
  document.getElementById('talk-status').textContent = interrupted ? 'Paused' : 'Idle';
  document.getElementById('talk-hint').textContent = interrupted
    ? 'Translation paused while the app was in the background.'
    : 'Press Start talking and grant microphone access.';
  if (rec) {
    try { rec.stopContinuousRecognitionAsync(() => rec.close(), () => rec.close()); } catch {}
  }
  // Sprint 2 cleanup — WebSocket + Worklet + AudioContext.
  const ws = talkWs;
  talkWs = null;
  if (ws) {
    ws.onmessage = null;
    ws.onerror = null;
    ws.onclose = null;
    if (ws.readyState === WebSocket.CONNECTING) {
      ws.onopen = () => {
        try { ws.close(1000, 'stop'); } catch {}
      };
    } else {
      ws.onopen = null;
      try { if (ws.readyState === WebSocket.OPEN) ws.close(1000, 'stop'); } catch {}
    }
  }
  if (talkWorkletNode) {
    try { talkWorkletNode.port.onmessage = null; talkWorkletNode.disconnect(); } catch {}
    talkWorkletNode = null;
  }
  if (talkAudioCtx) {
    try { await talkAudioCtx.close(); } catch {}
    talkAudioCtx = null;
  }
  if (talkAudioStream) {
    try { talkAudioStream.getTracks().forEach(t => t.stop()); } catch {}
    talkAudioStream = null;
  }
  for (const q of s2sQueues.values()) {
    if (q.flushTimer) clearTimeout(q.flushTimer);
  }
  if (s2sPlaybackCtx) {
    try { await s2sPlaybackCtx.close(); } catch {}
    s2sPlaybackCtx = null;
    s2sPlaybackTime = 0;
  }
  s2sQueues.clear();
  s2sCurrentBubble = null;
  s2sOriginalText = '';
  s2sTranslatedText = '';
  s2sTranslationLanguage = '';
  talkSpeakerColors.clear();
  await releaseTalkWakeLock();
}

document.addEventListener('visibilitychange', () => {
  if (document.visibilityState !== 'visible') {
    if (trainingWs) void stopTraining();
    if (talkActive || talkStarting) {
      talkRestartOnVisible = true;
      talkVisibilityStopPromise = stopTalk({ interrupted: true });
    }
    return;
  }

  if (!talkRestartOnVisible) {
    if (talkActive || talkStarting) {
      const sessionId = talkSessionId;
      void requestTalkWakeLock(sessionId);
      void resumeTalkAudioContexts(sessionId);
    }
    return;
  }

  talkRestartOnVisible = false;
  if (talkRecoveryInProgress) return;
  talkRecoveryInProgress = true;
  void (async () => {
    try {
      await talkVisibilityStopPromise;
      const hint = document.getElementById('talk-hint');
      const status = document.getElementById('talk-status');
      if (hint) hint.textContent = 'Reconnecting translation after background pause…';
      if (status) status.textContent = 'Reconnecting';
      await startTalk({ recovering: true });
    } finally {
      talkRecoveryInProgress = false;
    }
  })();
});

document.getElementById('talk-mic').addEventListener('click', () => {
  if (talkActive) stopTalk(); else startTalk();
});
document.getElementById('talk-mic-device').addEventListener('click', () => openMicDeviceDialog());
document.getElementById('talk-mode').addEventListener('click', () => openModeDialog());
document.getElementById('talk-tts').addEventListener('click', () => {
  unlockTtsAudio();
  const prefs = loadMeetingPrefs();
  saveMeetingPrefs({ ttsEnabled: prefs.ttsEnabled === false });
  syncTalkControls();
});
document.getElementById('talk-clear').addEventListener('click', () => {
  document.getElementById('talk-timeline').innerHTML = '<div class="muted center">Nothing yet.</div>';
  talkLastForeign = null;
  talkTurns.clear();
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
      getClientAccessUrl: async () => privatePubSubUrl(url),
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
    if (e instanceof ApiError && e.code === 'demo_persona_required') {
      document.getElementById('access-denied-message').textContent = 'Specify an existing demo user with ?dev=<name>.';
      show('access-denied');
      return;
    }
    if (e instanceof ApiError && e.code === 'user_not_provisioned') {
      document.getElementById('access-denied-message').textContent = 'This account has not been invited. Sign out and use an invited account, or contact the application operator.';
      show('access-denied');
      return;
    }
    document.getElementById('load-error-message').textContent = e instanceof ApiError
      ? `The profile service returned HTTP ${e.status}. Please try again.`
      : 'The profile service could not be reached or returned an invalid response. Please try again.';
    show('load-error'); return;
  }
  updateMeUi();
  document.querySelector('[data-tab="training"]').hidden = !state.me.canUseTraining;
  document.getElementById('training-mode').hidden = !state.me.canUseGuidedTraining;
  document.getElementById('tabs').hidden = false;
  document.getElementById('me').hidden = false;
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
      toast(`Translating into ${langName(sel.value)}`, 'success');
      if (talkActive) {
        await stopTalk();
        setTimeout(() => startTalk(), 250);
      }
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
  const detail = e?.stack ? String(e.stack).split('\n').slice(0, 3).join(' | ') : '';
  toast(msg, 'error', detail);
}

// -------- Utils --------
function escapeHtml(s) { return String(s ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch])); }
function langName(code) { return langMap.get(code)?.name ?? code; }
function formatTime(iso) { return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }
function formatDate(iso) { const d = new Date(iso); return d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ' ' + formatTime(iso); }
