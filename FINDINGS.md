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

### Panning that follows the view (SOLVED in 1.2.0)

Read with `isil.ps1`: `CameraController.InputMouseProc` pans along
`GetTransformDirection(v)`, which is `_transformCache.TransformDirection(v)` - the
camera transform as it is at that moment (then turned into `_transBase`'s space
if that is set; it is null here). The scenario puts the camera transform back to
the scene's own angle every frame before the controller runs, so a pan moved
along the angle the scene started at, not the one on screen. Rotation and zoom
were never affected.

The fix (`PanFix.cs`, CONFIRMED 2026-10-26): a Harmony prefix on
`InputMouseProc` turns our camera to `Quaternion.Euler(CameraRot)` first. The
stick pan takes its axes from `CameraRot` for the same reason. One free camera
type is left; the dialogue keeps running.

Tried before and dropped, one line each:

- Setting `ADVScene.IsCameraLock` and `CommandController.useCorrectCamera`: both
  already held the wanted value.
- A prefix returning false on `MainScenario.Update`: it runs, but does not skip
  a Unity message.
- Overwriting the orbit centre every frame: pins the camera to one point.
- **Switching `MainScenario` off** (the old Camera Type 2): pans correctly, but
  hides the text until switched back and replays the line (voice, lip sync,
  animation), which needed `_currentLine` saving, a one-frame re-enable and a
  voice silencer. Removed with the type choice in 1.2.0.

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

A click is **two stages**, and both belong to `ADV.TextController`, reached as
`scenario.TextController`:

- `IsCompleteDisplayText` - whether the line has finished arriving letter by
  letter. This is the real state; the identically named field on
  `TextScenario.NextInfo` is only what a caller claims.
- `ForceCompleteDisplayText()` - finish the line now, which is what the first
  click does.

Only once the line is complete does a click advance, through
`MessageWindowProc(NextInfo)` with `Set(true, true, false)`. Getting this wrong is
expensive and the symptoms mislead: asking for the next line while the text is
still arriving does not advance and does not fail either - **the scenario queues
the request** until the typewriter finishes. Clicks appear to be swallowed, then
several lines advance by themselves, and everything feels one to two seconds
behind. `RequestNextLine()` on its own advances, but skips whatever the click
handler does about the line that just ended: the previous voice line starts over
whenever the new line has no voice of its own.

Careful with the bool properties on `TextScenario`: only `IsChoice`, `IsSkip`,
`IsAuto`, `IsInputLock` and friends belong to it. `IsNext`, `IsCompleteDisplayText`
and `IsVisible` show up in a dump of the type but belong to nested classes, and
do not compile against the scenario itself.

Full write-up, including the corroborating `ADV.Commands.Base.Regulate` scenario
command, is in the companion notes for the SVS_3rdPov work,
where the mechanism was first identified.

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

Group framing - averaging the bones of everyone in frame - was tried once and
removed: counting every loaded character in shot could not tell a one-character
scene from a two or three hander (map characters are loaded too, far away).

**Back in 1.2.0, UNVERIFIED in game**, now counting the scene's own cast:
`scenario.CommandController.Characters` (`Dictionary<int, ADV.CharaData>`), kept
only if the head bone is active, within 10 m of the camera and inside its view,
and **never the player** (by `Heroine` against `SV.GameChara.Player`, and by
body against `SV.GameChara.PlayerAI.chaCtrl`), and **only if `Human.visibleAll`**.
CONFIRMED 2026-10-26 from the log: a one-person talk listed two cast members
in shot within 10 m (`chaM_03, chaM_04`), so it was treated as a group and
AllCharacters kept the game's shot instead of centring on the bone. Which of
the two filters drops the extra one is UNVERIFIED; "On stage:" now prints each
head position and "left out:" with the reason.

Neither mode follows anyone after the camera comes on: both read the bones at
that moment. AllCharacters's point stays straight ahead of the game's camera
(only its height changes), which is why it never looked centred on one person.
With two or more, setting "Several Characters": **AllCharacters** keeps the game's
shot (orbit point straight ahead) and only sets its height to the average of their
focus bones; **MostFavoured** centres on the one the player likes most, by
`SaveData.Extension.ActorExtension.GetFavorInfo(SV.GameChara.Player, actor)`,
`longSensitivityCounts`, indexed by `SaveData.SensitivityParameter.Sensitivity`
(LOVE 0, FRIEND 1, INDIFFERENT 2, DISLIKE 3; CONFIRMED from the enum): love
minus dislike first, friend minus indifferent to break a tie. Whether these
counters are the points the game shows is UNVERIFIED; the log prints all four.

The centre is worked out once, when the camera comes on. It does not follow the
character afterwards (the Aicomi port does); the developer found that fine.

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

The start is on the **first frame** that test passes. A counter for "the camera
has sat still" was still being computed after its sliders were removed, and never
read; it is gone (v1.2.0). CONFIRMED fine in SVS at the time ("delay 0 fine").

The Aicomi port found the same instant start centring on a bone caught
mid-animation, and fixed it by letting the orbit centre follow the focus bone for
3 seconds. **Not needed in SVS**: with Focus Point = Character and the camera
starting by itself, the character is centred (CONFIRMED 2026-10-26).

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

Update 2026-10-06: the HF-patched install already holds that decrypted metadata
(`BepInEx\SamabakeScramble\decrypted_global-metadata.dat`), and Cpp2IL turns it
plus `GameAssembly.dll` into a per-method listing in seconds, read-only. That
answers most "what does this method do" questions without Ghidra.

dnSpy ships a command line build, `dnSpy.Console.exe`, which needs neither the
GUI nor the MCP server:

```bash
dnSpy.Console.exe --no-color -t ILLGames.Unity.CameraController Assembly-CSharp.dll
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
frame. That defeated an **early** version of this one, which drove `Camera.main`
as well - turning them off was what first made the camera move at all. It does
not apply any more: conversations are driven through the ADV scene camera (§2),
and testing with both plugins enabled found nothing wrong. Do not disable them
out of habit. The SVS_3rdPov in the test install is a reworked build rather than
the original, so the original may still behave differently.

---

### SVS Screenshot Expansion

`com.korg.svs.ScreenshotExpansion` has its own talk camera on the same object
this plugin drives - the Simulation camera named `Camera` - so the two fight.
It does not use `CameraController`: it backs up the transform on the first drag
and rebuilds it with `RotateAround` about a pivot every `LateUpdate`.

Every part of that camera starts in one private method,
`SVS_ScreenshotEx.ScreenshotBehaviour.UpdateDialogueCamera` - hotkeys, drag
handling, and the backup and orbit state its `LateUpdate` later writes from. A
prefix skipping it leaves that state unset, so the rest of its camera code has
nothing to do, while its screenshot code, which runs earlier in `Update`, is
untouched. `ScreenshotExpansionCompat` does this, found by name so there is no
reference to their DLL.

**Prefix-skip works here** even though §2 says it does not on this game: that
rule is about the game's own IL2CPP methods. Another plugin is ordinary .NET, and
Harmony skips its methods normally.

A soft `BepInDependency` on their GUID makes BepInEx load theirs first when it is
present, so their type exists by the time this plugin `Load()`s. Without it,
load order decides whether the patch finds anything.

---

## 6. Features added in 1.2.0, and open ideas

### Pause and menus (SOLVED in 1.2.0, CONFIRMED 2026-10-28)

`CameraController::LateUpdate` (isil.ps1) returns before input and before the
virtual `CameraUpdate()` (which applies the pose) when a `SingletonInitializer` is
not initialised, when `Manager.Scene.IsOverlap`, or when `Scene.IsNowLoadingFade`.
The pause menu is an overlap scene, so the controller stopped applying our pose and
the scenario's per-frame reset (section 2, panning) was all that showed.
`PauseFix.cs`: a postfix on `LateUpdate` calls `CameraUpdate()` for our controller
on those frames, and logs "Menu over the conversation" once.

Tried before and failed, do not repeat: writing the pose back (transform,
`CameraPos`/`CameraRot`/`CameraDir`/`CameraFov`, both) from `LateUpdate` or from
that same postfix. Camera Type 2 (scenario driver off) was immune, which pointed
at the scenario. `MainScenario.Update` only calls the base `Update` while a
`SV.Config.ConfigWindow` static flag is set (not followed up).

### Several Characters = Speaker (1.2.0, CONFIRMED 2026-10-28)

Default since 2026-10-28; AverageHeight was renamed AllCharacters (never released,
so no carry-over). At the start of a talk `CurrentChara` is usually not set yet:
the camera then keeps the game's shot with nobody picked, checks every frame, and
starts on the first cast member to speak outright if that is within 0.75 s of
coming on, else glides there. Picking the newest-loaded character first caused a
glide from someone who had not spoken (reported 2026-10-28).

Centres on `TextScenario.CurrentChara` when it is one of the stage cast (by
`ChaCtrl.Pointer`); the player and narration are not in the cast, so they leave
the centre where it is. Checked four times a second; on a change the controller's
`CameraPos` glides to the new `FocusPoint` (exponential, rate 9/s, lerping from
the current `CameraPos` so a pan is not fought). Angle and zoom are untouched.
The same `CurrentChara` is what the Aicomi port uses.

**Who speaks first (CONFIRMED 2026-10-28).** `ADV.Commands.Chara.Create` calls
`TextScenario.ChangeCurrentChara`, so before the first line `CurrentChara` is just
whoever was created last (it was the tall character, centred before her turn).
The current speaker is set by the `CharaChange` command: `Int32.Parse(args[0])`
then `ChangeCurrentChara(no)` -> `CommandController.GetChara(no)` (isil/xref).
So when the camera comes on with no text showing, `UpcomingSpeaker` scans
`CommandPacks` from `CurrentLine` (300 at most) for the first `CharaChange`
before a `Text`; that cast member is the start. Many Apartment `CommandSelect`
commands call `ChangeCurrentChara` from code, which the scan cannot see: then it
falls back to the game's shot and moves at the first line. After that a speaker
change counts only while text is on screen.

**Tall characters left out of the cast (CONFIRMED from the log 2026-10-28).** The
in-shot test used the head 5% inside the frame; a very tall character's head is
at or above the top, so a two-person talk counted one (single-character path,
centred on her even under AllCharacters) or nobody. The cast now counts a
character whose head or body middle (between root and head) is anywhere in the
frame; "left out" prints the head's viewport position.

### Follow Character (1.2.0, UNVERIFIED)

On by default (briefly off, back on after testing 2026-10-28). Each frame the centre (`CameraPos` and the pivot) moves by how far
the focus bone moved, so a pan offset is kept. Paused while the cursor is hidden
for a drag, while the controller is moving the camera, and during a speaker
glide; the bone is re-read when it resumes. The Aicomi port instead follows to
the bone's position for a few seconds after the start, until the mouse is used.

### From the Aicomi port, not yet tried here

The Aicomi port (AC_ADVFreeCamera) turned these up. None is verified in SVS.

- **Driving the controller by hand.** The port calls `InputMouseWheelZoomProc`,
  `InputMouseProc`, `InputKeyProc`, then `CameraUpdate()` from its own
  `LateUpdate`. SVS has all four on `ILLGames.Unity.CameraController` (CONFIRMED,
  names only). Untested idea for Camera Type 1 panning: run them after the
  scenario has had its turn and see whether a pan survives. Aicomi itself has no
  panning problem - nothing re-applies the camera there - so this is an
  experiment, not a known fix.
- **The wheel and the text** - done here in 1.2.0, UNVERIFIED in game. Unmodded
  SVS advances the text on the wheel and does not zoom (CONFIRMED 2026-10-26). So
  "Game Uses Wheel During Text" (default on): while text or choices are up the
  camera ignores the wheel (`_zoomSpeed` 0, no wheel turning), and because
  blocking `ClickNext` also kills wheel-advance, a scroll down while the plugin
  holds the block calls `AdvanceOneLine()` (`WheelAdvance`, from Update only).
- Tried in the port and removed, do not repeat: making Camera Distance absolute
  for the default camera by measuring to the focus bone. Bones lean with the pose,
  so the view shifted a few centimetres even at the default.

---

### Background blur and black bars (1.3.0, CONFIRMED 2026-10-29)

Both belong to `SV.HighPolyBackGroundFrame`, found under `Manager.Game`'s
transform; its `animFrame` children are `Panel` (the blur, a `TranslucentImage`),
`UpFrame` and `DownFrame` (the bars). They stay active; the animator shows and
hides them. SVS_CheatTools ("Show background frame/blur") and SVS_3DRooms just
SetActive them (decompiled 2026-10-29). The same frame is opened for
conversations (`ADVManager._OpenADVAsync`, `BackGroundFrame` command, `PCHTask`)
and blur-only for menus (`OpenBlurOnly`: save/load, correlation diagram, entry,
SimulationScene), so `BackgroundFrame.cs` hides them only during a conversation,
from `LateUpdate` (after the animator), and puts them back after. Settings named
as in CheatTools: Show Background Blur, Show Background Frame (on = as the game).
SVS_3rdPov's old Hide Blur also raised the contrast; how it hid the blur was not
looked into.

## 7. Controller and cursor (v1.2.0)

### Controller

Unity cannot read the right stick or the triggers in SVS: only the legacy input
system ships, and its settings name axes for the left stick (`Horizontal` /
`Vertical`, shared with WASD and the arrow keys) and the D-pad only. CONFIRMED in
SVS_FreeRoam (its FINDINGS, section 15). So `Gamepad.cs` reads the pad
from Windows through XInput, left stick included to keep the keyboard out.

Mapping: left stick pans (`CameraPos` plus the camera right and up), right stick
turns (`CameraRot`, same signs as the mouse so Invert X / Y cover both, pitch held
short of straight up or down), right trigger in and left trigger out
(`CameraDir.z`). All written straight to the controller in `LateUpdate`, as the
wheel and middle-button remaps already do for turning. CONFIRMED working
(2026-10-26). The stick pan has the same Camera Type 1 fault as the mouse pan:
writing `CameraPos` from the plugin is undone the same way, so this is no way
round the panning bug.

The toggle button is a plain `KeyCode` read through Unity (`Toggle Button`,
default `JoystickButton6` = Back / View / Select), so it can be rebound.
UNVERIFIED in game.

**SVS_FreeRoam uses the left stick in conversations too.** A push past 0.6 stands
in for the D-pad and starts selecting on-screen buttons. `FreeRoamCompat` patches
its `SVS_FreeRoam.GamepadUI.NavDirection(bool useStick)` and turns `useStick` off
while the free camera and Gamepad Camera are on; the D-pad itself keeps working.
Found by name, like the Screenshot Expansion one. CONFIRMED working (2026-10-26).

**No `BepInDependency` for either (1.2.0).** KKManager listed both soft
dependencies as requirements and would not let the player disable them
(reported 2026-10-28). Both `Apply()`s now run from
`IL2CPPChainloader.Instance.Finished` (BepInEx.Core `BaseChainloader`), when
every plugin is loaded, whatever the order. UNVERIFIED in game. If that
method is renamed the log says so and the overlap returns.

### Cursor

Hidden from the moment any mouse button is pressed (1.2.0; it used to wait for
5 px of movement, which looked like a delay next to the crosshair), not while
paused, not for a press that landed on a button, slider or
scrollbar (top uGUI raycast hit has a `Selectable`), not while an IMGUI control is
being dragged (`GUIUtility.hotControl`). Restored to how it was found. Hiding
CONFIRMED (2026-10-26). The crosshair is drawn exactly while the camera is being moved
(mouse or controller). After the controller, the cursor stays hidden until the
mouse moves 3 px (asked for 2026-10-28). Since the cursor is pinned from the press, "was it a drag"
(for Left Click Advance = TapOnly) adds up each frame's distance from the pin
and calls it a drag at 5 px.

Hiding means both the Windows cursor and `SV.SpriteCursor.imgCursor`, the image
the game can draw as a pointer of its own. Which of the two a conversation shows
was never settled; hiding both works.

**Held in place** while hidden, as character creation does: the screen position
at the press (`GetCursorPos`) is put back every frame with `SetCursorPos`, in
`NativeCursor.cs` (native declarations kept out of the injected component), and
once more on release. UNVERIFIED in game; the risk is that the camera stops
turning if Unity reads mouse movement from the position. With the pointer held
still, TapOnly can no longer measure a drag by distance, so a press that hid the
cursor counts as a drag outright (`_pressDragged`).

Not tried: the game's `SetCursorLock(true)` on `GameCursor` / `SV.SpriteCursor`.
---

## 8. Working practice

Three rounds were lost to `str.replace` silently matching nothing while the
change was reported as done. **Assert every edit and grep for the result before
building.** Gate deployment on the build actually succeeding — a failed build
once shipped a stale DLL that was then tested as if it were new.
