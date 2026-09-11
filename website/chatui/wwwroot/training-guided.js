export class GuidedTraining {
  constructor({ createUrl, microphoneId, synthesize }) {
    this.createUrl = createUrl;
    this.microphoneId = microphoneId;
    this.synthesize = synthesize;
    this.root = document.getElementById('training-guided');
    this.phase = 'idle';
    this.epoch = 0;
    this.ids = {};
    this.audio = this.root.querySelector('audio');
    this.element('start').addEventListener('click', () => this.perform(() => this.start()));
    this.element('finish').addEventListener('click', () => this.perform(() => this.finish()));
    this.element('cancel').addEventListener('click', () => this.stop('Cancelled'));
    this.element('coach').addEventListener('click', () => {
      this.send('attempt.coach', { text: this.element('transcript').value });
      this.phase = 'coaching';
      this.render();
    });
    this.element('retry').addEventListener('click', () => this.newAttempt(true));
    this.element('new').addEventListener('click', () => this.newAttempt(false));
    for (const kind of ['hint', 'answer', 'listen']) {
      this.element(kind).addEventListener('click', () => this.send('attempt.aid', { kind }));
    }
    this.render();
  }

  element(name) { return document.getElementById(`guided-${name}`); }
  get active() { return this.socket != null || this.stream != null; }

  async perform(action) {
    try { await action(); }
    catch { await this.stop('Practice interrupted. Check your microphone or connection, then start again.'); }
  }

  async start() {
    if (this.phase === 'prompt') return this.record();
    if (!['idle', 'interrupted'].includes(this.phase)) return;
    this.phase = 'connecting';
    this.element('notice').textContent = '';
    this.render();
    this.startAfterReady = true;
    const epoch = ++this.epoch;
    const socket = this.socket = new WebSocket(this.createUrl());
    this.connectTimer = setTimeout(() => {
      if (this.epoch === epoch && this.phase === 'connecting') void this.stop('Connection timed out. Start again to reconnect.');
    }, 20000);
    socket.onmessage = event => {
      if (this.epoch !== epoch || this.socket !== socket) return;
      void this.perform(async () => {
        if (typeof event.data !== 'string' || event.data.length > 32768) throw new Error('Invalid message');
        await this.message(JSON.parse(event.data));
      });
    };
    socket.onclose = () => {
      if (this.epoch === epoch) void this.stop('Connection interrupted. Start a new attempt; the previous attempt was not resumed.');
    };
    socket.onerror = () => {
      if (this.epoch === epoch) void this.stop('Connection failed. Start again to reconnect.');
    };
  }

  send(type, data = {}) {
    if (this.socket?.readyState !== WebSocket.OPEN) return;
    this.socket.send(JSON.stringify({ type, ...this.ids, ...data }));
  }

  newAttempt(retry) {
    if (!['ready', 'feedback'].includes(this.phase)) return;
    this.stopPlayback();
    this.retry = retry;
    this.phase = 'starting';
    this.element('transcript').value = '';
    this.element('feedback').hidden = true;
    this.element('aid').textContent = '';
    this.element('notice').textContent = '';
    this.audio.hidden = true;
    this.send('attempt.start', { retry, register: this.element('register').value });
    this.render();
  }

  async message(message) {
    if (message.type === 'session_ready') {
      clearTimeout(this.connectTimer);
      this.ids = { sessionId: message.sessionId };
      this.phase = 'ready';
      this.newAttempt(false);
      return;
    }
    if (message.sessionId !== this.ids.sessionId) return;
    if (message.type === 'attempt_ready' && this.phase === 'starting') {
      this.ids = { sessionId: message.sessionId, exerciseId: message.exerciseId, attemptId: message.attemptId };
      this.phase = 'prompt';
      this.render();
      if (this.startAfterReady) {
        this.startAfterReady = false;
        await this.record();
      }
      return;
    }
    if (message.attemptId !== this.ids.attemptId) return;
    switch (message.type) {
      case 'recording':
        this.phase = 'recording';
        this.audioBytes = 0;
        this.recordTimer = setTimeout(() => this.perform(() => this.finish()), 119000);
        break;
      case 'processing':
      case 'coaching':
        this.phase = message.type;
        break;
      case 'original_done':
        this.element('transcript').value = message.data.text;
        this.phase = 'review';
        break;
      case 'feedback': {
        const { result, helped, asrEdited, independentEnglishRecall, retry } = message.data;
        const labels = { translation: 'Translation', correction: 'Correction', conversationalAlternative: 'Conversational alternative', alreadyNatural: 'Already natural' };
        this.element('feedback-type').textContent = labels[result.feedbackType] || 'Feedback';
        this.element('natural').textContent = result.naturalVersion;
        this.element('tip').textContent = result.oneActionableTip;
        const assessment = { acceptable: 'Acceptable response', needsWork: 'Needs work', notAssessable: 'Not assessable' };
        const meaning = { preserved: 'Meaning preserved', changed: 'Meaning changed', unknown: 'Meaning unclear' };
        const notes = [assessment[result.assessment], meaning[result.meaningPreserved]];
        if (result.needsClarification) notes.push('Clarification needed');
        if (result.language !== 'en') notes.push('Not independent English recall');
        if (asrEdited) notes.push('Transcript edited; not evidence of improved spoken recall');
        if (helped) notes.push('Assisted attempt');
        if (retry && independentEnglishRecall) notes.push('Independent English retry');
        this.element('assessment').textContent = notes.filter(Boolean).join('. ');
        this.phase = 'feedback';
        this.element('feedback').hidden = false;
        break;
      }
      case 'aid':
        this.element('aid').textContent = message.data.text;
        this.element('notice').textContent = 'Assisted attempt';
        if (message.data.kind === 'listen') await this.listen(message.data.text);
        break;
      case 'cancelled':
        await this.stop('Cancelled');
        return;
      case 'error':
        if (message.data.code === 'interrupted') {
          await this.stop(message.data.message);
          return;
        }
        this.phase = message.state;
        this.element('notice').textContent = message.data.message;
        break;
    }
    this.render();
  }

  async record() {
    if (this.phase !== 'prompt') return;
    this.phase = 'starting';
    this.render();
    this.stopPlayback();
    const epoch = this.epoch;
    const deviceId = this.microphoneId();
    const stream = await navigator.mediaDevices.getUserMedia({ audio: {
      channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true,
      ...(deviceId ? { deviceId: { exact: deviceId } } : {}),
    } });
    if (epoch !== this.epoch) { stream.getTracks().forEach(track => track.stop()); return; }
    this.stream = stream;
    const AudioContextClass = window.AudioContext || window.webkitAudioContext;
    const context = this.captureContext = new AudioContextClass();
    await context.resume();
    await context.audioWorklet.addModule('/pcm-processor.js?v=3');
    if (epoch !== this.epoch) return;
    const source = context.createMediaStreamSource(stream);
    const worklet = this.worklet = new AudioWorkletNode(context, 'pcm-processor');
    worklet.port.postMessage({ targetRate: 24000 });
    worklet.port.onmessage = event => {
      if (epoch !== this.epoch) return;
      if (event.data?.type === 'flushed') { this.flushResolve?.(); return; }
      if (this.phase !== 'recording' || !(event.data instanceof ArrayBuffer)) return;
      if (this.socket?.readyState !== WebSocket.OPEN || this.socket.bufferedAmount > 192000) {
        void this.stop('Connection is too slow. Start a new attempt.');
        return;
      }
      this.audioBytes += event.data.byteLength;
      if (this.audioBytes > 24000 * 2 * 120) { void this.stop('Attempt exceeded two minutes. Start a shorter attempt.'); return; }
      this.socket.send(event.data);
    };
    source.connect(worklet);
    this.send('attempt.record');
  }

  async finish() {
    if (this.phase !== 'recording' || this.finishing) return;
    if (this.audioBytes < 4800) { this.element('notice').textContent = 'Record a short phrase before finishing.'; return; }
    this.finishing = true;
    this.render();
    const epoch = this.epoch;
    clearTimeout(this.recordTimer);
    await this.captureContext.suspend();
    await new Promise((resolve, reject) => {
      this.flushTimer = setTimeout(() => reject(new Error('Audio flush timed out')), 2000);
      this.flushResolve = () => { clearTimeout(this.flushTimer); resolve(); };
      this.worklet.port.postMessage({ type: 'flush' });
    });
    if (epoch !== this.epoch) return;
    this.phase = 'processing';
    await this.stopCapture();
    if (epoch !== this.epoch) return;
    this.send('attempt.finish');
    this.finishing = false;
    this.render();
  }

  async listen(text) {
    this.stopPlayback();
    const epoch = this.epoch;
    const playbackVersion = this.playbackVersion;
    this.audioAbort = new AbortController();
    try {
      const response = await this.synthesize(text, this.audioAbort.signal);
      if (!response.ok) throw new Error('Speech unavailable');
      const blob = await response.blob();
      if (epoch !== this.epoch || playbackVersion !== this.playbackVersion) return;
      this.audioUrl = URL.createObjectURL(blob);
      this.audio.src = this.audioUrl;
      this.audio.hidden = false;
      await this.audio.play();
    } catch {
      if (epoch === this.epoch && playbackVersion === this.playbackVersion) this.element('notice').textContent = 'Audio unavailable. You can try Listen again.';
    }
  }

  stopPlayback() {
    this.playbackVersion = (this.playbackVersion || 0) + 1;
    this.audioAbort?.abort();
    this.audio.pause();
    this.audio.removeAttribute('src');
    this.audio.load();
    if (this.audioUrl) URL.revokeObjectURL(this.audioUrl);
    this.audioUrl = null;
    this.audio.hidden = true;
  }

  async stopCapture() {
    clearTimeout(this.recordTimer);
    if (this.worklet) { this.worklet.port.onmessage = null; this.worklet.disconnect(); this.worklet = null; }
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;
    const context = this.captureContext;
    this.captureContext = null;
    try { await context?.close(); } catch {}
  }

  async stop(message = '') {
    this.epoch++;
    clearTimeout(this.connectTimer);
    clearTimeout(this.flushTimer);
    this.flushResolve?.();
    this.flushResolve = null;
    this.send('attempt.cancel');
    const socket = this.socket;
    this.socket = null;
    if (socket) { socket.onclose = socket.onmessage = socket.onerror = null; socket.close(); }
    this.stopPlayback();
    await this.stopCapture();
    this.phase = message ? 'interrupted' : 'idle';
    this.ids = {};
    this.finishing = false;
    this.startAfterReady = false;
    this.element('transcript').value = '';
    this.element('feedback').hidden = true;
    for (const name of ['natural', 'tip', 'assessment', 'aid']) this.element(name).textContent = '';
    this.element('notice').textContent = message;
    this.render();
  }

  render() {
    const phase = this.phase;
    this.element('status').textContent = ({ idle: 'Ready', connecting: 'Connecting', ready: 'Ready', starting: 'Starting', prompt: this.retry ? 'Retry without the example' : 'Ready', recording: 'Recording', processing: 'Transcribing', review: 'Review transcript', coaching: 'Coaching', feedback: 'Feedback', interrupted: 'Interrupted' })[phase] || phase;
    this.element('start').hidden = !['idle', 'prompt', 'interrupted'].includes(phase);
    this.element('finish').hidden = phase !== 'recording';
    this.element('finish').disabled = !!this.finishing;
    this.element('cancel').hidden = ['idle', 'interrupted'].includes(phase);
    this.element('review').hidden = phase !== 'review';
    this.element('retry').hidden = phase !== 'feedback';
    this.element('new').hidden = phase !== 'feedback';
    this.element('register').disabled = !['idle', 'interrupted', 'feedback'].includes(phase);
    for (const kind of ['hint', 'answer', 'listen']) {
      this.element(kind).hidden = !(phase === 'feedback' || (this.retry && ['prompt', 'recording'].includes(phase)));
      this.element(kind).disabled = kind === 'listen' && phase === 'recording';
    }
  }
}