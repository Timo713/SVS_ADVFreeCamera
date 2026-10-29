#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using ILLGames.Unity;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Keep the free camera where it is while the pause menu is open.
	///
	/// CameraController.LateUpdate returns before applying the camera when
	/// Manager.Scene.IsOverlap or IsNowLoadingFade is set (read with isil.ps1). The
	/// scenario resets the camera every frame and the controller normally puts it
	/// back, so with the controller idle the view fell back to the scene's framing.
	/// A postfix applies the camera on those frames (UNVERIFIED in game).
	/// </summary>
	internal static class PauseFix
	{
		public static void Apply()
		{
			try
			{
				MethodInfo? method = AccessTools.Method(typeof(CameraController), nameof(CameraController.LateUpdate));
				if (method == null)
				{
					Logging.LogWarning("Pause fix: CameraController.LateUpdate not found");
					return;
				}

				new Harmony(ADVFreeCameraPlugin.GUID + ".PauseFix").Patch(method,
					postfix: new HarmonyMethod(typeof(PauseFix), nameof(AfterLateUpdate)));
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Pause fix could not be set up: {exception.Message}");
			}
		}

		private static void AfterLateUpdate(CameraController __instance)
		{
			ADVFreeCameraComponent.HoldThroughOverlay(__instance);
		}
	}
}
