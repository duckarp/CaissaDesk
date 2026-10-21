# Chess Sound Assets

The sound effects in this directory are sourced from the open-source [Lichess](https://github.com/lichess-org/lila) project (`public/sound/`):

- **move.wav**: Lichess standard piece placement sound (crisp wood hit, Sounddogs/Lichess, AGPLv3).
- **capture.wav**: Lichess standard piece capture sound (deeper wooden strike, Sounddogs/Lichess, AGPLv3).
- **check.wav**: Lichess SFX check cue (Enigmahack / Lichess, AGPLv3+).
- **victory.wav**: Lichess SFX victory fanfare (Enigmahack / Lichess, AGPLv3+).
- **lowtime.wav**: Lichess standard low time warning (Lichess, AGPLv3).

All assets are converted to uncompressed 44.1 kHz, 16-bit PCM WAV format for zero-latency, cross-platform native playback across Linux (pw-play, paplay, aplay), Windows (PlaySoundWin32), macOS (afplay), and Web Audio API / HTML5 Audio.
