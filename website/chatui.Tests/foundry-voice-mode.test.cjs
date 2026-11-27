const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '..', 'chatui', 'wwwroot', 'app.js'), 'utf8');
const preferences = source.slice(
  source.indexOf('const meetingPrefsKey'),
  source.indexOf('// Last language actually heard'));

function loadPreferences(value) {
  const storage = new Map();
  if (value !== undefined) storage.set('meetingPrefs:user-1', JSON.stringify(value));
  const context = vm.createContext({
    state: { me: { id: 'user-1' } },
    localStorage: {
      getItem: key => storage.get(key) ?? null,
      setItem: (key, next) => storage.set(key, next),
    },
  });
  vm.runInContext(preferences + '\nglobalThis.result = loadMeetingPrefs();', context);
  return context.result;
}

test('managed Foundry Voice remains a persisted recognition mode', () => {
  const preferences = loadPreferences({ recognitionMode: 'foundry-voice', ttsEnabled: false });

  assert.equal(preferences.recognitionMode, 'foundry-voice');
  assert.equal(preferences.ttsEnabled, false);
});

test('unsupported legacy recognition modes fall back to S2S', () => {
  const preferences = loadPreferences({ recognitionMode: 'gpt-diarize' });

  assert.equal(preferences.recognitionMode, 's2s-realtime');
});

test('managed Voice uses its dedicated endpoint and 24 kHz capture', () => {
  assert.match(source, /startTalkServer\(btn, hint, status, '\/api\/speech\/voice-agent'/);
  assert.match(source, /endpointPath\.includes\('voice-agent'\)[\s\S]*?\? 24000/);
});
