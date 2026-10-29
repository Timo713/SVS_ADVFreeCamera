#nullable enable
using System;
using System.Runtime.InteropServices;
using UnityEngine;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// Reads an Xbox-style controller straight from Windows (XInput). The same
	/// reader SVS_FreeRoam uses.
	///
	/// Unity cannot do it in this game. Only the legacy input system ships, and
	/// its settings name axes for the left stick and the D-pad and nothing for the
	/// right stick or the triggers. The left stick is read here as well, rather
	/// than through Unity's Horizontal and Vertical, because those also report the
	/// arrow keys and WASD.
	///
	/// XInput covers Xbox controllers and anything presented as one, which
	/// includes most pads under Steam Input.
	/// </summary>
	internal static class Gamepad
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct XInputGamepad
		{
			public ushort Buttons;
			public byte LeftTrigger;
			public byte RightTrigger;
			public short ThumbLX;
			public short ThumbLY;
			public short ThumbRX;
			public short ThumbRY;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XInputState
		{
			public uint PacketNumber;
			public XInputGamepad Gamepad;
		}

		[DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
		private static extern uint GetState14(uint userIndex, out XInputState state);

		[DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
		private static extern uint GetState910(uint userIndex, out XInputState state);

		private const uint SUCCESS = 0;

		//Microsoft's recommended dead zones, as a fraction of full tilt, and its
		//trigger threshold out of 255
		private const float LEFT_DEAD_ZONE = 7849f / 32767f;
		private const float RIGHT_DEAD_ZONE = 8689f / 32767f;
		private const float TRIGGER_THRESHOLD = 30f;

		private static int _library;            //0 untried, 1 = 1_4, 2 = 9_1_0, -1 none
		private static int _connected = -1;     //controller slot in use, or -1
		private static float _nextScan;
		private static int _readFrame = -1;
		private static Vector2 _leftStick;
		private static Vector2 _rightStick;
		private static float _leftTrigger;
		private static float _rightTrigger;

		/// <summary>
		/// Sticks with the dead zone removed and rescaled, so they start at 0 just
		/// past it and reach 1 at full tilt. Zero with no controller.
		/// </summary>
		internal static Vector2 LeftStick
		{
			get
			{
				Poll();
				return _leftStick;
			}
		}

		internal static Vector2 RightStick
		{
			get
			{
				Poll();
				return _rightStick;
			}
		}

		/// <summary>Triggers, 0 released to 1 fully pulled.</summary>
		internal static float LeftTrigger
		{
			get
			{
				Poll();
				return _leftTrigger;
			}
		}

		internal static float RightTrigger
		{
			get
			{
				Poll();
				return _rightTrigger;
			}
		}

		/// <summary>Once per frame at most, however many callers ask.</summary>
		private static void Poll()
		{
			if (_readFrame == Time.frameCount) return;
			_readFrame = Time.frameCount;
			_leftStick = Vector2.zero;
			_rightStick = Vector2.zero;
			_leftTrigger = 0f;
			_rightTrigger = 0f;

			if (_library < 0) return;

			try
			{
				XInputState state = default;
				bool have = _connected >= 0 && TryRead((uint)_connected, out state);

				//Asking an empty slot is slow, because it waits on the driver, so
				//with nothing connected the four slots are only scanned now and then
				if (have == false)
				{
					_connected = -1;
					if (Time.unscaledTime < _nextScan) return;
					_nextScan = Time.unscaledTime + 2f;

					for (uint slot = 0; slot < 4 && have == false; slot++)
					{
						have = TryRead(slot, out state);
						if (have) _connected = (int)slot;
					}

					if (have == false) return;
				}

				_leftStick = Shape(state.Gamepad.ThumbLX, state.Gamepad.ThumbLY, LEFT_DEAD_ZONE);
				_rightStick = Shape(state.Gamepad.ThumbRX, state.Gamepad.ThumbRY, RIGHT_DEAD_ZONE);
				_leftTrigger = Trigger(state.Gamepad.LeftTrigger);
				_rightTrigger = Trigger(state.Gamepad.RightTrigger);
			}
			catch (Exception exception)
			{
				//No XInput on this system at all: stop trying rather than throw every frame
				_library = -1;
				Logging.LogWarning($"Controller unavailable (XInput): {exception.Message}");
			}
		}

		private static bool TryRead(uint slot, out XInputState state)
		{
			if (_library == 0)
			{
				try
				{
					GetState14(0, out state);
					_library = 1;
				}
				catch (DllNotFoundException)
				{
					_library = 2;
				}
			}

			uint result = _library == 1 ? GetState14(slot, out state) : GetState910(slot, out state);
			return result == SUCCESS;
		}

		private static float Trigger(byte raw)
		{
			return raw <= TRIGGER_THRESHOLD ? 0f : (raw - TRIGGER_THRESHOLD) / (255f - TRIGGER_THRESHOLD);
		}

		/// <summary>Radial dead zone, rescaled so movement starts smoothly from zero.</summary>
		private static Vector2 Shape(short rawX, short rawY, float deadZone)
		{
			Vector2 stick = new Vector2(rawX / 32767f, rawY / 32767f);
			float magnitude = stick.magnitude;
			if (magnitude <= deadZone) return Vector2.zero;

			float scaled = Mathf.Clamp01((magnitude - deadZone) / (1f - deadZone));
			return stick / magnitude * scaled;
		}
	}
}
