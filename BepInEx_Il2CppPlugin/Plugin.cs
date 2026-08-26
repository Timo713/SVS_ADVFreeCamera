#nullable enable
using System.Diagnostics.CodeAnalysis;
using BepInEx; //Add BepInEx dependencies from e.g. NuGet
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using LogLevel = BepInEx.Logging.LogLevel;


//Change namespace and class names to avoid naming conflicts
namespace BepInEx_Il2CppPlugin
{
	[BepInProcess(PROCESS_NAME)]
	[BepInPlugin(GUID, PLUGIN_NAME, VERSION)]
	public class Plugin : BasePlugin
	{
		#region PLUGIN_INFO

		/*PLUGIN INFO*/
		public const string PLUGIN_NAME = "PluginName";
		public const string COPYRIGHT = "Copyright ©  2026";
		public const string COMPANY = "LinkOrCompanyName";

		public const string PROCESS_NAME = "ProcessName";
		public const string GUID = "org.PluginGUID";
		public const string VERSION = "1.0.0";

		#endregion



		/*VARIABLES*/
		//Instance
		public static Plugin Instance { get; private set; } = null!;
		private static ManualLogSource _log = null!;



		/*METHODS*/
		public static bool TryGetComponent([MaybeNullWhen(false)] out Component component)
		{
			component = Component.Instance;
			return component != null;
		}

		public static Component GetOrAddComponent()
		{
			Component? component = Component.Instance;

			if (component != null)
			{
				return component;
			}
			else
			{
				return Instance.AddComponent<Component>();
			}
		}



		/*CONFIG*/
		//Category
		public const string CATEGORY_GENERAL = "General";
		public const string CATEGORY_DEBUG = "Debug";

		//General
		public static ConfigEntry<bool> enabled = null!;

		//Debug
		public static ConfigEntry<bool> logEnabled = null!;
		public static ConfigEntry<LogLevel> logLevel = null!;



		/*PLUGIN LOAD*/
		public override void Load()
		{
			//Set instance
			Instance = this;
			_log = Log;

			//General
			enabled = Config.Bind(CATEGORY_GENERAL, "Enabled", true);

			//Debug
			logEnabled = Config.Bind(CATEGORY_DEBUG, "Enable Logging", false);
            logLevel = Config.Bind(CATEGORY_DEBUG, "Log Levels", LogLevel.Info | LogLevel.Message | LogLevel.Warning | LogLevel.Error | LogLevel.Fatal);

            //Create hooks
            Harmony.CreateAndPatchAll(typeof(Plugin.Hooks), GUID);
			Harmony.CreateAndPatchAll(typeof(Component.Hooks), GUID);
		}



		/*PLUGIN SPECIFIC HOOKS*/
		public static class Hooks
		{
			/* Template

			//Prefix
			[HarmonyPrefix]
			[HarmonyPatch(typeof(), nameof())]
			public static void Pre()
			{
				
			}

			[HarmonyPrefix]
			[HarmonyPatch(typeof(), nameof(), new Type[] { })]
			public static void Pre()
			{
				
			}

			//Postfix
			[HarmonyPostfix]
			[HarmonyPatch(typeof(), nameof())]
			public static void Post()
			{
				
			}

			[HarmonyPostfix]
			[HarmonyPatch(typeof(), nameof(), new Type[] { })]
			public static void Post()
			{
				
			}

			*/
		}



		#region LOGGING

		/*LOGGING*/
		public static class Logging
		{
			public static void Log(LogLevel level, string message)
			{
				if (logEnabled.Value && level != LogLevel.None && (logLevel.Value & level) != 0)
				{
					_log.Log(level, message);
				}
			}

			public static void LogFatal(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Fatal) != 0)
				{
					_log.LogFatal(message);
				}
			}

			public static void LogError(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Error) != 0)
				{
					_log.LogError(message);
				}
			}

			public static void LogWarning(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Warning) != 0)
				{
					_log.LogWarning(message);
				}
			}

			public static void LogMessage(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Message) != 0)
				{
					_log.LogMessage(message);
				}
			}

			public static void LogInfo(string message)
			{
				if (logEnabled.Value && (logLevel.Value & LogLevel.Info) != 0)
				{
					_log.LogInfo(message);
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