// Downsamples the mic's native sample rate (typically 44.1 or 48 kHz) to a
// target sample rate in mono int16 PCM (the format Azure Speech and Azure
// OpenAI Realtime expect on the push stream) and posts frames to the main
// thread as ArrayBuffers.
//
// Loaded via AudioWorklet.addModule('/pcm-processor.js') from app.js.
// The main thread sets `processor.port.postMessage({ targetRate: 24000 })`
// after instantiation to override the default 16 kHz rate.
class PcmProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this._targetRate = 16000;
    this._ratio = sampleRate / this._targetRate;
    this._buf = [];
    this.port.onmessage = (e) => {
      if (e.data?.type === 'flush') {
        this.emitFrame();
        this.port.postMessage({ type: 'flushed' });
        return;
      }
      const rate = e.data?.targetRate;
      if (typeof rate === 'number' && rate > 0) {
        this._targetRate = rate;
        this._ratio = sampleRate / rate;
      }
    };
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || input.length === 0) return true;
    const ch = input[0]; // mono — expected by both back-ends
    if (!ch || ch.length === 0) return true;

    for (let i = 0; i < ch.length; i++) this._buf.push(ch[i]);

    // Only emit when we have enough samples for a nicely-sized frame.
    const minFrame = Math.floor(this._targetRate * 0.02); // ~20 ms
    const target = Math.floor(this._buf.length / this._ratio);
    if (target < minFrame) return true;

    this.emitFrame();
    return true;
  }

  emitFrame() {
    const target = Math.floor(this._buf.length / this._ratio);
    if (target === 0) return;
    const out = new Int16Array(target);
    for (let i = 0; i < target; i++) {
      const srcIdx = i * this._ratio;
      const srcFloor = Math.floor(srcIdx);
      const frac = srcIdx - srcFloor;
      const a = this._buf[srcFloor] ?? 0;
      const b = this._buf[srcFloor + 1] ?? a;
      const sample = a + (b - a) * frac;
      // Clip and convert float [-1, 1] to int16 [-32768, 32767]
      const s = Math.max(-1, Math.min(1, sample));
      out[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
    }
    // Drop the consumed source samples.
    this._buf.splice(0, Math.floor(target * this._ratio));
    this.port.postMessage(out.buffer, [out.buffer]);
  }
}

registerProcessor('pcm-processor', PcmProcessor);
