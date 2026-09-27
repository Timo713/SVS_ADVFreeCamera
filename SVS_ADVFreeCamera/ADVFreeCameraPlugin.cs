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
	//Soft: not needed, only loaded first when present so its camera can be switched off
	[BepInDependency(ScreenshotExpansionCompat.GUID, BepInDependency.DependencyFlags.SoftDependency)]
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
		public const string VERSION = "1.1.1";

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
		public const string CATEGORY_DEBUG = "Debug";

		//General
		public static ConfigEntry<bool> enabled = null!;
		public static ConfigEntry<KeyCode> toggleKey = null!;

		//Camera
		public static ConfigEntry<CameraType> freeCameraType = null!;
		public static ConfigEntry<InitialCamera> initialState = null!;
		public static ConfigEntry<bool> silenceRepeatedVoice = null!;
		public static ConfigEntry<bool> invertX = null!;
		public static ConfigEntry<bool> invertY = null!;
		public static ConfigEntry<float> rotateSpeed = null!;
		public static ConfigEntry<float> zoomSpeed = null!;
		public static ConfigEntry<float> moveSpeed = null!;
		public static ConfigEntry<WheelFunction> wheelFunction = null!;
		public static ConfigEntry<MiddleButtonFunction> middleButtonFunction = null!;
		public static ConfigEntry<ClickAdvance> clickAdvance = null!;
		public static ConfigEntry<float> tapSeconds = null!;
		public static ConfigEntry<float> tapPixels = null!;

		//Framing
		public static ConfigEntry<FocusMode> focusMode = null!;
		public static ConfigEntry<FocusBone> focusBone = null!;
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
			freeCameraType = Config.Bind(CATEGORY_CAMERA, "Camera Type", CameraType.CameraType1,
				new ConfigDescription("CameraType1 keeps the dialogue working, but panning does not follow the "
				+ "view. CameraType2 pans properly and hides the dialogue until you switch back.",
					null, Order(100)));
			initialState = Config.Bind(CATEGORY_CAMERA, "Initial Camera", InitialCamera.CameraType1,
				new ConfigDescription("Which camera a conversation starts on.", null, Order(90)));
			clickAdvance = Config.Bind(CATEGORY_CAMERA, "Left Click Advance", ClickAdvance.TapOnly,
				new ConfigDescription("How left click advances the dialogue while the free camera is on. Normal "
				+ "leaves it alone, Never stops it, TextAreaOnly advances only from the message frame, TapOnly "
				+ "advances on a quick tap. Choices stay clickable. CameraType1 only.", null, Order(75)));
			tapSeconds = Config.Bind(CATEGORY_CAMERA, "Tap Length", 0.2f,
				new ConfigDescription("How long left click can be held and still count as a tap. TapOnly only.",
					new AcceptableValueRange<float>(0.05f, 1f), Order(74)));
			tapPixels = Config.Bind(CATEGORY_CAMERA, "Tap Drag Allowance", 10f,
				new ConfigDescription("How far the mouse can move during a tap. TapOnly only.",
					new AcceptableValueRange<float>(0f, 100f), Order(73)));
			silenceRepeatedVoice = Config.Bind(CATEGORY_CAMERA, "Silence Repeated Voice", true,
				new ConfigDescription("Mute the voice line that repeats when switching camera type.",
					null, Order(70)));
			invertX = Config.Bind(CATEGORY_CAMERA, "Invert X", false,
				new ConfigDescription("Reverse horizontal mouse look. Panning reverses only when both Invert "
				+ "boxes are on.", null, Order(66)));
			invertY = Config.Bind(CATEGORY_CAMERA, "Invert Y", false,
				new ConfigDescription("Reverse vertical mouse look. Panning reverses only when both Invert "
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
			middleButtonFunction = Config.Bind(CATEGORY_CAMERA, "Middle Button Function", MiddleButtonFunction.Pan,
				new ConfigDescription("What holding the middle mouse button does.", null, Order(34)));


			//Framing
			focusMode = Config.Bind(CATEGORY_FRAMING, "Focus Point", FocusMode.KeepCameraPosition,
				new ConfigDescription("What the camera aims at. KeepCameraPosition leaves the shot as the game "
				+ "framed it, Character centres on whoever is talking, RememberLastPosition resumes where you "
				+ "left the camera.", null, Order(100)));
			focusBone = Config.Bind(CATEGORY_FRAMING, "Character Focus Bone", FocusBone.Neck,
				new ConfigDescription("Which part of the character to centre on. Only used when Focus Point is "
				+ "Character.", null, Order(95)));
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
				new ConfigDescription("Which messages to write.", null, Order(60)));


			//Create the component that watches for conversations. It lives on the
			//BepInEx manager object, so it survives every scene change.
			GetOrAddComponent();

			//Other plugins that drive the conversation camera
			ScreenshotExpansionCompat.Apply();


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

			public static void LogDebug(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Debug) != 0)
				{
					_log.LogDebug(message);
				}
			}
		}

		#endregion
	}
}
