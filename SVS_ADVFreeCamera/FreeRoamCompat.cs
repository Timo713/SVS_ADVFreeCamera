#nullable enable
using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Keeps SVS_FreeRoam off the left stick while this plugin is panning with it.
	///
	/// In a conversation SVS_FreeRoam lets a firm push of the left stick stand in
	/// for the D-pad, to move between the buttons on screen. With the free camera
	/// up that same push pans the camera, so every pan would also start picking
	/// buttons. Its stick reading goes through one method,
	/// GamepadUI.NavDirection(bool useStick); turning that argument off leaves the
	/// D-pad itself working, so choices can still be picked with it.
	///
	/// Found by name rather than referenced, as with ScreenshotExpansionCompat,
	/// and nothing happens at all when SVS_FreeRoam is not installed.
	/// </summary>
	internal static class FreeRoamCompat
	{
		public const string GUID = "SVS_FreeRoam";
		private const string TYPE = "SVS_FreeRoam.GamepadUI";
		private const string METHOD = "NavDirection";

		public static void Apply()
		{
			if (IL2CPPChainloader.Instance.Plugins.ContainsKey(GUID) == false) return;

			try
			{
				Type? type = AccessTools.TypeByName(TYPE);
				MethodInfo? method = type != null ? AccessTools.Method(type, METHOD) : null;

				if (method == null)
				{
					//Only if a later version reorganises its code - say so rather than fail silently
					Logging.LogWarning("SVS_FreeRoam found, but not its stick navigation. The left stick may pick buttons while it pans");
					return;
				}

				new Harmony(ADVFreeCameraPlugin.GUID + ".FreeRoam").Patch(method,
					prefix: new HarmonyMethod(typeof(FreeRoamCompat), nameof(LeaveStickToCamera)));

				Logging.LogInfo("SVS_FreeRoam found: its left stick navigation waits while the free camera is using the stick");
			}
			catch (Exception exception)
			{
				//Never let another plugin take this one down with it
				Logging.LogWarning($"Could not set up SVS_FreeRoam compatibility: {exception.Message}");
			}
		}

		/// <summary>
		/// The parameter has to carry the same name as the original's for Harmony to
		/// hand it over.
		/// </summary>
		private static void LeaveStickToCamera(ref bool useStick)
		{
			if (ADVFreeCameraComponent.GamepadCameraInUse) useStick = false;
		}
	}
}
