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
		public const string VERSION = "1.0.0";

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
		public static ConfigEntry<float> rotateSpeed = null!;
		public static ConfigEntry<float> zoomSpeed = null!;
		public static ConfigEntry<float> moveSpeed = null!;
		public static ConfigEntry<WheelFunction> wheelFunction = null!;
		public static ConfigEntry<MiddleButtonFunction> middleButtonFunction = null!;
		public static ConfigEntry<bool> blockClickAdvance = null!;

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
				new ConfigDescription("Master switch for every feature of this plugin.", null, Order(100)));
			toggleKey = Config.Bind(CATEGORY_GENERAL, "Toggle Free Camera", KeyCode.F,
				new ConfigDescription("Switches the free camera on and off during a conversation.", null, Order(90)));


			//Camera
			freeCameraType = Config.Bind(CATEGORY_CAMERA, "Camera Type", CameraType.CameraType1,
				new ConfigDescription(
					"CameraType1 is safe: the dialogue keeps working, so lines can be advanced while looking "
					+ "around. Panning moves along a frame fixed when the camera came on rather than following "
					+ "the view. CameraType2 pans correctly, following the view, but the dialogue UI stops "
					+ "responding until you switch back, because the scenario driver has to be switched off.",
					null, Order(100)));
			initialState = Config.Bind(CATEGORY_CAMERA, "Initial Camera", InitialCamera.DefaultCamera,
				new ConfigDescription("How each conversation starts. DefaultCamera leaves the game exactly as it "
				+ "would be without this plugin until the toggle key is pressed. CameraType1 brings the free "
				+ "camera up as soon as a character is actually in shot.", null, Order(90)));
			blockClickAdvance = Config.Bind(CATEGORY_CAMERA, "Left Click Does Not Advance", false,
				new ConfigDescription("While the free camera is on, stop left click advancing the dialogue, so the "
				+ "camera can be dragged freely. Choices are still selectable. Advance with the mouse wheel or "
				+ "keyboard instead.", null, Order(75)));
			silenceRepeatedVoice = Config.Bind(CATEGORY_CAMERA, "Silence Repeated Voice", true,
				new ConfigDescription("Extra safety net for the voice line that restarts when switching camera "
					+ "types. Normally unnecessary, since the line number is restored instead.", null, Order(70)));
			rotateSpeed = Config.Bind(CATEGORY_CAMERA, "Rotate Speed", 2f,
				new ConfigDescription("Character creation uses 2.", new AcceptableValueRange<float>(0.1f, 10f), Order(60)));
			zoomSpeed = Config.Bind(CATEGORY_CAMERA, "Zoom Speed", 2f,
				new ConfigDescription("Mouse wheel zoom. The right-drag dolly is governed by Move Speed.",
					new AcceptableValueRange<float>(0.1f, 10f), Order(50)));
			moveSpeed = Config.Bind(CATEGORY_CAMERA, "Move Speed", 0.05f,
				new ConfigDescription("Panning and right-drag dolly speed. Character creation uses 0.05.",
					new AcceptableValueRange<float>(0.01f, 1f), Order(40)));

			wheelFunction = Config.Bind(CATEGORY_CAMERA, "Mouse Wheel Function", WheelFunction.Zoom,
				new ConfigDescription("What the wheel does while the free camera is on. Rotating with the wheel "
				+ "avoids holding a mouse button, which also advances the dialogue.", null, Order(35)));
			middleButtonFunction = Config.Bind(CATEGORY_CAMERA, "Middle Button Function", MiddleButtonFunction.Pan,
				new ConfigDescription("What holding the middle button does. Rotate lets the camera be turned "
				+ "without left click, which is the only button that advances the dialogue.", null, Order(34)));

			//Framing
			focusMode = Config.Bind(CATEGORY_FRAMING, "Focus Point", FocusMode.KeepCameraPosition,
				new ConfigDescription(
					"KeepCameraPosition leaves the view exactly as the scene framed it, so switching to the free "
					+ "camera changes nothing until you move. Character centres on whoever is being talked to. "
					+ "RememberLastPosition resumes wherever you left the camera.", null, Order(100)));
			focusBone = Config.Bind(CATEGORY_FRAMING, "Character Focus Bone", FocusBone.Neck,
				new ConfigDescription("ONLY APPLIES WHEN FOCUS POINT IS SET TO CHARACTER. Which bone the camera centres on, "
				+ "from Head down to Hips. Tracks the character as they move, so it needs no assumption about "
				+ "how tall they are.",
				null, Order(95)));
			cameraDistance = Config.Bind(CATEGORY_FRAMING, "Camera Distance", 1.04f,
				new ConfigDescription("How far the camera sits from the point it orbits. Lower is closer in. 1.04 matches the "
					+ "framing the game uses, and applies to the default camera as well.",
					new AcceptableValueRange<float>(0.05f, 3f), Order(90)));
			focusHeightOffset = Config.Bind(CATEGORY_FRAMING, "Height Offset", 0f,
				new ConfigDescription("Raise or lower the point the camera aims at, in metres. Useful range is about -1.2 to 0.8; "
					+ "the slider goes wider so larger values can still be typed. Applies to the default camera too.", new AcceptableValueRange<float>(-1.5f, 1.5f), Order(80)));
			focusDepthOffset = Config.Bind(CATEGORY_FRAMING, "Depth Offset", 0f,
				new ConfigDescription("Push the aim point past the character (positive) or nearer (negative), in metres. Useful "
					+ "range is about -2.0 to 0.8; the slider goes wider so larger values can still be typed. "
					+ "Applies to the default camera too.", new AcceptableValueRange<float>(-1.5f, 1.5f), Order(70)));
			angleOffset = Config.Bind(CATEGORY_FRAMING, "Tilt Offset", 0f,
				new ConfigDescription("Tilt the camera down (positive) or up (negative), in degrees.",
				new AcceptableValueRange<float>(-120f, 60f), Order(68)));
			offsetsAffectDefault = Config.Bind(CATEGORY_FRAMING, "Offsets Affect Default Camera", false,
				new ConfigDescription("The offsets always apply to every free camera. Turn this on to have them "
				+ "affect the default camera as well.", null, Order(66)));
			showCrosshair = Config.Bind(CATEGORY_FRAMING, "Show Crosshair", false,
				new ConfigDescription("Mark the point the camera orbits while you are moving it, as character creation does.",
					null, Order(60)));

			//Debug
			showOverlay = Config.Bind(CATEGORY_DEBUG, "Show Status Overlay", false,
				new ConfigDescription("Draw plugin status and recent messages on screen.", null, Order(100)));
			showReadout = Config.Bind(CATEGORY_DEBUG, "Show Camera Readout", false,
				new ConfigDescription("Show live camera position, angles, field of view and orbit radius. "
					+ "Needs Show Status Overlay.", null, Order(90)));
			overlaySeconds = Config.Bind(CATEGORY_DEBUG, "Overlay Message Seconds", 6f,
				new ConfigDescription("How long each on-screen message stays before fading out.",
					new AcceptableValueRange<float>(1f, 30f), Order(80)));
			logEnabled = Config.Bind(CATEGORY_DEBUG, "Enable Logging", true,
				new ConfigDescription("Write plugin messages to LogOutput.log. Turn on when reporting a "
					+ "problem.", null, Order(70)));
			logLevel = Config.Bind(CATEGORY_DEBUG, "Log Levels",
				LogLevel.Info | LogLevel.Message | LogLevel.Warning | LogLevel.Error | LogLevel.Fatal,
				new ConfigDescription("Which message kinds to keep.", null, Order(60)));

			//Create the component that watches for conversations. It lives on the
			//BepInEx manager object, so it survives every scene change.
			GetOrAddComponent();

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
