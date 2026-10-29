#nullable enable
using System;
using ILLGames.Unity.Component;
using Manager;
using SV;
using UnityEngine;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;

namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Hides the blur over the background and the black bars of the conversation
	/// view. Both are children of the game's HighPolyBackGroundFrame: "Panel" is the
	/// blur, "UpFrame" and "DownFrame" the bars. They stay active and the frame's
	/// animator shows and hides them, so switching them off is all it takes (as
	/// SVS_CheatTools and SVS_3DRooms do). The same blur sits behind menus such as
	/// save and load, so it is only hidden during a conversation and put back after.
	/// </summary>
	internal static class BackgroundFrame
	{
		private static HighPolyBackGroundFrame? _frame;
		private static GameObject? _blur;
		private static GameObject? _upFrame;
		private static GameObject? _downFrame;
		private static float _nextSearch;
		private static bool _blurHidden;
		private static bool _barsHidden;

		/// <summary>Each frame, after the animator: hide what the settings ask for while in a conversation.</summary>
		public static void Apply(bool inConversation)
		{
			bool hideBlur = inConversation && ADVFreeCameraPlugin.showBlur.Value == false;
			bool hideBars = inConversation && ADVFreeCameraPlugin.showFrame.Value == false;

			//Nothing to hide and nothing to put back
			if (hideBlur == false && hideBars == false && _blurHidden == false && _barsHidden == false) return;

			if (Find() == false) return;

			if (hideBlur != _blurHidden)
			{
				_blurHidden = hideBlur;
				Logging.LogDebug(hideBlur ? "Background blur hidden" : "Background blur back");
			}

			if (hideBars != _barsHidden)
			{
				_barsHidden = hideBars;
				Logging.LogDebug(hideBars ? "Black bars hidden" : "Black bars back");
			}

			SetActive(_blur, hideBlur == false);
			SetActive(_upFrame, hideBars == false);
			SetActive(_downFrame, hideBars == false);
		}

		private static void SetActive(GameObject? target, bool active)
		{
			if (target != null && target.activeSelf != active) target.SetActive(active);
		}

		private static bool Find()
		{
			if (_frame != null) return true;

			//At most once a second while there is nothing to find
			if (Time.unscaledTime < _nextSearch) return false;
			_nextSearch = Time.unscaledTime + 1f;

			try
			{
				Game? game = SingletonInitializer<Game>.Instance;
				if (game == null) return false;

				HighPolyBackGroundFrame? frame = game.transform.GetComponentInChildren<HighPolyBackGroundFrame>(true);
				if (frame == null || frame.animFrame == null) return false;

				Transform root = frame.animFrame.transform;
				_blur = root.Find("Panel")?.gameObject;
				_upFrame = root.Find("UpFrame")?.gameObject;
				_downFrame = root.Find("DownFrame")?.gameObject;
				_frame = frame;

				if (_blur == null || _upFrame == null || _downFrame == null)
				{
					Logging.LogWarning("Background blur or black bars not found, they cannot be hidden");
				}
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Could not find the background blur and black bars: {exception.Message}");
			}

			return _frame != null;
		}
	}
}
