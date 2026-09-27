#nullable enable
using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Keeps SVS Screenshot Expansion's talk camera out of the way. It drives the
	/// same conversation camera this plugin does, so with both running they fight.
	/// Its screenshot features are left completely alone.
	///
	/// Every part of its camera - its hotkeys, its drag handling, and the state its
	/// LateUpdate later writes to the camera from - starts in one method,
	/// UpdateDialogueCamera. Skipping that method is enough: nothing ever sets that
	/// state, so the rest of its camera code finds nothing to do.
	///
	/// Found by name rather than referenced, so this plugin needs nothing from it
	/// and does nothing at all when it is not installed.
	/// </summary>
	internal static class ScreenshotExpansionCompat
	{
		public const string GUID = "com.korg.svs.ScreenshotExpansion";
		private const string TYPE = "SVS_ScreenshotEx.ScreenshotBehaviour";
		private const string METHOD = "UpdateDialogueCamera";

		/// <summary>
		/// Patch it if it is installed. Relies on the soft dependency on the plugin
		/// class, which makes BepInEx load it before this one, so it is already
		/// there to be found.
		/// </summary>
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
					Logging.LogWarning("SVS Screenshot Expansion found, but not its talk camera. The two may fight over the camera");
					return;
				}

				new Harmony(ADVFreeCameraPlugin.GUID).Patch(method,
					prefix: new HarmonyMethod(typeof(ScreenshotExpansionCompat), nameof(SkipTalkCamera)));

				Logging.LogInfo("SVS Screenshot Expansion found: its talk camera is off while this plugin is enabled, its screenshots are unaffected");
			}
			catch (Exception exception)
			{
				//Never let another plugin take this one down with it
				Logging.LogWarning($"Could not switch off SVS Screenshot Expansion's talk camera: {exception.Message}");
			}
		}

		/// <summary>
		/// Returning false skips the original. That works here because this is
		/// ordinary .NET code in another plugin - FINDINGS records prefix-skip being
		/// ignored, but only on the game's own IL2CPP methods.
		///
		/// With this plugin's Enabled setting off, theirs gets the camera back.
		/// </summary>
		private static bool SkipTalkCamera()
		{
			return ADVFreeCameraPlugin.enabled.Value == false;
		}
	}
}
