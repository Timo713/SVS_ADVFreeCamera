#nullable enable
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;

using LogLevel = BepInEx.Logging.LogLevel;


namespace SVS_ADVFreeCamera
{
	[BepInProcess(PROCESS_NAME)]
	[BepInPlugin(GUID, PLUGIN_NAME, VERSION)]
	public class ADVFreeCameraPlugin : BasePlugin
	{
		#region PLUGIN_INFO

		/*PLUGIN INFO*/
		public const string PLUGIN_NAME = "SVS_ADVFreeCamera";
		public const string COPYRIGHT = "Copyright ©  2026";
		public const string COMPANY = "timo713";

		public const string PROCESS_NAME = "SamabakeScramble";
		public const string GUID = "SVS_ADVFreeCamera";
		public const string VERSION = "1.2.0";

		#endregion



		/*VARIABLES*/
		public static ADVFreeCameraPlugin Instance { get; private set; } = null!;
		private static ManualLogSource _log = null!;



		/*METHODS*/
		public static bool TryGetComponent([MaybeNullWhen(false)] out ADVFreeCameraComponent component)
		{
			component = ADVFreeCameraComponent.Instance;
			return component != null;
		}

		public static ADVFreeCameraComponent GetOrAddComponent()
		{
			ADVFreeCameraComponent? component = ADVFreeCameraComponent.Instance;
			return component != null ? component : Instance.AddComponent<ADVFreeCameraComponent>();
		}

		/// <summary>Sort weight for ConfigurationManager. Higher shows first.</summary>
		private static ConfigurationManagerAttributes Order(int order)
		{
			return new ConfigurationManagerAttributes { Order = order };
		}



		/*CONFIG*/
		public const string CATEGORY_GENERAL = "General";
		public const string CATEGORY_CAMERA = "Camera";
		public const string CATEGORY_FRAMING = "Framing";
		public const string CATEGORY_GAMEPAD = "Gamepad";
		public const string CATEGORY_DEBUG = "Debug";

		//General
		public static ConfigEntry<bool> enabled = null!;
		public static ConfigEntry<KeyCode> toggleKey = null!;

		//Camera
		public static ConfigEntry<InitialCamera> initialState = null!;
		public static ConfigEntry<bool> invertX = null!;
		public static ConfigEntry<bool> invertY = null!;
		public static ConfigEntry<float> rotateSpeed = null!;
		public static ConfigEntry<float> zoomSpeed = null!;
		public static ConfigEntry<float> moveSpeed = null!;
		public static ConfigEntry<WheelFunction> wheelFunction = null!;
		public static ConfigEntry<bool> gameUsesWheel = null!;
		public static ConfigEntry<MiddleButtonFunction> middleButtonFunction = null!;

		//Gamepad
		public static ConfigEntry<bool> gamepadCamera = null!;
		public static ConfigEntry<KeyCode> gamepadToggleKey = null!;
		public static ConfigEntry<float> gamepadTurnSpeed = null!;
		public static ConfigEntry<float> gamepadMoveSpeed = null!;
		public static ConfigEntry<ClickAdvance> clickAdvance = null!;
		public static ConfigEntry<float> tapSeconds = null!;
		public static ConfigEntry<float> tapPixels = null!;

		//Framing
		public static ConfigEntry<FocusMode> focusMode = null!;
		public static ConfigEntry<FocusBone> focusBone = null!;
		public static ConfigEntry<GroupFraming> groupFraming = null!;
		public static ConfigEntry<bool> followCharacter = null!;
		public static ConfigEntry<float> cameraDistance = null!;
		public static ConfigEntry<float> focusHeightOffset = null!;
		public static ConfigEntry<float> focusDepthOffset = null!;
		public static ConfigEntry<float> angleOffset = null!;
		public static ConfigEntry<bool> offsetsAffectDefault = null!;
		public static ConfigEntry<bool> showCrosshair = null!;

		//Debug
		public static ConfigEntry<bool> showOverlay = null!;
		public static ConfigEntry<bool> showReadout = null!;
		public static ConfigEntry<bool> showTextArea = null!;
		public static ConfigEntry<float> overlaySeconds = null!;
		public static ConfigEntry<bool> logEnabled = null!;
		public static ConfigEntry<LogLevel> logLevel = null!;



		/*PLUGIN LOAD*/
		public override void Load()
		{
			Instance = this;
			_log = Log;

			//General
			enabled = Config.Bind(CATEGORY_GENERAL, "Enabled", true,
				new ConfigDescription("Turn the whole plugin on or off.", null, Order(100)));
			toggleKey = Config.Bind(CATEGORY_GENERAL, "Toggle Free Camera", KeyCode.F,
				new ConfigDescription("Key that turns the free camera on and off during a conversation.",
					null, Order(90)));


			//Camera
			initialState = Config.Bind(CATEGORY_CAMERA, "Initial Camera", InitialCamera.FreeCamera,
				new ConfigDescription("Whether a conversation starts on the free camera or on the game's own.",
					null, Order(90)));
			clickAdvance = Config.Bind(CATEGORY_CAMERA, "Left Click Advance", ClickAdvance.TapOnly,
				new ConfigDescription("How left click advances the dialogue while the free camera is on. Normal "
				+ "leaves it alone, Never stops it, TextAreaOnly advances only from the message frame, TapOnly "
				+ "advances on a quick tap. Choices stay clickable.", null, Order(75)));
			tapSeconds = Config.Bind(CATEGORY_CAMERA, "Tap Length", 0.2f,
				new ConfigDescription("How long left click can be held and still count as a tap. TapOnly only.",
					new AcceptableValueRange<float>(0.05f, 1f), Order(74)));
			tapPixels = Config.Bind(CATEGORY_CAMERA, "Tap Drag Allowance", 10f,
				new ConfigDescription("How far the mouse can move during a tap. TapOnly only.",
					new AcceptableValueRange<float>(0f, 100f), Order(73)));
			invertX = Config.Bind(CATEGORY_CAMERA, "Invert X", false,
				new ConfigDescription("Reverse horizontal look, mouse and right stick. Mouse panning reverses only when both Invert "
				+ "boxes are on.", null, Order(66)));
			invertY = Config.Bind(CATEGORY_CAMERA, "Invert Y", false,
				new ConfigDescription("Reverse vertical look, mouse and right stick. Mouse panning reverses only when both Invert "
				+ "boxes are on.", null, Order(65)));
			rotateSpeed = Config.Bind(CATEGORY_CAMERA, "Rotate Speed", 2f,
				new ConfigDescription("How fast the camera turns. Character creation uses 2.",
					new AcceptableValueRange<float>(0.1f, 10f), Order(60)));
			zoomSpeed = Config.Bind(CATEGORY_CAMERA, "Zoom Speed", 2f,
				new ConfigDescription("How fast the mouse wheel zooms.",
					new AcceptableValueRange<float>(0.1f, 10f), Order(50)));
			moveSpeed = Config.Bind(CATEGORY_CAMERA, "Move Speed", 0.05f,
				new ConfigDescription("How fast the camera pans. Character creation uses 0.05.",
					new AcceptableValueRange<float>(0.01f, 1f), Order(40)));
			wheelFunction = Config.Bind(CATEGORY_CAMERA, "Mouse Wheel Function", WheelFunction.Zoom,
				new ConfigDescription("What the mouse wheel does while the free camera is on.", null, Order(35)));
			gameUsesWheel = Config.Bind(CATEGORY_CAMERA, "Game Uses Wheel During Text", true,
				new ConfigDescription("While text or choices are on screen the wheel works as in the game: "
				+ "scrolling down advances the text, and the camera does not zoom. Off: the wheel is always "
				+ "the camera's.", null, Order(34)));
			middleButtonFunction = Config.Bind(CATEGORY_CAMERA, "Middle Button Function", MiddleButtonFunction.Pan,
				new ConfigDescription("What holding the middle mouse button does.", null, Order(33)));


			//Gamepad
			gamepadCamera = Config.Bind(CATEGORY_GAMEPAD, "Gamepad Camera", true,
				new ConfigDescription("Move the free camera with a controller: left stick pans, right stick "
				+ "turns, right trigger moves in and left trigger moves out.", null, Order(100)));
			gamepadToggleKey = Config.Bind(CATEGORY_GAMEPAD, "Toggle Button", KeyCode.JoystickButton6,
				new ConfigDescription("The controller button that turns the free camera on and off. "
				+ "JoystickButton6 is Back / View / Select.", null, Order(95)));
			gamepadTurnSpeed = Config.Bind(CATEGORY_GAMEPAD, "Turn Speed", 1f,
				new ConfigDescription("How fast the right stick turns the camera.",
					new AcceptableValueRange<float>(0.1f, 5f), Order(90)));
			gamepadMoveSpeed = Config.Bind(CATEGORY_GAMEPAD, "Move Speed", 1f,
				new ConfigDescription("How fast the left stick pans and the triggers move the camera.",
					new AcceptableValueRange<float>(0.1f, 5f), Order(80)));


			//Framing
			focusMode = Config.Bind(CATEGORY_FRAMING, "Focus Point", FocusMode.KeepCameraPosition,
				new ConfigDescription("What the camera aims at. KeepCameraPosition leaves the shot as the game "
				+ "framed it, Character centres on whoever is talking, RememberLastPosition resumes where you "
				+ "left the camera.", null, Order(100)));
			focusBone = Config.Bind(CATEGORY_FRAMING, "Character Focus Bone", FocusBone.Neck,
				new ConfigDescription("Which part of the character to centre on. Only used when Focus Point is "
				+ "Character.", null, Order(95)));
			groupFraming = Config.Bind(CATEGORY_FRAMING, "Several Characters", GroupFraming.Speaker,
				new ConfigDescription("Who the camera centres on when two or three people are on screen "
				+ "(Focus Point on Character). Speaker: whoever is talking, moving to the next one. "
				+ "AllCharacters: the game's shot of the whole group, at their average height. "
				+ "MostFavoured: the one you like most.", null, Order(94)));
			followCharacter = Config.Bind(CATEGORY_FRAMING, "Follow Character", true,
				new ConfigDescription("Keep the camera on the character as they move. Pauses while you move "
				+ "the camera, and carries on from where you left it when you let go.", null, Order(93)));
			cameraDistance = Config.Bind(CATEGORY_FRAMING, "Camera Distance", 1.04f,
				new ConfigDescription("How far the camera sits from what it looks at. 1.04 matches the game.",
					new AcceptableValueRange<float>(0.05f, 3f), Order(90)));
			focusHeightOffset = Config.Bind(CATEGORY_FRAMING, "Height Offset", 0f,
				new ConfigDescription("Raise or lower the point the camera aims at, in metres.",
					new AcceptableValueRange<float>(-1.5f, 1.5f), Order(80)));
			focusDepthOffset = Config.Bind(CATEGORY_FRAMING, "Depth Offset", 0f,
				new ConfigDescription("Move the aim point further away (positive) or nearer (negative), in metres.",
					new AcceptableValueRange<float>(-1.5f, 1.5f), Order(70)));
			angleOffset = Config.Bind(CATEGORY_FRAMING, "Tilt Offset", 0f,
				new ConfigDescription("Tilt the camera down (positive) or up (negative), in degrees.",
					new AcceptableValueRange<float>(-120f, 60f), Order(68)));
			offsetsAffectDefault = Config.Bind(CATEGORY_FRAMING, "Offsets Affect Default Camera", false,
				new ConfigDescription("Apply the offsets to the normal conversation camera as well.",
					null, Order(66)));
			showCrosshair = Config.Bind(CATEGORY_FRAMING, "Show Crosshair", false,
				new ConfigDescription("Mark the point the camera orbits while you move it.", null, Order(60)));


			//Debug
			showOverlay = Config.Bind(CATEGORY_DEBUG, "Show Status Overlay", false,
				new ConfigDescription("Show plugin status and messages on screen.", null, Order(100)));
			showReadout = Config.Bind(CATEGORY_DEBUG, "Show Camera Readout", false,
				new ConfigDescription("Show camera and click details. Needs Show Status Overlay.", null, Order(90)));
			showTextArea = Config.Bind(CATEGORY_DEBUG, "Show Text Area", false,
				new ConfigDescription("Outline the message frame on screen.", null, Order(85)));
			overlaySeconds = Config.Bind(CATEGORY_DEBUG, "Overlay Message Seconds", 6f,
				new ConfigDescription("How long each message stays on screen.",
					new AcceptableValueRange<float>(1f, 30f), Order(80)));
			logEnabled = Config.Bind(CATEGORY_DEBUG, "Enable Logging", true,
				new ConfigDescription("Write plugin messages to LogOutput.log.", null, Order(70)));
			logLevel = Config.Bind(CATEGORY_DEBUG, "Log Levels",
				LogLevel.Info | LogLevel.Message | LogLevel.Warning | LogLevel.Error | LogLevel.Fatal,
				new ConfigDescription("Which messages to write. Add Debug for the details of each "
				+ "conversation: who is on stage, who the camera centres on, where it starts.", null, Order(60)));


			//Create the component that watches for conversations. It lives on the
			//BepInEx manager object, so it survives every scene change.
			GetOrAddComponent();

			//Other plugins that drive the conversation camera. Looked for once every
			//plugin has loaded rather than declared as dependencies: neither is needed,
			//and mod managers list a declared one as a requirement.
			IL2CPPChainloader.Instance.Finished += () =>
			{
				ScreenshotExpansionCompat.Apply();
				FreeRoamCompat.Apply();
			};
			PanFix.Apply();
			PauseFix.Apply();


			Logging.LogInfo($"{PLUGIN_NAME} {VERSION} loaded");
		}



		#region LOGGING

		/*LOGGING*/
		public static class Logging
		{
			public static void LogFatal(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Fatal) != 0)
				{
					_log.LogFatal(message);
					ADVFreeCameraComponent.Notify(message, Color.red);
				}
			}

			public static void LogError(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Error) != 0)
				{
					_log.LogError(message);
					ADVFreeCameraComponent.Notify(message, Color.red);
				}
			}

			public static void LogWarning(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Warning) != 0)
				{
					_log.LogWarning(message);
					ADVFreeCameraComponent.Notify(message, Color.yellow);
				}
			}

			public static void LogMessage(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Message) != 0)
				{
					_log.LogMessage(message);
					ADVFreeCameraComponent.Notify(message, Color.white);
				}
			}

			public static void LogInfo(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Info) != 0)
				{
					_log.LogInfo(message);
					ADVFreeCameraComponent.Notify(message, Color.white);
				}
			}

			//Written as Info: BepInEx leaves Debug out of LogOutput.log by default, and
			//adding Debug to Log Levels here should be all a player has to do
			public static void LogDebug(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Debug) != 0)
				{
					_log.LogInfo(message);
					ADVFreeCameraComponent.Notify(message, Color.white);
				}
			}
		}

		#endregion
	}
}
