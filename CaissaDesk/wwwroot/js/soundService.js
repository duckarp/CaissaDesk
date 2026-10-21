// Caissalytics Audio Service
// Supports HTML5 Audio elements with pre-rendered WAV sound assets,
// alongside zero-latency Web Audio API synthesis fallback and autoplay unlocking.

window.chessSound = (function () {
    const soundFiles = {
        move: 'sounds/move.wav',
        capture: 'sounds/capture.wav',
        check: 'sounds/check.wav',
        victory: 'sounds/victory.wav',
        lowTime: 'sounds/lowtime.wav'
    };

    let audioCtx = null;

    function getAudioContext() {
        if (!audioCtx) {
            try {
                const AudioContextClass = window.AudioContext || window.webkitAudioContext;
                if (AudioContextClass) {
                    audioCtx = new AudioContextClass();
                }
            } catch (e) {
                console.warn("[chessSound] Web Audio Context initialization error:", e);
            }
        }
        if (audioCtx && audioCtx.state === 'suspended') {
            audioCtx.resume().catch(() => {});
        }
        return audioCtx;
    }

    // Unlock audio context on first user interaction
    function unlockAudio() {
        try {
            const ctx = getAudioContext();
            if (ctx && ctx.state === 'suspended') {
                ctx.resume().catch(() => {});
            }
        } catch (e) {}
    }

    if (typeof window !== 'undefined') {
        ['pointerdown', 'keydown', 'click'].forEach(evt => {
            window.addEventListener(evt, unlockAudio, { capture: true, passive: true });
        });
    }

    function createMasterGain(ctx, volume) {
        const gain = ctx.createGain();
        const clampedVol = Math.max(0, Math.min(1, typeof volume === 'number' ? volume : 0.8));
        gain.gain.setValueAtTime(clampedVol, ctx.currentTime);
        gain.connect(ctx.destination);
        return gain;
    }

    function playWebAudio(name, volume) {
        try {
            const ctx = getAudioContext();
            if (!ctx || ctx.state !== 'running') return;
            const master = createMasterGain(ctx, volume);
            const now = ctx.currentTime;

            if (name === 'move') {
                const osc = ctx.createOscillator();
                const oscGain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(180, now);
                osc.frequency.exponentialRampToValueAtTime(65, now + 0.08);
                oscGain.gain.setValueAtTime(0.7, now);
                oscGain.gain.exponentialRampToValueAtTime(0.001, now + 0.09);
                osc.connect(oscGain);
                oscGain.connect(master);
                osc.start(now);
                osc.stop(now + 0.1);
            } else if (name === 'capture') {
                const osc = ctx.createOscillator();
                const oscGain = ctx.createGain();
                osc.type = 'triangle';
                osc.frequency.setValueAtTime(260, now);
                osc.frequency.exponentialRampToValueAtTime(50, now + 0.12);
                oscGain.gain.setValueAtTime(0.9, now);
                oscGain.gain.exponentialRampToValueAtTime(0.001, now + 0.13);
                osc.connect(oscGain);
                oscGain.connect(master);
                osc.start(now);
                osc.stop(now + 0.14);
            } else if (name === 'check') {
                [739.99, 1108.73].forEach((freq, idx) => {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    osc.type = 'sine';
                    osc.frequency.setValueAtTime(freq, now);
                    gain.gain.setValueAtTime(0.35, now);
                    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.28 + idx * 0.05);
                    osc.connect(gain);
                    gain.connect(master);
                    osc.start(now);
                    osc.stop(now + 0.35);
                });
            } else if (name === 'victory') {
                [523.25, 659.25, 783.99, 1046.50].forEach((freq, idx) => {
                    const startTime = now + (idx * 0.08);
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();
                    osc.type = 'triangle';
                    osc.frequency.setValueAtTime(freq, startTime);
                    gain.gain.setValueAtTime(0.001, startTime);
                    gain.gain.linearRampToValueAtTime(0.35, startTime + 0.02);
                    gain.gain.exponentialRampToValueAtTime(0.001, startTime + 0.4);
                    osc.connect(gain);
                    gain.connect(master);
                    osc.start(startTime);
                    osc.stop(startTime + 0.45);
                });
            } else if (name === 'lowTime') {
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(1400, now);
                osc.frequency.exponentialRampToValueAtTime(400, now + 0.03);
                gain.gain.setValueAtTime(0.3, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.035);
                osc.connect(gain);
                gain.connect(master);
                osc.start(now);
                osc.stop(now + 0.04);
            }
        } catch (e) {
            console.warn("[chessSound] Web Audio synth error:", e);
        }
    }

    function playSound(name, volume) {
        const clampedVol = Math.max(0, Math.min(1, typeof volume === 'number' ? volume : 0.8));
        const url = soundFiles[name];
        if (url) {
            try {
                const audio = new Audio(url);
                audio.volume = clampedVol;
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.catch(() => {
                        // Fallback to Web Audio if audio element play is rejected
                        playWebAudio(name, clampedVol);
                    });
                }
                return;
            } catch (e) {
                playWebAudio(name, clampedVol);
                return;
            }
        }
        playWebAudio(name, clampedVol);
    }

    return {
        playMove: function (volume) { playSound('move', volume); },
        playCapture: function (volume) { playSound('capture', volume); },
        playCheck: function (volume) { playSound('check', volume); },
        playVictory: function (volume) { playSound('victory', volume); },
        playLowTime: function (volume) { playSound('lowTime', volume); }
    };
})();
