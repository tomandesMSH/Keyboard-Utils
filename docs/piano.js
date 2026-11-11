// The keyboard strips on the page can be played: click or touch a key (several fingers, or slide along them), or
// use the computer keyboard (A W S E D F T G Z H U J K - the row of a piano). The sound is made right here with
// Web Audio: a few sine partials that die away like a struck string - no sound files.
(function () {
  const strips = document.querySelectorAll('.keys');
  if (!strips.length) return;

  let audio = null;
  const sounding = new Map(); // id (pointer or key) -> { stop }

  function context() {
    if (!audio) {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return null;
      audio = new Ctx();
    }
    if (audio.state === 'suspended') audio.resume();
    return audio;
  }

  function play(midi) {
    const ctx = context();
    if (!ctx) return () => {};
    const now = ctx.currentTime;
    const hz = 440 * Math.pow(2, (midi - 69) / 12);
    const out = ctx.createGain();
    // Higher notes fade faster, like a real piano.
    const decay = Math.max(0.6, 3 - (midi - 40) * 0.035);
    out.gain.setValueAtTime(0, now);
    out.gain.linearRampToValueAtTime(0.22, now + 0.005);
    out.gain.exponentialRampToValueAtTime(0.001, now + decay);
    out.connect(ctx.destination);
    const oscillators = [[1, 1], [2, 0.42], [3, 0.2], [4, 0.1]].map(([harmonic, level]) => {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.frequency.value = hz * harmonic;
      gain.gain.value = level;
      osc.connect(gain).connect(out);
      osc.start(now);
      osc.stop(now + decay + 0.05);
      return osc;
    });
    // Letting go damps the string quickly (but not with a click).
    return function stop() {
      const t = ctx.currentTime;
      out.gain.cancelScheduledValues(t);
      out.gain.setValueAtTime(Math.max(out.gain.value, 0.0001), t);
      out.gain.exponentialRampToValueAtTime(0.001, t + 0.25);
      oscillators.forEach((osc) => { try { osc.stop(t + 0.3); } catch (e) { /* already stopped */ } });
    };
  }

  // Where the black keys are in an octave, in white-key widths (the same numbers as style.css).
  const blackKeys = [[0.7, 1.3, 1], [1.7, 2.3, 3], [3.7, 4.3, 6], [4.7, 5.3, 8], [5.7, 6.3, 10]];
  const whiteSemitones = [0, 2, 4, 5, 7, 9, 11];

  function keyAt(strip, clientX, clientY) {
    const rect = strip.getBoundingClientRect();
    const w = parseFloat(getComputedStyle(strip).getPropertyValue('--w')) || 40;
    const x = clientX - rect.left, y = clientY - rect.top;
    if (x < 0 || y < 0 || x > rect.width || y > rect.height) return null;
    const lowest = Number(strip.dataset.lowest || 48); // the leftmost key: C3 (C4 on the small strips)
    const octave = Math.floor(x / (7 * w));
    const inOctave = x / w - octave * 7;
    if (y < rect.height * 0.62) {
      for (const [from, to, semitone] of blackKeys)
        if (inOctave >= from && inOctave < to)
          return { midi: lowest + octave * 12 + semitone, left: rect.left + (octave * 7 + from) * w, width: (to - from) * w, black: true };
    }
    const white = Math.floor(inOctave);
    return { midi: lowest + octave * 12 + whiteSemitones[white], left: rect.left + (octave * 7 + white) * w, width: w - 2, black: false };
  }

  // A pressed key lights up: a small overlay over it.
  function light(strip, key) {
    const mark = document.createElement('span');
    mark.className = 'key-pressed' + (key.black ? ' black' : '');
    mark.style.left = (key.left - strip.getBoundingClientRect().left) + 'px';
    mark.style.width = key.width + 'px';
    strip.appendChild(mark);
    return () => mark.remove();
  }

  function press(id, strip, key) {
    release(id);
    const stop = play(key.midi);
    const unlight = light(strip, key);
    sounding.set(id, { midi: key.midi, stop, unlight });
  }

  function release(id) {
    const note = sounding.get(id);
    if (!note) return;
    note.stop();
    note.unlight();
    sounding.delete(id);
  }

  strips.forEach((strip, index) => {
    if (strip.dataset.lowest == null) strip.dataset.lowest = strip.classList.contains('keys-small') ? '60' : '48';
    strip.classList.add('playable');
    strip.title = 'Zahraj si! (myší, prstem nebo klávesami A W S E D F T G Z H U J K)';
    strip.addEventListener('pointerdown', (e) => {
      const key = keyAt(strip, e.clientX, e.clientY);
      if (!key) return;
      e.preventDefault();
      strip.setPointerCapture(e.pointerId);
      press('p' + e.pointerId, strip, key);
    });
    strip.addEventListener('pointermove', (e) => {
      const note = sounding.get('p' + e.pointerId);
      if (!note) return;
      const key = keyAt(strip, e.clientX, e.clientY);
      if (key && key.midi !== note.midi) press('p' + e.pointerId, strip, key); // sliding along: a glissando
    });
    ['pointerup', 'pointercancel', 'lostpointercapture'].forEach((type) =>
      strip.addEventListener(type, (e) => release('p' + e.pointerId)));
  });

  // The computer keyboard plays the first strip from middle C: the home row is the white keys, the row above the black.
  const keyboard = { a: 0, w: 1, s: 2, e: 3, d: 4, f: 5, t: 6, g: 7, z: 8, y: 8, h: 9, u: 10, j: 11, k: 12 };
  const first = strips[0];
  document.addEventListener('keydown', (e) => {
    if (e.repeat || e.ctrlKey || e.metaKey || e.altKey || /input|textarea|select/i.test(e.target.tagName)) return;
    const semitone = keyboard[e.key.toLowerCase()];
    if (semitone == null) return;
    const midi = 60 + semitone;
    const lowest = Number(first.dataset.lowest);
    const w = parseFloat(getComputedStyle(first).getPropertyValue('--w')) || 40;
    const rect = first.getBoundingClientRect();
    const octave = Math.floor((midi - lowest) / 12), pc = (midi - lowest) % 12;
    const black = blackKeys.find((b) => b[2] === pc);
    const key = black
      ? { midi, left: rect.left + (octave * 7 + black[0]) * w, width: (black[1] - black[0]) * w, black: true }
      : { midi, left: rect.left + (octave * 7 + whiteSemitones.indexOf(pc)) * w, width: w - 2, black: false };
    press('k' + e.code, first, key);
  });
  document.addEventListener('keyup', (e) => release('k' + e.code));
  window.addEventListener('blur', () => [...sounding.keys()].forEach(release));
})();
