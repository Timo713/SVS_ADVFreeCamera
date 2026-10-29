#nullable enable
using System;
using System.Runtime.InteropServices;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// The Windows cursor position, read and set directly. Holding the cursor still
	/// while a drag moves the camera is done the way the game's own GameCursor does
	/// it: put it back where the press began, every frame.
	///
	/// Kept out of ADVFreeCameraComponent, which is a class registered with the
	/// game's runtime, where native declarations do not belong.
	/// </summary>
	internal static class NativeCursor
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct Point
		{
			public int X;
			public int Y;
		}

		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out Point point);

		[DllImport("user32.dll")]
		private static extern bool SetCursorPos(int x, int y);

		private static bool _failed;

		/// <summary>Where the cursor is on screen, or false if it cannot be read.</summary>
		internal static bool TryGet(out int x, out int y)
		{
			x = 0;
			y = 0;
			if (_failed) return false;

			try
			{
				if (GetCursorPos(out Point point) == false) return false;
				x = point.X;
				y = point.Y;
				return true;
			}
			catch (Exception)
			{
				_failed = true;
				return false;
			}
		}

		internal static void Set(int x, int y)
		{
			if (_failed) return;

			try
			{
				SetCursorPos(x, y);
			}
			catch (Exception)
			{
				_failed = true;
			}
		}
	}
}
