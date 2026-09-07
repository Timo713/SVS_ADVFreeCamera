# SVS_ADVFreeCamera

## What this is

A BepInEx plugin for Summer Vacation Scramble (SamabakeScramble), a Unity
IL2CPP game. Free camera control during conversation (talk/ADV) scenes, with
optional centering on the character being spoken to.

It works by attaching the game's own `ILLGames.Unity.CameraController` to the
conversation camera and configuring it, rather than driving the camera by hand.
Read FINDINGS.md before changing any of that — nearly every part of the setup
sequence exists because a simpler version was tried and failed.

Built from TonWonton/BepInEx_Il2CppPluginTemplate (MIT-0).

## Paths

Machine-specific paths — the game install, the reference folder, the tools —
live in `CLAUDE.local.md`, which is not tracked by git. `Local.props` holds the
one the build needs.

## Tools

1. RuntimeUnityEditor — runs inside the live game, browse the actual scene
   hierarchy, inspect components, watch field values change, toggle things at
   runtime. Fastest answer to "what object is this at runtime".
2. Published source of other SVS plugins — real C# with real API calls. Often
   answers a question faster than static analysis.
3. dnSpy MCP — available ONLY while dnSpy is open with Assembly-CSharp.dll
   loaded and MCP Server > Start clicked. Gives signatures, fields, enums, type
   hierarchy. Method bodies are EMPTY STUBS — no logic. Don't expect readable
   implementations of game methods.
4. dnSpy.Console.exe — the same decompiler without the GUI or the MCP server.
   `dnSpy.Console.exe --no-color -t Some.Type Assembly-CSharp.dll` prints a whole
   type to stdout. Best way to get a complete field list; game method bodies are
   still stubs.
5. dnSpy on compiled plugin DLLs — unlike game code, other people's plugin DLLs
   are real .NET and decompile fully. Load with `load_assembly`, then pass the
   assembly name to `search_types` to scope searches to it.
6. Ghidra + Il2CppDumper for actual method logic — expensive, and complicated by
   SVS's encrypted metadata. Last resort only. Decrypted metadata for SVS does
   exist (y0soro/BepInEx-Builds, metadata-index/index.json) and would give real
   method bodies, but using it means a different BepInEx build — do not put it in
   a test install that must keep SVS-HF_Patch's.

## Workflow

- Build: `dotnet build SVS_ADVFreeCamera.sln`
- Deploy: copy `SVS_ADVFreeCamera\bin\Debug\net6.0\SVS_ADVFreeCamera.dll` to the
  game's `BepInEx\plugins\` folder.
- After each test run, read `BepInEx\LogOutput.log` to diagnose failures.
- Record discovered class names, field names and hook points in FINDINGS.md,
  NOT in this file. Keep this file short — it's read every session.

## Build setup

- nuget.config adds the BepInEx feed; BepInEx.Unity.IL2CPP is not on nuget.org.
- BepInEx package version must match the install: 6.0.0-be.752.
- `Local.props` (untracked, copied from `Local.props.example`) sets `InteropPath`.
  The csproj references that interop folder directly rather than the IllusionLibs
  NuGet packages, because no published package version matched the install. See
  FINDINGS.md §5.

## Constraints

- IL2CPP: Harmony prefix/postfix/finalizer only, no transpilers. A prefix
  returning false does NOT skip the original on this game's Unity messages — see
  FINDINGS.md §2.
- Assert every scripted source edit and check the result before building. Silent
  no-op replaces have cost several rounds.
- Gate deployment on the build actually succeeding. A failed build once shipped a
  stale DLL that was then tested as if it were new.
- Custom MonoBehaviours need ClassInjector.RegisterTypeInIl2Cpp<T>(). Game types
  are already registered — AddComponent<GameType>() needs no injection.
- Plugin code runs on real .NET (CoreCLR), so normal C# libraries work. Only the
  game's own code is IL2CPP.
- Never modify anything in the reference folder or the game install except the
  plugins folder, or to add a new reference.
- The repository is public, but don't push, tag or release without being asked.

## The developer

Beginner programmer. Explain what you're doing and why. Don't assume familiarity
with C#, Harmony, Unity, or IL2CPP conventions. Give exact commands and exact
file paths rather than general directions. Stop and check in after each
significant step rather than doing everything at once.

## Reference plugins

**TalkFreeCamera** — https://github.com/Mantas-2155X/TalkFreeCamera
The design blueprint, ~40 lines. Adds `CameraControl_Ver2` to `Camera.main`,
toggles `enabled`, calls `TargetSet(head.transform, true)` to focus the speaker
and `Reset(1)` to restore. Koikatsu/Mono. SVS carries the same class over, but
uses `ILLGames.Unity.CameraController` everywhere itself — see FINDINGS.md §1.
Don't assume *other* KK APIs exist (it depends on KKAPI, which has no SVS
equivalent).

**SVS_3rdPov** — compiled DLL in the game's plugins folder (no repo; decompiles
fully in dnSpy). Third-person camera driven entirely from a postfix on
`SimulationScene.Update`. Detects conversations with
`SingletonInitializer<ADVManager>.Instance.IsADV` and during ADV snaps
`Camera.main.transform` to `playerAI.HeadPos`. Rebindable `ConfigEntry<KeyCode>`
hotkeys, `Unload()` calling `UnpatchSelf()`. Borrow: ADV detection, singleton
access pattern, config-driven keys.

**SVS_3DRooms** — https://github.com/TonWonton/SVS_3DRooms
Closest structural model: same plugin template, same author. Composites the
Simulation scene into H scenes. Brackets `HScene.Start` / `HScene.Dispose` to
attach and tear down a component, saves original camera state and restores it on
exit, drives the camera in `LateUpdate()`, and reorders the URP camera stack via
`GetUniversalAdditionalCameraData()`. Borrow: the whole attach/save/restore/
detach lifecycle.

**DarkSoldier27_SVS_Plugins** (branch DS27) —
https://github.com/DarkSoldier27/SVS_Plugins/tree/DS27
**IllusionMods_SVS_Plugins** — https://github.com/IllusionMods/SVS_Plugins
These two overlap heavily — several plugins appear in both with identical GUIDs.
DS27 is the experimental fork with extras. Reference only, not a dependency.

- SVS_ADVLoader — intercepts ADV scenario *loading* to substitute custom
  dialogue from disk, and can extract ADVs to JSON. Hooks `TextScenario.LoadFile`
  (prefix), `OpenData.Load(string,string)` (prefix returning false to skip the
  original), `LowCharaADV.Load` (prefix with `ref __result` to replace the
  return value), `SimulationScene.Update` (postfix, extraction hotkey). Borrow:
  how to reach `TextScenario`/`ScenarioCommand`, prefix-returns-false to suppress
  a game method, and `HarmonyPriority` to order against other plugins.
- SVS_NPCSubtitles — replaces the on-map speech-bubble sprite with readable
  text. Hooks `FukidashiUICanvas.Emit` (postfix, receives `Transform target`) and
  `LowCharaADV.VoicePlay` (postfix), resolving the speaker with
  `target.GetComponentInParent<AI>()`. **Caution**: this is ambient map chatter,
  not ADV conversation scenes — its speaker detection does not transfer.

**SVS_PovX** — compiled DLL, not yet examined. SVS_3DRooms carries explicit
compatibility code for it (GUID `SVS_PovX`), so it likely also moves the camera
and may conflict.
