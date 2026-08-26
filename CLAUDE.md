\# CameraPlus — SVS FreeCamera plugin



\## What this is

A BepInEx plugin for Summer Vacation Scramble (SamabakeScramble),

a Unity IL2CPP game. Goal: free camera control during conversation

(talk/ADV) scenes, with optional auto-centering on the speaking

character's head, and optional persistence of the last camera position.



Built from TonWonton/BepInEx\_Il2CppPluginTemplate (MIT-0).



\## The developer

Beginner programmer. Explain what you're doing and why. Don't assume

familiarity with C#, Harmony, Unity, or IL2CPP conventions.

Give exact commands and exact file paths rather than general directions.

Stop and check in after each significant step rather than doing

everything at once.



\## Paths

\- Project: D:\\Code\\CameraPlus

\- Game (test install): D:\\Games\\SamabakeScrambleTest

\- Interop assemblies: D:\\Games\\SamabakeScrambleTest\\BepInEx\\SamabakeScramble\\interop\\

\- Log file: D:\\Games\\SamabakeScrambleTest\\BepInEx\\LogOutput.log

\- Deploy target: D:\\Games\\SamabakeScrambleTest\\BepInEx\\plugins\\

\- Reference code: D:\\Code\\\_reference



\## Tools

1\. RuntimeUnityEditor — runs inside the live game, browse the actual scene

&#x20;  hierarchy, inspect components, watch field values change, toggle things

&#x20;  at runtime. Fastest answer to "what object is this at runtime".

2\. Local reference plugin source in D:\\Code\\\_reference — real C# with real

&#x20;  API calls. Often answers a question faster than static analysis.

3\. dnSpy MCP — available ONLY while dnSpy is open with Assembly-CSharp.dll

&#x20;  loaded and MCP Server > Start clicked. Gives signatures, fields, enums,

&#x20;  type hierarchy. Method bodies are EMPTY STUBS — no logic. Don't expect

&#x20;  readable implementations of game methods.

4\. dnSpy on compiled plugin DLLs — unlike game code, other people's plugin

&#x20;  DLLs are real .NET and decompile fully.

5\. Ghidra + Il2CppDumper for actual method logic — expensive, and

&#x20;  complicated by SVS's encrypted metadata. Last resort only.



\## Workflow

\- Build with `dotnet build`, then copy the DLL to the deploy target.

\- After each test run, read LogOutput.log to diagnose failures.

\- Record discovered class names, field names and hook points in FINDINGS.md,

&#x20; NOT in this file. Keep this file short — it's read every session.



\## Constraints

\- IL2CPP: Harmony prefix/postfix/finalizer only, no transpilers.

\- Custom MonoBehaviours need ClassInjector.RegisterTypeInIl2Cpp<T>().

\- Plugin code runs on real .NET (CoreCLR), so normal C# libraries work.

&#x20; Only the game's own code is IL2CPP.

\- The game has anti-mod checks and encrypted metadata; the test install

&#x20; uses SVS-HF\_Patch's custom BepInEx build. Never suggest replacing it.

\- Local only. Do not push anything to GitHub.

\- Never modify anything in D:\\Code\\\_reference or the game install except

&#x20; the plugins folder or if it's to add a new reference.



\## Environment notes

\- Windows. dnSpy 6.6.0 (.NET 8 build) at D:\\Code\\\_tools\\dnSpy

\- Visual Studio 2022 installed, but prefer `dotnet build` from terminal.

\- The game has anti-mod checks and encrypted metadata; the test install

&#x20; uses SVS-HF\_Patch's custom BepInEx build. Do not suggest replacing it.



\## Reference plugins

Cloned to D:\\Code\\\_reference. Read-only. Add useful information you might find.



\- DarkSoldier27\_SVS\_Plugins (branch DS27)

&#x20; https://github.com/DarkSoldier27/SVS\_Plugins/tree/DS27

&#x20; - src/SVS\_ADVLoader — creates custom dialogue and ADV scenes.

&#x20;   https://github.com/DarkSoldier27/SVS\_Plugins/tree/DS27/src/SVS\_ADVLoader

&#x20; - src/SVS\_NPCSubtitles — displays subtitles when NPCs talk.

&#x20;   https://github.com/DarkSoldier27/SVS\_Plugins/tree/DS27/src/SVS\_NPCSubtitles

&#x20; Experimental branch — reference only, not a dependency.



\- IllusionMods\_SVS\_Plugins — https://github.com/IllusionMods/SVS\_Plugins

&#x20; Many working SVS plugins, incl. SVS\_PersonalityLoader (talk/ADV scenes).



\- SVS\_3DRooms — https://github.com/TonWonton/SVS\_3DRooms

&#x20; Camera manipulation in SVS H scenes.



\- SVS\_3rdPov — compiled DLL only, in the test game's plugins folder.

&#x20; Original repo is gone. Third-person camera.



\- TalkFreeCamera (NOT cloned yet) — https://github.com/Mantas-2155X/TalkFreeCamera

&#x20; Koikatsu equivalent, but Mono not IL2CPP. Design ideas only;

&#x20; do not copy its API calls, they won't exist here.



TASK: these descriptions are provisional and written by someone who

hasn't read the code. Once you've read them, rewrite this section with

accurate descriptions of what each actually does and which patterns are

worth borrowing.



\## Roadmap

1\. CameraPlus (this project) — current focus.

2\. 3rdPov modifications — run with left click in third person, plus other

&#x20;  rebindable controls. Work from the compiled DLL. Don't start until

&#x20;  CameraPlus works.

3\. ChatSVS/AIPersonalities — LLM-driven dialogue with optional TTS and

&#x20;  lip-sync. Much larger. Don't start.

