# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A space shooter in the style of Nichibutsu's 1980 arcade game Moon Cresta, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the rocket and aliens are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAMoonCresta.slnx                   # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAMoonCresta.slnx                    # every test runs headless: no window opens
dotnet run --project src/AVAMoonCresta            # play it; add -- --no-audio for silence
CRESTA_SHOTS=$PWD/Images dotnet test tests/AVAMoonCresta.Tests --filter ScreenshotTests   # refresh the README's pictures
CRESTA_PROBE=1 dotnet test tests/AVAMoonCresta.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVAMoonCresta/Sounds (standard-library Python only)
```

The probe reports how the computer pilot gets on, wave by wave, for tuning; `CRESTA_PROBE=lazy` runs one that
just sits and fires. Without the variable it does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAMoonCresta/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over, initials, high scores) and the
    fixed-step loop; `GameEngine.Play.cs` has the play itself: the waves, the meteor storm and docking.
  - `Entities.cs` has `Controls`, the screen measures (`Field`, including `Field.Step`), the rocket's stages, the
    aliens, shots, bombs and `Docking`.
  - `ShipPilot.cs` is the computer pilot, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAMoonCresta/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled to
  the window and runs the 60 Hz timer.
- `src/AVAMoonCresta/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAMoonCresta/Sounds/`: the generated WAV files.
- `tests/AVAMoonCresta.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests` and
  `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate, so
  tests can play whole waves deterministically from a seed. Draw random numbers from the engine's seeded
  `Random` (`new GameEngine(seed, table)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** The rocket moves only left and right: `InputHandler` sets `Dx` (negative
  is left) and `Fire` (Up and W fire too), and leaves `Dy` at 0. Test new controls through `InputHandler`, not
  by building `Controls` directly.
- **Test hooks.** `GameEngine.Calm` (internal, for tests) stops anything new coming, swooping or dropping bombs
  by itself.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAMoonCresta`). A
  `GameEngine` built without a table loads that file, so tests load the table from a temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
