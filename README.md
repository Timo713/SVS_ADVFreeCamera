# SVS_ADVFreeCamera

A free camera for conversation scenes in **Summer Vacation Scramble**
(SamabakeScramble), a Unity IL2CPP game by IllGames.

The game already gives you a free camera in character creation and in H scenes,
but conversation (talk/ADV) scenes lock you to scripted camera angles.
CameraPlus aims to give you the same freedom during dialogue.

**v1.0.0.** Free camera during conversations, with optional centering on the
character being spoken to. Research notes are in [FINDINGS.md](FINDINGS.md).

> **Remove or disable other camera plugins** (SVS_3rdPov, SVS_PovX) while using
> this — they overwrite the camera every frame and will silently defeat it.

## Features

- Free camera control during conversation scenes
- Optional auto-centering on the speaking character's head
- Optional persistence of the last camera position between scenes
- Rebindable hotkeys

## Approach

Rather than fighting the game for control of the camera transform, CameraPlus
follows the approach Koikatsu's
[TalkFreeCamera](https://github.com/Mantas-2155X/TalkFreeCamera) took: attach the
game's *own* camera control component (`CameraControl_Ver2`) to the main camera
and toggle it on demand.

SVS turns out to carry that class over from the older Illusion codebase with
identical method signatures, so the same trick should apply. Its
`TargetSet(Transform, bool)` also gives auto-centering on a character's head
essentially for free. Details and evidence in [FINDINGS.md](FINDINGS.md).

## Requirements

- Summer Vacation Scramble
- BepInEx 6 for Unity IL2CPP (`6.0.0-be.752`; this is what SVS-HF_Patch ships)

## Building

Requires the .NET SDK. The project references the game's generated interop
assemblies directly, so **the path in `CameraPlus/CameraPlus.csproj` must point
at your install**:

```xml
<InteropPath>D:\Games\SamabakeScrambleTest\BepInEx\SamabakeScramble\interop</InteropPath>
```

Then:

```bash
dotnet build CameraPlus.sln
```

`nuget.config` adds the BepInEx package feed, since `BepInEx.Unity.IL2CPP` is not
published on nuget.org.

### Why interop instead of the IllusionLibs NuGet packages

Most SVS plugins reference `IllusionLibs.SamabakeScramble.*` from the IllusionMods
feed. This project doesn't, because no published version matches the test install
— its interop assemblies and `GameAssembly.dll` disagree on hash. Referencing the
local interop folder guarantees that what we compile against is exactly what
loads at runtime. See [FINDINGS.md](FINDINGS.md) §7.

## Installing

Copy the built DLL into your BepInEx plugins folder:

```
CameraPlus/bin/Debug/net6.0/SVS_ADVFreeCamera.dll  →  <game>/BepInEx/plugins/
```

## Project layout

| File | Purpose |
|---|---|
| `CameraPlus/ADVFreeCameraPlugin.cs` | `BasePlugin` entry point, config, logging |
| `CameraPlus/ADVFreeCameraComponent.cs` | `MonoBehaviour` for per-frame work |
| `CameraPlus/Properties/AssemblyInfo.cs` | DLL metadata, driven by the plugin constants |
| `FINDINGS.md` | Discovered game classes and candidate hook points |
| `CLAUDE.md` | Working notes and project conventions |

## Credits

Built from [TonWonton/BepInEx_Il2CppPluginTemplate](https://github.com/TonWonton),
which is in turn based on
[SpockBauru/BepInEx_IL2CPP_Template](https://github.com/SpockBauru/BepInEx_IL2CPP_Template)
and [IllusionMods/PluginTemplate](https://github.com/IllusionMods/PluginTemplate).
Template licensed MIT-0 — see [LICENSE](LICENSE).

Design owes a great deal to Mantas-2155X's TalkFreeCamera, and to the SVS
plugins by TonWonton, DarkSoldier27 and the IllusionMods community.
