# FINDINGS

Reference for SVS_ADVFreeCamera. Everything here is either read from game
metadata via the dnSpy MCP or confirmed in game. The round-by-round history has
been removed; what remains is what is still true.

Sources and their limits:

- **dnSpy MCP** gives signatures, fields, enums, hierarchy. Method bodies are
  empty stubs — the interop assemblies carry no logic.
- **RuntimeUnityEditor** gives runtime truth: hierarchy, live field values, a C#
  console. Most answers here came from there, not from static analysis.
- **Asset bundles** hold bone names. They are not in the assembly, and IL2CPP
  string metadata is encrypted, so `search_strings` finds nothing.

---

## 1. How the free camera works

The design is Koikatsu TalkFreeCamera: do not drive the camera by hand, attach
the game's own camera component and let it do the work.

`ILLGames.Unity.CameraController` is what character creation and H scenes use.
`CameraControl_Ver2` also exists, inherited from the older Illusion codebase, but
SVS uses it nowhere — it was tried and dropped.

**Attaching it is not enough.** `AddComponent` returns a blank component, and
Unity runs `Awake()` before it returns, so fields cannot be filled in first — and
this `Awake` throws when they are empty. The working sequence:

1. Deactivate the camera GameObject (Unity defers `Awake` while inactive)
2. `AddComponent<CameraController>()`
3. Fill in everything below
4. Reactivate — `Awake` now runs with real values
5. Apply the framing *after* that, or `Awake` overwrites it

Fields `Awake` needs, none of which `AddComponent` provides:

| Field | Value |
|---|---|
| `_cameraData` | `new CameraController.CameraData()` |
| `_rotateMove` | `new CameraController.RotateMove()` |
| `_fov_k__BackingField` | `new UniRx.FloatReactiveProperty(fov)` — the one that finally made `Awake` succeed |
| `_isDrags_k__BackingField`, `_isButtons_k__BackingField` | `new Il2CppStructArray<bool>(4)` |
| `_nowCamera`, `_transformCache` | the camera and its transform |
| `noCtrlCondition`, `keyCondition`, `zoomCondition` | `Il2CppSystem.Func<bool>` via `DelegateSupport.ConvertDelegate` |
| `_targetTrans`, `_targetTexTrans` | the orbit pivot |

Framing is `CameraPos` (orbit centre), `CameraRot` (euler), `CameraDir`
(`0,0,-distance`), `CameraFov`. The camera ends up at
`CameraPos + rotation * CameraDir`, verified against measured values.

**Never destroy the component.** `Awake`/`Start` subscribe to per-frame UniRx
observables, disposed by an `ObservableDestroyTrigger` on the **GameObject**.
Destroying only the component leaves them firing at a dead object — a
`NullReferenceException` every frame, which starves the shared update chain and
leaves characters T-posing. Disable and reuse instead.

`MonoBehaviour.enabled` does **not** stop a UniRx subscription, so nothing those
subscriptions might read may be destroyed either: the orbit pivot lives for the
whole session under `DontDestroyOnLoad`.

Measured framing for a standard character: camera `(0, 1.42, 1.00)`, angles
`(13.84, 180, 0)`, fov 40, orbit centre `(0, 1.17, 0)`, radius 1.04.

---

## 2. The conversation scene

- `ADV.ADVManager` via `FindObjectOfType`. `IsADV` says a conversation is running.
- `ADV.ADVScene` at `SimulationScene/Manager/ADVManager/ADVScene(Clone)`.
  `ADVCamera` is the camera to drive — it is also `_activeCamera`, named
  `Camera`, tagged Untagged. `Camera.main` is `Main Camera`, the **background**.
- `ADVScene.Scenario` is the running `TextScenario`; `Stand` marks where the cast
  stands.
- `ADV.MainScenario` sits on the same object and inherits `TextScenario`, so
  `_currentLine`, `IsChoice` and `IsInputLock` are all readable and writable.

### MainScenario re-applies the camera every frame

This is why panning does not follow the view: the orbit centre is reset each
frame, so rotation and zoom survive — both are relative to the centre — while
panning, which *moves* the centre, is silently undone.

Only clearing `MainScenario.enabled` stops it. Specifically ruled out:

- `ADVScene.IsCameraLock` and `CommandController.useCorrectCamera` were **already**
  in the state we wanted, so setting them did nothing.
- **Harmony prefixes returning false do not skip the original on this target.**
  The prefix runs and logs, but the method executes anyway. Verified by
  suppressing `MainScenario.Update` unconditionally and seeing the dialogue UI
  completely unaffected, while unchecking the same component by hand breaks it
  instantly. Do not reach for prefix-skip here.

Overwriting the orbit centre every frame instead was tried and does not work:
the centre gets pinned, so the camera can only rotate about one fixed point.
Worse, that failed approach silently became Camera Type 2 when Camera Type 3
was removed - the working branch was left unreachable behind a type check that
nothing satisfied any more. If panning is wrong, check `EngageFix` reaches the
`_mainScenario.enabled = false` line at all.

Camera Type 2 therefore disables the driver, which costs the dialogue UI until it
is switched back. Toggling the driver replays the current line — voice, lip sync,
subtitle and animation together — so `_currentLine` is saved and restored around
it, and the driver is switched back on the next frame to clear the leftover
subtitle. Restoring the UI needs the `ADVScene(Clone)` GameObject cycled off and
on.

---

### Blocking click-advance

`TextScenario.Regulate` is the gate. `MainScenario.IsInputLock` is not, and can
be held true every single frame while clicking still advances - do not go back
to it. `Regulate.Control` is a bit-flag set of what is currently **blocked**,
over `ADV.Regulate/Controls`:

| `Next` | `ClickNext` | `Skip` | `Auto` | `AutoForce` | `Log` |
|---|---|---|---|---|---|
| 1 | **2** | 4 | 8 | 16 | 32 |

Measured from the RUE console during a conversation:

```csharp
var scenario = UnityEngine.Object.FindObjectOfType<ADV.ADVScene>().Scenario;
scenario.Regulate.Control                                        // 0 in normal dialogue
scenario.Regulate.AddRegulate(ADV.Regulate.Controls.ClickNext);  // click and wheel stop advancing
scenario.Regulate.SubRegulate(ADV.Regulate.Controls.ClickNext);  // both work again
```

Reading 0 is what settles the direction: the set lists restrictions, so Add
blocks and Sub restores. The flag **holds** - `Control` still read `ClickNext`
seconds later - so `UpdateRegulate` does not recompute it away and there is no
per-frame fight. Blocking `ClickNext` also stops the mouse wheel advancing, and
choice buttons stay clickable, so choices need no special case.

The plugin writes the flag every frame regardless, which is free and survives
the scenario object being swapped between lines, and it only ever lifts the
flag from the exact `TextScenario` it set it on - a scenario already blocking
clicks by itself is left alone.

Advancing by hand, for modes that block the click and then decide: `TextScenario`
has `RequestNextLine()` and `MessageWindowProc(NextInfo)`, the second being the
game own click handler - `new TextScenario.NextInfo()` then
`Set(isCompleteDisplayText, isNext, isSkip)`. Lift the flag around the call and
put it back in the same frame, so it makes no difference whether that path
checks `ClickNext` itself.

**Only advance when the message frame is on screen.** With no frame showing,
asking for the next line ends the conversation, so a tap anywhere - including
one meant for a pause or config button - closes it. `TextScenario._messageWindow`
is the frame RectTransform and `_msgWindowCanvas` the canvas that draws it; an
overlay canvas is already in screen pixels, anything else needs projecting
through `canvas.worldCamera`. The same rect is what makes TextAreaOnly exact at
any resolution, with no height to calibrate.

The frame is **not deactivated** when it goes away at the end of a conversation:
`activeInHierarchy` stays true and the rect keeps its size, so an active check
alone passes and a tap still ends the conversation. What actually changes is that
the window leaves the screen, so the test that works is whether its rect still
overlaps `0,0,Screen.width,Screen.height`. Canvas enabled and CanvasGroup alpha
are checked too, being the other ways UI hides without being deactivated.

Careful with the bool properties on `TextScenario`: only `IsChoice`, `IsSkip`,
`IsAuto`, `IsInputLock` and friends belong to it. `IsNext`, `IsCompleteDisplayText`
and `IsVisible` show up in a dump of the type but belong to nested classes, and
do not compile against the scenario itself.

Full write-up, including the corroborating `ADV.Commands.Base.Regulate` scenario
command, is in D:\CoderdPov\FINDINGS.md §4.

---

## 3. Characters and bones

`Character.Human` is **not** a MonoBehaviour, so `FindObjectsOfType` cannot see
it. `Character.Human._list` is a static registry of every loaded character.

`TextScenario.HeroineList` is no use for the camera: its `ChaCtrl` points at the
character as it exists on the simulation map, hundreds of metres away, not the
copy posed in front of the conversation camera.

**Take the newest entry in `_list`.** The conversation character is spawned when
the scene starts, so newest is the right one — and unlike any distance test it
does not care how tall anybody is. Every distance rule tried (nearest to camera,
nearest to `Stand`, with and without a view cone or a radius) failed on unusually
tall characters, whose head bone is further from any anchor simply by virtue of
their height.

Group framing - averaging the bones of everyone in frame - was tried and removed.
It could not reliably tell a one-character scene from a two or three hander, and
misjudging that broke centring outright.

### Bones

Chain, all lower case:
`cf_j_root / cf_n_height / cf_j_hips / cf_j_spine01 / cf_j_spine02 /
cf_j_spine03 / cf_j_neck / cf_s_head`

**Search from the skeleton that owns the real head bone** (`face.objHeadBone`,
walking up to `cf_j_root`). A character carries more than one rig — a low poly
copy, a spare body — so a search from the character transform can return a bone
belonging to a rig that is not being drawn, positioned somewhere else. Match by
**exact name**: several bones contain "neck", and only `cf_j_neck` is the visible
one.

| Setting | Bone |
|---|---|
| Head | `cf_s_head` |
| Neck | `cf_j_neck` |
| Chest | `cf_j_spine03` |
| Waist | `cf_j_spine02` |
| Hips | `cf_j_waist02` |
| Feet | `cf_j_root` (`cf_n_height` is the same height) |

Anything ending `_L` or `_R` is a limb and off centre.

Console helper, with the free camera on and Focus Point set to Character:

```
SVS_ADVFreeCamera.ADVFreeCameraComponent.Bones()
SVS_ADVFreeCamera.ADVFreeCameraComponent.AimAt("cf_j_hips")
```

`GameObject.Find` is no use for bones — it matches the first object of that name
anywhere in the scene, usually a character out on the map.

---

## 4. Starting automatically

The scenario and its text both start during sequences that only *lead up* to a
conversation: being invited to an activity, being approached, being walked to a
room, choosing from the action buttons. Every readiness signal tried
(`Scenario != null`, `_currentLine > 0`, the ADV camera being enabled, a choice
having been made) is satisfied then, and calibrating at that point leaves the
camera framed on an empty shot for the rest of the conversation.

What works: **wait for a character to be in frame** — within `Start Proximity`
metres of the conversation camera *and* inside its viewport. Distance alone is
not enough, since a character can be near the camera without being in the shot.
Tall characters need a larger proximity, because the head bone sits further away.

---

## 5. Environment

The interop assemblies match no published `IllusionLibs` NuGet version:
`assembly-hash.txt` records `5e3dc5c2…` while BepInEx computes `dc9b08db…` for
the installed `GameAssembly.dll`. The csproj therefore references the local
interop folder directly, so compile-time and runtime are the same assemblies.

`GameAssembly.dll` MD5 is `6b5fe009a63e7381ef042e0872080373`, which matches
`SVS/v1.1.6.dat` in y0soro/BepInEx-Builds. Decrypted metadata would let
Il2CppDumper emit the symbol script that makes Ghidra decompile
`GameAssembly.dll` into readable logic — the only route to answering what
`MainScenario.Update` actually does, why prefix-skip is ignored, and what moves
the camera during a pause. It does **not** give readable C# on its own;
Il2CppDumper output has empty bodies too. Do it offline against a *copy* of
`GameAssembly.dll`, never by swapping the BepInEx build.

dnSpy has a command line build, `D:\Code\_tools\dnSpy\dnSpy.Console.exe`, which
needs neither the GUI nor the MCP server:

```bash
D:/Code/_tools/dnSpy/dnSpy.Console.exe --no-color -t ILLGames.Unity.CameraController Assembly-CSharp.dll
```

`-t` takes `Namespace.Name` or a bare `Name`, and prints the whole type to
stdout - every field, property and signature. Method bodies are still stubs
for game code, but for "what fields does this type have" it is faster and more
complete than the MCP, and it works when dnSpy is closed. That is how the
camera field list below was settled.

`ILLGames.Unity.CameraController` has exactly four speed fields: `_xRotSpeed`,
`_yRotSpeed`, `_zoomSpeed` and `_moveSpeed`, and **no invert or reverse field
of any kind**. Rotation therefore inverts per axis by sign, while panning and
the right-drag dolly share `_moveSpeed` and cannot be inverted separately.

Other camera plugins (`SVS_3rdPov`, `SVS_PovX`) overwrite `Camera.main` every
frame and will silently defeat this one. Rename or remove them while testing.

---

## 6. Still open

- **Pause** returns the camera to its default framing. The readout confirms it is
  our camera being moved, not a different one rendering; holding the pose against
  `Time.timeScale == 0` did not help.
- **Panning with the dialogue intact.** Only Camera Type 2 pans correctly, and it
  costs the UI. Overwriting the orbit centre every frame was tried and failed.

---

## 7. Working practice

Three rounds were lost to `str.replace` silently matching nothing while the
change was reported as done. **Assert every edit and grep for the result before
building.** Gate deployment on the build actually succeeding — a failed build
once shipped a stale DLL that was then tested as if it were new.
