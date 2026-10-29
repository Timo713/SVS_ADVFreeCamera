#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using ILLGames.Unity;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Panning that follows the view, with the dialogue still running.
	///
	/// The controller pans along CameraController.GetTransformDirection, which reads
	/// the camera's own transform at that moment (read from the game code with
	/// isil.ps1). The scenario has just put that transform back to the scene's
	/// original angle, so a pan used to move along the angle the scene started at
	/// rather than the one on screen. Turning the camera to the angle the controller
	/// is showing just before it reads its mouse fixes that (CONFIRMED 2026-10-26).
	/// The old way round it switched the dialogue driver off and hid the text.
	/// </summary>
	internal static class PanFix
	{
		public static void Apply()
		{
			try
			{
				MethodInfo? method = AccessTools.Method(typeof(CameraController), nameof(CameraController.InputMouseProc));
				if (method == null)
				{
					Logging.LogWarning("Pan fix: CameraController.InputMouseProc not found");
					return;
				}

				new Harmony(ADVFreeCameraPlugin.GUID + ".PanFix").Patch(method,
					prefix: new HarmonyMethod(typeof(PanFix), nameof(BeforeMouse)));
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Pan fix could not be set up: {exception.Message}");
			}
		}

		private static void BeforeMouse(CameraController __instance)
		{
			ADVFreeCameraComponent.AlignForPan(__instance);
		}
	}
}
