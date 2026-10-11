# MP3 fixture

`tone.mp3` is an original, synthesized 440 Hz stereo sine tone (0.6 seconds,
44.1 kHz, MP3 128 kbps). It contains no third-party music.

Generated with FFmpeg:

```sh
ffmpeg -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=0.6" \
  -ac 2 -c:a libmp3lame -b:a 128k -map_metadata -1 tone.mp3
```

The Windows integration test exercises the actual Windows ACM MP3 decoder,
not a mock decoder. Physical WASAPI/ASIO devices are checked separately.
