# Frame Probe (diagnostic, 0.1.0)

Frame Probe records low-overhead, 30-second summaries of frame time, CPU main and render thread time, GPU time when Unity reports it, and draw calls. It does not change rendering or gameplay. Output is `UserData/FrameProbe/session-*.csv`; the MelonLoader log records the loaded mod names and versions.

This is a measurement aid, not a per-mod profiler. Run the **same scene and settings** with (1) FrameProbe alone, (2) FrameProbe plus the full mod set, then isolate any measured difference by groups. Keep FrameProbe installed in every comparison. At 120 Hz the frame budget is 8.33 ms; at 90 Hz it is 11.11 ms. Warm up for two minutes and record at least three minutes per run. Use the VR runtime overlay to check reprojection and dropped frames. Change DLLs only while the game is closed. The full protocol is in [IDEAS.md](../IDEAS.md#vr-frame-time-baseline-and-mod-isolation-sep-29-2026).

The `frame_*` values come from Unity's `Time.unscaledDeltaTime` and include frame pacing and waiting. Main/render/GPU values come from `FrameTimingManager`; Unity returns them with a delay and may provide no GPU timing on some setups. Empty fields mean unavailable, not zero. Draw counts use Unity's runtime `ProfilerRecorder` counter and may also be unavailable. A high counter or a single slow window does not identify a cause. The probe itself adds some overhead, so use identical probe settings in each run. Gameplay performance is not yet verified.

## Compact report and optional Jev filtering

`summarize.py` reads the CSV and optionally selects useful lines from a MelonLoader log. It computes the 120 Hz budget test with ordinary code. `--jev` asks TypeSafe Jev whether **ambiguous warning/error lines** might matter to a performance comparison; obvious performance lines are kept by simple text rules. The output includes original log line numbers, Jev probabilities and API token usage. It does not send the CSV, entire log, or API key to the model. It sends at most the latest 40 selected warning/error lines, in batches of 10; the raw logs remain local. A probability of at least 0.5 keeps a line, pending validation with real logs. Jev's score is a filtering aid, not evidence of causation. If an API call fails, remaining candidate lines are kept as unclassified.

```powershell
python FrameProbe/summarize.py "E:/SteamLibrary/steamapps/common/Gunman Contracts - Stand Alone/UserData/FrameProbe/session-....csv" --hz 120
python FrameProbe/summarize.py "path/to/full-stack.csv" --baseline "path/to/probe-only.csv" --log "path/to/MelonLoader.log" --jev --hz 120 --out report.json
```

The second command requires `TYPESAFE_API_KEY` in the process environment. `setx` affects new terminals, so open a new terminal if needed. The game DLL never uses the key or connects to TypeSafe; only the optional post-processing command does. The script uses Python's standard library and [TypeSafe's System One API](https://docs.typesafe.ai/api).

## Build

`dotnet build FrameProbe/FrameProbe.csproj -c Release -p:GameDir="<game install>"`. This requires MelonLoader 0.7.x interop assemblies. Deploy `FrameProbe/bin/Release/FrameProbe.dll` to the game's `Mods` folder after backing up an existing copy outside `Mods`, then restart the game. Do not include this diagnostic DLL in a public release without in-game validation.
