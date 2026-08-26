#nullable enable
using UnityEngine; //Add UnityEngine dependencies from e.g. NuGet
using HarmonyLib;

using Logging = BepInEx_Il2CppPlugin.Plugin.Logging; //Change "BepInEx_Il2CppPlugin.Plugin.Logging" to your plugin namespace and class name


//Change namespace and class names to avoid naming conflicts
namespace BepInEx_Il2CppPlugin
{
	public class Component : MonoBehaviour
	{
		/*VARIABLES*/
		//Instance
		public static Component? Instance { get; private set; }



		/*INITIALIZATION*/
		private void Start()
		{
			//Initialize component
		}

		private void OnDestroy()
		{
			//Clear instance
			if (Instance == this)
			{
				Instance = null;
			}
		}

		private void Awake()
		{
			//Set instance
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				//Call Destroy() on component, not GameObject
				Destroy(this);
			}
		}



		/*COMPONENT SPECIFIC HOOKS*/
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
	}
}