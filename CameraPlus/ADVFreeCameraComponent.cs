#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ILLGames.Unity;

using Logging = SVS_ADVFreeCamera.ADVFreeCameraPlugin.Logging;


namespace SVS_ADVFreeCamera
{
	/// <summary>
	/// How a conversation starts.
	/// </summary>
	public enum InitialCamera
	{
		/// <summary>Exactly as the game would be without this plugin.</summary>
		DefaultCamera,

		/// <summary>Free camera up straight away. Only Type1 is offered, because
		/// the other modes interrupt the dialogue.</summary>
		CameraType1,
	}


	/// <summary>
	/// Which free camera to use. The difference between them is how each deals
	/// with the scenario re-applying the camera every frame, which is what stops
	/// panning from following the view.
	/// </summary>
	public enum CameraType
	{
		/// <summary>Safe. Dialogue keeps working; panning uses a frame fixed when
		/// the camera came on.</summary>
		CameraType1,

		/// <summary>Correct panning, at the cost of the dialogue UI, because the
		/// scenario driver is switched off.</summary>
		CameraType2,

	}


	/// <summary>
	/// What holding the middle mouse button does while the free camera is on.
	/// </summary>
	public enum MiddleButtonFunction
	{
		/// <summary>The game's own pan.</summary>
		Pan,

		/// <summary>Turn the camera, so rotating needs no left click.</summary>
		Rotate,
	}


	/// <summary>
	/// What the mouse wheel does while the free camera is on.
	/// </summary>
	public enum WheelFunction
	{
		/// <summary>The game's own zoom.</summary>
		Zoom,

		/// <summary>Turn the camera around the subject instead.</summary>
		RotateHorizontal,

		/// <summary>Tilt the camera up and down instead.</summary>
		RotateVertical,
	}


	/// <summary>
	/// What left click does while the free camera is on.
	/// </summary>
	public enum ClickAdvance
	{
		/// <summary>The game as it ships - dragging the camera advances too.</summary>
		Normal,

		/// <summary>Click and wheel never advance while the camera is up.</summary>
		Never,

		/// <summary>Advance only while the pointer is over the message frame.</summary>
		TextAreaOnly,

		/// <summary>A quick tap advances; press and hold moves the camera.</summary>
		TapOnly,
	}


	/// <summary>
	/// Which bone to centre on when Focus Point is Character.
	/// </summary>
	public enum FocusBone
	{
		Head,
		Neck,
		Chest,
		Waist,
		Hips,
		Feet,

	}


	/// <summary>
	/// What the camera orbits around when the free camera is switched on.
	/// </summary>
	public enum FocusMode
	{
		/// <summary>Leave the camera where the scene had it, orbiting a point
		/// straight ahead. Nothing appears to move when you toggle.</summary>
		KeepCameraPosition,

		/// <summary>Aim at the character being talked to.</summary>
		Character,

		/// <summary>Start where the last conversation left off.</summary>
		RememberLastPosition,
	}


	/// <summary>
	/// Lives on the BepInEx manager object for the whole session. Watches for
	/// conversation (ADV) scenes and, on the hotkey, bolts the camera component
	/// the game itself uses onto the conversation camera.
	/// </summary>
	public class ADVFreeCameraComponent : MonoBehaviour
	{
		/*CONSTANTS*/
		//Measured by hand in a conversation: the torso of a standard character
		//sits 1.17 above the floor, with the head bone at 1.30.
		private const float FOCUS_HEIGHT = 1.17f;

		//What the scene itself uses, so Camera Distance reads as an absolute value
		//while still meaning "unchanged" at its default
		private const float DEFAULT_DISTANCE = 1.04f;

		//Generous, because the head bone of a tall character sits further from the
		//camera. The viewport test is what actually decides whether someone is in
		//the shot; this only rules out characters out on the map.
		private const float START_PROXIMITY = 10f;

		//Character creation clamps how far its camera may travel. That is wrong for
		//a free camera, so the limits are off and the camera can go anywhere.



		/*VARIABLES*/
		public static ADVFreeCameraComponent? Instance { get; private set; }

		//Looked up once and reused. Tells us whether a conversation is running.
		private ADV.ADVManager? _advManager;

		//Live free-camera state, all null while the free camera is off
		private CameraController? _controller;
		private GameObject? _pivot;
		private Camera? _camera;
		private Transform? _headTarget;
		private Transform? _charaRoot;
		private Transform? _focusBone;

		//The scenario driver, switched off to stop it re-applying the camera
		private ADV.ADVScene? _advScene;
		private ADV.MainScenario? _mainScenario;
		private bool _scenarioDisabled;
		private int _reEnableScenarioOnFrame = -1;

		//Voices are cut for a short window rather than once, because the replayed
		//line does not start until a frame or two after the scenario is toggled
		private int _silenceUntilFrame = -1;

		//Whether the panning fix is currently engaged, and the orbit centre we own
		//while OverrideCentre is doing the panning
		private bool _fixEngaged;

		//The mode in force right now, which the toggle key can flip without
		//rewriting the configured default
		private CameraType _currentMode = CameraType.CameraType1;
		private ADV.TextScenario? _clickBlocked;



		//Left click tap detection, for ClickAdvance.TapOnly
		private string _lastTap = "none";
		private bool _tapTracking;
		private float _tapDownTime;
		private Vector3 _tapDownPos;

		//Where the camera was left last time, for RememberLastPosition. Static so
		//it survives the component being rebuilt between conversations.
		private static bool _hasRemembered;
		private static Vector3 _rememberedCentre;
		private static Vector3 _rememberedAngles;
		private static float _rememberedDistance;

		//The scenario line in progress, put back so toggling does not replay it
		private int _savedLine = -1;

		//Set when the player switches the camera off by hand, so the auto-start does
		//not immediately bring it back. Cleared when the conversation ends.
		private bool _userSwitchedOff;

		//Waiting for the ADV camera to stop being repositioned before attaching,
		//rather than a fixed delay, so it comes up as soon as it is safe
		private Vector3 _settleLastPosition;
		private int _settleFrames;
		private int _sceneFirstSeenFrame = -1;

		//Framing worked out in Activate and applied once the controller attaches.
		//The base point is kept without offsets so live slider changes can be
		//re-applied on top of it instead of accumulating.
		private Vector3 _baseOrbitPoint;
		private Vector3 _orbitAngles;
		private float _orbitDistance;

		//The original framing of the conversation camera, restored by hand
		//because removing the controller does not put the camera back
		private Vector3 _savedPosition;
		private Quaternion _savedRotation;
		private float _savedFov;

		//Refreshed in Update so OnGUI never has to search for anything
		private bool _inConversation;

		//Config values already applied, so live changes are noticed
		private float _lastDistance = float.NaN;
		private float _lastHeight = float.NaN;
		private float _lastDepth = float.NaN;
		private float _lastTilt = float.NaN;

		public bool IsActive => _controller != null;



		/*ON-SCREEN MESSAGES*/
		private readonly struct Notice
		{
			public readonly string Text;
			public readonly Color Colour;
			public readonly float Expires;

			public Notice(string text, Color colour, float expires)
			{
				Text = text;
				Colour = colour;
				Expires = expires;
			}
		}

		private static readonly List<Notice> _notices = new List<Notice>();

		/// <summary>
		/// Show a message on screen for a few seconds. Called from the logging
		/// helpers so anything written to the log is also visible in game.
		/// </summary>
		public static void Notify(string text, Color colour)
		{
			if (ADVFreeCameraPlugin.showOverlay == null || ADVFreeCameraPlugin.showOverlay.Value == false) return;

			_notices.Add(new Notice(text, colour, Time.unscaledTime + ADVFreeCameraPlugin.overlaySeconds.Value));
			if (_notices.Count > 12) _notices.RemoveAt(0);
		}



		/*CONVERSATION DETECTION*/
		/// <summary>
		/// True while a talk/ADV scene is running. ADVManager is cached because
		/// FindObjectOfType is too slow to call every frame.
		/// </summary>
		private bool IsInConversation()
		{
			if (_advManager == null)
			{
				_advManager = UnityEngine.Object.FindObjectOfType<ADV.ADVManager>();
			}

			return _advManager != null && _advManager.IsADV;
		}



		/*FREE CAMERA*/
		private bool _autoStarting;

		private void Activate()
		{
			ADV.ADVScene? scene = UnityEngine.Object.FindObjectOfType<ADV.ADVScene>();
			if (scene == null)
			{
				Logging.LogWarning("No ADVScene found, cannot enable free camera");
				return;
			}

			Camera camera = scene.ADVCamera;
			if (camera == null)
			{
				Logging.LogWarning("ADVScene has no ADVCamera, cannot enable free camera");
				return;
			}

			//Starting a conversation with the free camera already up must never
			//interrupt the dialogue, so that path is pinned to Type1
			_currentMode = ADVFreeCameraPlugin.initialState.Value == InitialCamera.CameraType1 && IsActive == false && _autoStarting
				? CameraType.CameraType1
				: ADVFreeCameraPlugin.freeCameraType.Value;

			//Remember the framing so we can put it back exactly on detach
			Transform cameraTransform = camera.transform;
			cameraTransform.GetPositionAndRotation(out _savedPosition, out _savedRotation);
			_savedFov = camera.fieldOfView;
			_camera = camera;

			EnsurePivot();

			float distance = ADVFreeCameraPlugin.cameraDistance.Value;
			Vector3 orbitPoint;

			FocusMode focus = ADVFreeCameraPlugin.focusMode.Value;

			if (focus == FocusMode.Character)
			{
				_headTarget = FindSpeakerHead(scene, camera);
			}

			if (focus == FocusMode.RememberLastPosition && _hasRemembered)
			{
				_baseOrbitPoint = _rememberedCentre;
				_pivot!.transform.position = _rememberedCentre + OffsetVector(cameraTransform);
				_orbitAngles = _rememberedAngles;
				_orbitDistance = _rememberedDistance;

				_advScene = scene;
				EngageFix();

				try
				{
					Attach(camera, cameraTransform);
				}
				catch (Exception exception)
				{
					Logging.LogError($"Failed to attach the camera controller: {exception}");
					Deactivate("attach failed");
					return;
				}

				Logging.LogInfo($"Free camera ON, resumed at {_rememberedCentre}");
				return;
			}

			if (_headTarget != null)
			{
				//Keep the angles the scene chose and let the camera move to suit.
				//Re-aiming from the old position instead tilts the view downward
				//and looks nothing like how the game frames a conversation.
				orbitPoint = FocusPoint(cameraTransform);
				Logging.LogInfo($"Focus at {orbitPoint}, head bone {_headTarget.position}");
			}
			else
			{
				//Orbit a point straight ahead, then apply the offsets. They used to
				//be applied inside FocusPoint, which only runs for Character - so
				//height and depth did nothing in the other two modes.
				orbitPoint = _savedPosition + (cameraTransform.forward * distance);
				_baseOrbitPoint = orbitPoint;
				orbitPoint += OffsetVector(cameraTransform);
			}

			_pivot!.transform.position = orbitPoint;
			_orbitAngles = cameraTransform.eulerAngles;
			_orbitDistance = distance;

			_advScene = scene;

			//Resolved in every mode, not just Type2, because the drag lock needs it
			//while the dialogue is still running
			_mainScenario = scene.GetComponent<ADV.MainScenario>();

			EngageFix();

			try
			{
				Attach(camera, cameraTransform);
			}
			catch (Exception exception)
			{
				Logging.LogError($"Failed to attach the camera controller: {exception}");
				Deactivate("attach failed");
				return;
			}

			Logging.LogInfo($"Free camera ON. Saved pos={_savedPosition} rot={_savedRotation.eulerAngles} fov={_savedFov}");
		}

		/// <summary>
		/// Attach as soon as the ADV camera has stopped being moved by the scene.
		/// A fixed delay was either too slow to feel instant or too quick and
		/// captured a pose the scene had not finished setting.
		/// </summary>
		private void WaitForSceneThenActivate()
		{
			ADV.ADVScene? pending = UnityEngine.Object.FindObjectOfType<ADV.ADVScene>();
			if (pending == null)
			{
				_settleFrames = 0;
				_sceneFirstSeenFrame = -1;
				return;
			}

			Camera camera = pending.ADVCamera;
			if (camera == null)
			{
				_settleFrames = 0;
				return;
			}

			//Which readiness signal to trust is a question for testing, so it is a
			//setting rather than a guess baked in here
			if (SceneReady(pending) == false)
			{
				_settleFrames = 0;
				return;
			}

			if (_sceneFirstSeenFrame < 0) _sceneFirstSeenFrame = Time.frameCount;

			Vector3 position = camera.transform.position;
			_settleFrames = (position - _settleLastPosition).sqrMagnitude < 0.000001f ? _settleFrames + 1 : 0;
			_settleLastPosition = position;

			//Two conditions, because either alone was not enough. The camera can sit
			//still for a moment early on while the scene is still being built, which
			//produced a first activation with the wrong framing that then stuck for
			//the rest of the conversation - the bad pose having been recorded as the
			//one to restore. Waiting for a settled camera AND a minimum age makes
			//the automatic start behave like pressing the key a moment later.
			if (_sceneFirstSeenFrame >= 0)
			{
				_settleFrames = 0;
				_sceneFirstSeenFrame = -1;
				Activate();
			}
		}

		/// <summary>
		/// Whether the conversation looks built enough to take the camera over.
		/// </summary>
		private bool SceneReady(ADV.ADVScene scene)
		{
			Camera shotCamera = scene.ADVCamera;
			if (shotCamera == null || shotCamera.isActiveAndEnabled == false) return false;
			if (scene.Scenario == null) return false;

			return SomeoneInShot(shotCamera);
		}

		/// <summary>
		/// Whether any loaded character is standing close in front of the given
		/// camera. During the walk-up to a conversation the characters are still
		/// out on the map, far from the shot.
		/// </summary>
		private static bool SomeoneInShot(Camera camera)
		{
			UniRx.ReactiveCollection<Character.Human> humans = Character.Human._list;
			if (humans == null) return false;

			for (int index = 0; index < humans.Count; index++)
			{
				Character.Human human = humans[index];
				if (human == null || human.face == null) continue;

				GameObject headBone = human.face.objHeadBone;
				if (headBone == null) continue;

				if (InShot(camera, headBone.transform.position)) return true;
			}

			return false;
		}

		/// <summary>
		/// Close enough to the conversation camera, and inside its frame. Distance
		/// alone was not enough - a character can be near the camera without being
		/// in the shot, which is what let the free camera start during the walk-up
		/// to a conversation.
		/// </summary>
		private static bool InShot(Camera camera, Vector3 position)
		{
			if (Vector3.Distance(position, camera.transform.position) > START_PROXIMITY)
			{
				return false;
			}

			Vector3 viewport = camera.WorldToViewportPoint(position);
			return viewport.z > 0f
				&& viewport.x >= 0.05f && viewport.x <= 0.95f
				&& viewport.y >= 0.05f && viewport.y <= 0.95f;
		}

		/// <summary>
		/// Turn the free camera off and put the conversation camera back exactly
		/// where the game left it.
		/// </summary>
		private void Deactivate(string reason)
		{
			//Disable, do NOT destroy. The game subscribes to per-frame UniRx
			//observables in Awake and Start, cleaned up by an
			//ObservableDestroyTrigger when the GameObject dies. Destroying only the
			//component leaves those alive but pointing at a dead object, which
			//throws every frame forever and starves the animation system.
			RememberNow();

			if (_controller != null)
			{
				_controller.enabled = false;
				_controller = null;
			}

			//The pivot is deliberately NOT destroyed - see EnsurePivot
			_headTarget = null;
			_charaRoot = null;
			_focusBone = null;

			if (_camera != null)
			{
				_camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
				_camera.fieldOfView = _savedFov;
				_camera = null;
			}

			ReleaseClickBlock();

			_fixEngaged = false;
			RestoreScenario();
			_advScene = null;
			_mainScenario = null;

			Logging.LogInfo($"Free camera OFF ({reason})");
		}

		/// <summary>
		/// Keep where the camera is now, so RememberLastPosition can resume from
		/// it. Called both when the free camera goes away and when handing control
		/// back to the default camera, so switching types does not lose the view.
		/// </summary>
		private void RememberNow()
		{
			if (_controller == null) return;

			_rememberedCentre = _controller.CameraPos;
			_rememberedAngles = _controller.CameraRot;
			_rememberedDistance = Mathf.Abs(_controller.CameraDir.z);
			_hasRemembered = true;
		}

		/// <summary>
		/// Attach and configure the camera controller.
		///
		/// Unity runs Awake() the instant AddComponent returns, and this component
		/// throws in Awake when its fields are empty - so the GameObject is
		/// deactivated first, which defers Awake until we switch it back on and
		/// gives us a window to fill everything in. Nothing renders in between, so
		/// there is no flicker.
		/// </summary>
		private void Attach(Camera camera, Transform cameraTransform)
		{
			GameObject cameraObject = camera.gameObject;

			//Reuse an existing one. Awake subscribes to a per-frame observable, so
			//adding a fresh component each time would stack up subscriptions.
			CameraController? existing = cameraObject.GetComponent<CameraController>();
			if (existing != null)
			{
				_controller = existing;
				Configure(existing, camera, cameraTransform);
				existing.enabled = true;
				ApplyFraming(existing);
				return;
			}

			bool wasActive = cameraObject.activeSelf;
			CameraController controller;

			try
			{
				cameraObject.SetActive(false);

				controller = cameraObject.AddComponent<CameraController>();
				_controller = controller;

				Configure(controller, camera, cameraTransform);
			}
			finally
			{
				//Always switch the camera back on, or the scene goes black
				if (cameraObject.activeSelf != wasActive) cameraObject.SetActive(wasActive);
			}

			ApplyFraming(controller);
		}

		/// <summary>
		/// Everything Awake() dereferences, plus the tuning values. A prefab would
		/// carry all of this; AddComponent carries none of it.
		/// </summary>
		private void Configure(CameraController controller, Camera camera, Transform cameraTransform)
		{
			if (controller._cameraData == null) controller._cameraData = new CameraController.CameraData();
			if (controller._rotateMove == null) controller._rotateMove = new CameraController.RotateMove();
			if (controller._fov_k__BackingField == null) controller._fov_k__BackingField = new UniRx.FloatReactiveProperty(_savedFov);
			if (controller._isDrags_k__BackingField == null) controller._isDrags_k__BackingField = new Il2CppStructArray<bool>(4);
			if (controller._isButtons_k__BackingField == null) controller._isButtons_k__BackingField = new Il2CppStructArray<bool>(4);
			controller._nowCamera = camera;
			controller._transformCache = cameraTransform;

			//These gate whether input is accepted
			controller.noCtrlCondition = Predicate(() => false);
			controller.keyCondition = Predicate(() => true);
			controller.zoomCondition = Predicate(() => true);

			controller._targetTrans = _pivot!.transform;
			controller._targetTexTrans = _pivot!.transform;
			controller.isConfigTargetTex = true;

			controller._xRotSpeed = ADVFreeCameraPlugin.rotateSpeed.Value * SignX();
			controller._yRotSpeed = ADVFreeCameraPlugin.rotateSpeed.Value * SignY();
			controller._zoomSpeed = ADVFreeCameraPlugin.zoomSpeed.Value;
			controller._moveSpeed = ADVFreeCameraPlugin.moveSpeed.Value * MoveSign();
			controller._noneTargetDir = 5f;

			//Pausing sets the time scale to zero. Without this the controller stops
			//updating and the view snaps back to the scene camera until unpaused.
			controller._ignoreTimeScale = true;

			controller._isLimitPos = false;
			controller._isLimitDir = false;
			controller._limitFov = 40f;
			controller._isLlimitRot = false;

			controller.cameraType = CameraController.Config.Rotation;
		}

		/// <summary>
		/// Reproduce the framing worked out in Activate. Must run after Awake,
		/// which would otherwise overwrite it.
		/// </summary>
		private void ApplyFraming(CameraController controller)
		{
			if (controller._cameraData == null) controller._cameraData = new CameraController.CameraData();

			controller.CameraPos = _pivot!.transform.position;
			//Tilt is applied here rather than baked into _orbitAngles, so it reaches
			//every path that writes the framing - including the remembered one
			controller.CameraRot = _orbitAngles + new Vector3(ADVFreeCameraPlugin.angleOffset.Value, 0f, 0f);
			controller.CameraDir = new Vector3(0f, 0f, -_orbitDistance);
			controller.CameraFov = _savedFov;
		}

		private static Il2CppSystem.Func<bool> Predicate(Func<bool> predicate)
		{
			return DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(predicate)!;
		}



		/*SCENARIO DRIVER*/
		/// <summary>
		/// Stop ADV.MainScenario re-applying the scenario camera every frame,
		/// which is what prevents panning from working.
		///
		/// Harmony cannot do this: prefixes on its Update are reached, but
		/// returning false does not skip the original on this IL2CPP target.
		/// Clearing the enabled flag is the only thing that works.
		///
		/// Switching it straight back on next frame keeps the camera free while
		/// clearing the leftover subtitle and its backdrop. The dialogue UI stays
		/// unresponsive until the free camera is switched off.
		/// </summary>
		private void EngageFix()
		{
			if (_fixEngaged) return;

			CameraType fix = _currentMode;
			if (fix == CameraType.CameraType1) return;

			_fixEngaged = true;

			if (_mainScenario == null)
			{
				Logging.LogWarning("No MainScenario found, panning will be restricted");
				return;
			}

			//Toggling the driver makes it run the current line again - voice, lip
			//sync, subtitle and animation together. Putting the line number back
			//after the toggle stops the replay at source rather than muting parts
			//of it one at a time.
			_savedLine = _mainScenario._currentLine;

			_mainScenario.enabled = false;
			_scenarioDisabled = true;
			_reEnableScenarioOnFrame = Time.frameCount + 1;
			RequestSilence();

			Logging.LogWarning("Panning fix: scenario disabled - dialogue UI will not respond");
		}

		private void DisengageFix()
		{
			if (_fixEngaged == false) return;

			_fixEngaged = false;
			RestoreScenario();

			//The scene cycle resets the camera, so put our framing back
			if (_controller != null) ApplyFraming(_controller);
		}

		/// <summary>
		/// Put the scenario back. Re-enabling the component is not enough to revive
		/// the dialogue UI; the scene object has to be cycled to rebuild it.
		/// </summary>
		private void RestoreScenario()
		{
			_reEnableScenarioOnFrame = -1;

			//Only put things back if Type2 actually took them apart
			if (_scenarioDisabled == false || _mainScenario == null) return;

			_scenarioDisabled = false;
			Logging.LogInfo("Scenario restored");

			GameObject sceneObject = _mainScenario.gameObject;
			_mainScenario.enabled = true;
			if (_savedLine >= 0) _mainScenario._currentLine = _savedLine;
			_mainScenario = null;
			_savedLine = -1;

			if (sceneObject != null)
			{
				sceneObject.SetActive(false);
				sceneObject.SetActive(true);
			}

			RequestSilence();
		}

		/// <summary>
		/// Cut off any dialogue voice that is playing. Toggling the scenario makes
		/// the current line start again, and hearing the same greeting repeatedly
		/// is the most irritating part of this approach.
		/// </summary>
		/// <summary>
		/// Cut voices for the next half second or so. The replayed line does not
		/// begin on the same frame the scenario is toggled, so a single call
		/// catches nothing - this is why restoring needed its own window too.
		/// </summary>
		private void RequestSilence()
		{
			_silenceUntilFrame = Time.frameCount + 40;
			SilenceVoices();
		}

		private void SilenceVoices()
		{
			if (ADVFreeCameraPlugin.silenceRepeatedVoice.Value == false || _advManager == null) return;

			try
			{
				Il2CppSystem.Collections.Generic.List<AudioSource> voices = _advManager._playingVoice;
				if (voices == null) return;

				int stopped = 0;
				for (int index = 0; index < voices.Count; index++)
				{
					AudioSource source = voices[index];
					if (source == null || source.isPlaying == false) continue;

					source.Stop();
					stopped++;
				}

				if (stopped > 0) Logging.LogInfo($"Stopped {stopped} repeated voice line(s)");
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Could not stop the voice line: {exception.Message}");
			}
		}



		/*FOCUS POINT*/
		/// <summary>
		/// The pivot has to outlive every teardown. The camera component subscribes
		/// to per-frame UniRx observables which keep firing even when it is
		/// disabled, and a destroyed target Transform would make them throw every
		/// frame. One pivot for the whole session removes that risk.
		/// </summary>
		private void EnsurePivot()
		{
			if (_pivot != null) return;

			_pivot = new GameObject("ADVFreeCameraPivot");
			DontDestroyOnLoad(_pivot);
		}

		/// <summary>
		/// Find the bone to centre on.
		///
		/// The rig is named in lower case - the chain under BodyTop is
		/// p_cf_sv_body_bone_00(Clone) / cf_j_root / cf_n_height / cf_j_hips /
		/// cf_j_spine01 / cf_j_spine02 / cf_j_spine03 / cf_j_neck / cf_s_head.
		///
		/// Matching is by exact name, not by substring: several bones contain
		/// "neck" and only cf_j_neck is the visible one, which is why a substring
		/// search picked seemingly random joints.
		/// </summary>
		private Transform? ResolveFocusBone(Character.Human human, Transform head)
		{
			try
			{
				//Search the skeleton the real head bone belongs to, not the whole
				//character. A character can carry more than one rig - a low poly
				//copy, a spare body - and a depth-first search from the top can
				//return a bone of the wrong one, positioned somewhere else. That is
				//why floor bones resolved for a character whose neck and chest did
				//not: those happened to be unique, the spine bones were not.
				Transform root = SkeletonRoot(head) ?? human.transform;
				if (root == null) return head;

				FocusBone wanted = ADVFreeCameraPlugin.focusBone.Value;

				//The Test_ entries name the bone directly, so candidates can be
				//tried from the dropdown without a rebuild
				string name = wanted.ToString();
				if (name.StartsWith("Test_", StringComparison.Ordinal))
				{
					return FindBone(root, name.Substring(5));
				}

				switch (wanted)
				{
					case FocusBone.Head:
						return FindBone(root, "cf_s_head") ?? head;

					case FocusBone.Chest:
						return FindBone(root, "cf_j_spine03")
							?? FindBone(root, "cf_j_spine02")
							?? head;

					case FocusBone.Waist:
						//What used to be Chest, which read correctly as the waist
						return FindBone(root, "cf_j_spine02")
							?? FindBone(root, "cf_j_spine01")
							?? human.body?.trfBodyBone;

					case FocusBone.Hips:
						//cf_j_waist02 measured closest to the true hips in game
						return FindBone(root, "cf_j_waist02")
							?? FindBone(root, "cf_j_hips")
							?? human.body?.trfBodyBone;

					case FocusBone.Feet:
						//cf_j_root and cf_n_height both sit on the floor
						return FindBone(root, "cf_j_root")
							?? FindBone(root, "cf_n_height");

					default:
						return FindBone(root, "cf_j_neck")
							?? FindBone(root, "cf_j_spine03")
							?? head;
				}
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Could not resolve the focus bone: {exception.Message}");
				return null;
			}
		}

		/// <summary>
		/// Walk up from a known-good bone to the top of its skeleton, so searches
		/// stay inside the rig that is actually being drawn.
		/// </summary>
		private static Transform? SkeletonRoot(Transform bone)
		{
			Transform node = bone;

			for (int guard = 0; guard < 64 && node != null; guard++)
			{
				if (string.Equals(node.name, "cf_j_root", StringComparison.OrdinalIgnoreCase)) return node;

				Transform parent = node.parent;
				if (parent == null) return node;
				node = parent;
			}

			return node;
		}

		/// <summary>Depth-first search for an exactly named transform.</summary>
		private static Transform? FindBone(Transform root, string name)
		{
			if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase)) return root;

			for (int index = 0; index < root.childCount; index++)
			{
				Transform? found = FindBone(root.GetChild(index), name);
				if (found != null) return found;
			}

			return null;
		}

		/// <summary>
		/// Height and depth as a world offset. Depth runs away from the camera, so
		/// positive pushes the aim point past the subject.
		/// </summary>
		private static Vector3 OffsetVector(Transform cameraTransform)
		{
			Vector3 offset = Vector3.up * ADVFreeCameraPlugin.focusHeightOffset.Value;

			Vector3 forward = cameraTransform.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude > 0.0001f)
			{
				offset += forward.normalized * ADVFreeCameraPlugin.focusDepthOffset.Value;
			}

			return offset;
		}

		private Vector3 FocusPoint(Transform cameraTransform)
		{
			Vector3 point;

			if (_focusBone != null)
			{
				//Centre on the bone itself, which follows the character as they move
				//and needs no assumption about how tall they are
				Vector3 bone = _focusBone.position;
				point = new Vector3(bone.x, bone.y + ADVFreeCameraPlugin.focusHeightOffset.Value, bone.z);
			}
			else if (_headTarget != null)
			{
				//No bone resolved. The head is a far better fallback than a fixed
				//height above the feet, which is wrong for anyone unusually tall or
				//short - and being wrong there looked like centring had failed.
				Vector3 head = _headTarget.position;
				point = new Vector3(head.x, head.y + ADVFreeCameraPlugin.focusHeightOffset.Value, head.z);
			}
			else
			{
				Transform root = _charaRoot!;
				Vector3 position = root.position;

				point = new Vector3(
					position.x,
					position.y + FOCUS_HEIGHT + ADVFreeCameraPlugin.focusHeightOffset.Value,
					position.z);
			}

			//Depth runs away from the camera, so positive pushes the aim point past
			//the character and negative brings it nearer
			Vector3 forward = cameraTransform.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude > 0.0001f)
			{
				point += forward.normalized * ADVFreeCameraPlugin.focusDepthOffset.Value;
			}

			return point;
		}

		/// <summary>
		/// The head bone of the character on screen, or null if none can be found.
		///
		/// The scenario cast list cannot be used: its ChaCtrl points at the
		/// character as it exists on the simulation map, hundreds of metres away,
		/// not the copy posed in front of the conversation camera. Character.Human
		/// is not a MonoBehaviour either, so FindObjectsOfType cannot see them -
		/// the static registry is the only way to enumerate characters.
		/// </summary>
		private Transform? FindSpeakerHead(ADV.ADVScene scene, Camera camera)
		{
			try
			{
				UniRx.ReactiveCollection<Character.Human> humans = Character.Human._list;
				if (humans == null || humans.Count == 0)
				{
					Logging.LogWarning("No loaded characters, cannot centre on anyone");
					return null;
				}

				//Measure from where the scene stands its cast, not from the camera.
				//Anchoring on the camera meant that moving the camera far away, or
				//framing that put the character off to one side, changed which
				//character was picked - or found none at all.
				//Newest first, and that is the whole rule. The conversation character
				//is spawned when the scene starts, so the last entry is the one being
				//talked to - and unlike any distance test it does not care how tall
				//anybody is.
				//
				//No in-shot filter here. That test belongs to deciding *when* to
				//start, not to *who* to centre on: applied to selection it rejected
				//characters that were simply not inside the viewport at that instant,
				//which broke centring for everyone.
				Character.Human? best = null;
				Transform? bestHead = null;

				for (int step = 0; step < humans.Count; step++)
				{
					Character.Human human = humans[humans.Count - 1 - step];
					if (human == null || human.face == null) continue;

					GameObject headBone = human.face.objHeadBone;
					if (headBone == null) continue;

					best = human;
					bestHead = headBone.transform;
					break;
				}

				if (bestHead == null)
				{
					Logging.LogWarning($"No character with a head bone among {humans.Count}");
					return null;
				}

				_charaRoot = best!.transform;
				_focusBone = ResolveFocusBone(best, bestHead);

				Logging.LogInfo($"Centred on \"{_charaRoot.name}\", {humans.Count} loaded");

				if (_focusBone == null)
				{
					Logging.LogWarning($"No {ADVFreeCameraPlugin.focusBone.Value} bone found, falling back to the head");
				}
				else
				{
					Logging.LogInfo($"{ADVFreeCameraPlugin.focusBone.Value} bone \"{_focusBone.name}\" at {_focusBone.position}");
				}

				return bestHead;
			}
			catch (Exception exception)
			{
				Logging.LogWarning($"Could not find anyone to centre on: {exception.Message}");
			}

			return null;
		}



		/*UNITY METHODS*/
		private void Update()
		{
			if (ADVFreeCameraPlugin.enabled.Value == false)
			{
				if (IsActive) Deactivate("plugin disabled");
				return;
			}

			//Switch the scenario back on the frame after it was switched off. That
			//keeps the camera free while clearing the leftover subtitle.
			if (_reEnableScenarioOnFrame >= 0 && Time.frameCount >= _reEnableScenarioOnFrame)
			{
				_reEnableScenarioOnFrame = -1;

				if (_mainScenario != null)
				{
					_mainScenario.enabled = true;
					if (_savedLine >= 0) _mainScenario._currentLine = _savedLine;
				}

				//The scenario gets one frame to run before it is switched off
				//again, and moves the camera in it. Putting the framing back here
				//removes the flicker that caused.
				if (_controller != null) ApplyFraming(_controller);
			}

			//The replayed line starts a frame or two after a toggle, so silencing
			//runs for a short window rather than once
			if (Time.frameCount <= _silenceUntilFrame) SilenceVoices();

			//Leaving the conversation must always tear the free camera down,
			//otherwise the controller keeps driving a camera the game reuses
			_inConversation = IsInConversation();

			if (_inConversation == false)
			{
				_settleFrames = 0;
				_sceneFirstSeenFrame = -1;
				_userSwitchedOff = false;
				if (IsActive) Deactivate("conversation ended");
				return;
			}

			bool autoStart = ADVFreeCameraPlugin.initialState.Value == InitialCamera.CameraType1;

			//Bring the camera up by itself when the conversation starts
			if (autoStart && IsActive == false && _userSwitchedOff == false)
			{
				_autoStarting = true;
				WaitForSceneThenActivate();
				_autoStarting = false;
			}

			//Also applied here, not only in LateUpdate: the scenario handles the
			//click in its own Update, so setting the flag afterwards was always a
			//frame late.
			ApplyClickBlock();

			//Only here, never in LateUpdate: GetMouseButtonUp is true in both, so
			//a tap would otherwise be counted twice and skip a line
			TrackTapAdvance();

			if (Input.GetKeyDown(ADVFreeCameraPlugin.toggleKey.Value))
			{
				if (IsActive)
				{
					_userSwitchedOff = true;
					Deactivate("hotkey");
				}
				else
				{
					_userSwitchedOff = false;
					Activate();
				}
			}

		}

		/// <summary>
		/// Direction of horizontal mouse input. Rotation carries a separate speed
		/// per axis, so inverting it is only a sign on that speed.
		/// </summary>
		private static float SignX()
		{
			return ADVFreeCameraPlugin.invertX.Value ? -1f : 1f;
		}

		/// <summary>
		/// Direction of vertical mouse input.
		/// </summary>
		private static float SignY()
		{
			return ADVFreeCameraPlugin.invertY.Value ? -1f : 1f;
		}

		/// <summary>
		/// Panning and the right-drag dolly share the single _moveSpeed value, so
		/// they cannot be inverted one axis at a time. Reversing them only when both
		/// axes are inverted keeps the two settings honest.
		/// </summary>
		private static float MoveSign()
		{
			bool both = ADVFreeCameraPlugin.invertX.Value && ADVFreeCameraPlugin.invertY.Value;
			return both ? -1f : 1f;
		}


		/// <summary>
		/// Push settings that can be changed mid-conversation straight through, so
		/// dragging a slider in the config window shows immediately rather than
		/// only after the window closes and the camera is rebuilt.
		/// </summary>
		private void ApplyLiveConfig()
		{
			if (IsActive == false || _controller == null) return;

			_controller._xRotSpeed = ADVFreeCameraPlugin.rotateSpeed.Value * SignX();
			_controller._yRotSpeed = ADVFreeCameraPlugin.rotateSpeed.Value * SignY();
			_controller._zoomSpeed = ADVFreeCameraPlugin.zoomSpeed.Value;
			_controller._moveSpeed = ADVFreeCameraPlugin.moveSpeed.Value * MoveSign();

			//Framing is only re-applied when a value actually changes, otherwise it
			//would fight the player every frame
			float distance = ADVFreeCameraPlugin.cameraDistance.Value;
			float height = ADVFreeCameraPlugin.focusHeightOffset.Value;
			float depth = ADVFreeCameraPlugin.focusDepthOffset.Value;
			float tilt = ADVFreeCameraPlugin.angleOffset.Value;

			if (Mathf.Approximately(distance, _lastDistance)
				&& Mathf.Approximately(height, _lastHeight)
				&& Mathf.Approximately(depth, _lastDepth)
				&& Mathf.Approximately(tilt, _lastTilt)) return;

			_lastDistance = distance;
			_lastHeight = height;
			_lastDepth = depth;
			_lastTilt = tilt;

			_orbitDistance = distance;

			//Every focus mode, not just Character. Height and depth were only being
			//re-applied when a character was targeted, so dragging those sliders did
			//nothing in KeepCameraPosition and RememberLastPosition.
			if (_camera != null)
			{
				Vector3 point = _headTarget != null
					? FocusPoint(_camera.transform)
					: _baseOrbitPoint + OffsetVector(_camera.transform);

				if (_pivot != null) _pivot.transform.position = point;
				_controller.CameraPos = point;
			}

			_controller.CameraDir = new Vector3(0f, 0f, -_orbitDistance);
			_controller.CameraRot = _orbitAngles + new Vector3(ADVFreeCameraPlugin.angleOffset.Value, 0f, 0f);
		}

		/// <summary>
		/// Apply the framing offsets to the game's own camera when the free camera
		/// is not attached, so the settings mean the same thing in every mode. At
		/// their defaults nothing moves.
		/// </summary>
		private void OffsetDefaultCamera()
		{
			if (IsActive || _inConversation == false) return;
			if (ADVFreeCameraPlugin.offsetsAffectDefault.Value == false) return;

			float height = ADVFreeCameraPlugin.focusHeightOffset.Value;
			float depth = ADVFreeCameraPlugin.focusDepthOffset.Value;
			float distance = ADVFreeCameraPlugin.cameraDistance.Value - DEFAULT_DISTANCE;

			float angle = ADVFreeCameraPlugin.angleOffset.Value;

			//Tilt has to be part of this test. It was not, so setting tilt alone
			//returned here and the default camera never moved.
			if (Mathf.Approximately(height, 0f) && Mathf.Approximately(depth, 0f)
				&& Mathf.Approximately(distance, 0f) && Mathf.Approximately(angle, 0f)) return;

			ADV.ADVScene? scene = _advScene;
			if (scene == null)
			{
				scene = UnityEngine.Object.FindObjectOfType<ADV.ADVScene>();
				if (scene == null) return;
				_advScene = scene;
			}

			Camera camera = scene.ADVCamera;
			if (camera == null) return;

			//Runs after the scenario has placed the camera, so this is a nudge on
			//top of whatever framing the scene chose
			Transform view = camera.transform;
			Vector3 forward = view.forward;
			view.position += (Vector3.up * height) + (forward * depth) - (forward * distance);

			if (Mathf.Approximately(angle, 0f) == false)
			{
				Vector3 angles = view.eulerAngles;
				angles.x += angle;
				view.eulerAngles = angles;
			}
		}

		/// <summary>
		/// Runs after the scenario has had its turn, so anything written here is
		/// the last word on the camera for the frame.
		/// </summary>
		private void LateUpdate()
		{
			ApplyLiveConfig();
			ApplyClickBlock();
			RemapWheel();
			RemapMiddleButton();
			OffsetDefaultCamera();
		}


		/// <summary>
		/// Decide whether left click may advance the dialogue this frame, and hold
		/// the games own gate to match.
		///
		/// That gate is TextScenario.Regulate, a bit-flag set of what is currently
		/// blocked, and ClickNext is the flag for advancing by click. Confirmed in
		/// game: Control reads 0 during normal dialogue, AddRegulate(ClickNext) stops
		/// clicking and scrolling from advancing, SubRegulate(ClickNext) puts both
		/// back, and choice buttons stay clickable throughout.
		///
		/// MainScenario.IsInputLock was tried first and is simply the wrong flag - it
		/// can be held true every single frame and clicking still advances.
		/// </summary>
		private void ApplyClickBlock()
		{
			ADV.TextScenario? scenario = Scenario();
			bool wanted = IsActive && scenario != null && BlockClickNow(scenario!);

			//A new line can bring a new scenario object, so let go of the old one first
			if (_clickBlocked != null && (wanted == false || _clickBlocked != scenario))
			{
				ReleaseClickBlock();
			}

			if (wanted == false) return;

			if (_clickBlocked == null)
			{
				//If the scenario is already blocking clicks by itself, leave it alone.
				//Lifting the flag later would be undoing a restriction we did not impose
				if ((scenario!.Regulate.Control & ADV.Regulate.Controls.ClickNext) != 0) return;
				_clickBlocked = scenario;
			}

			//Written every frame in case the scenario recomputes its own restrictions
			scenario!.Regulate.AddRegulate(ADV.Regulate.Controls.ClickNext);
		}

		/// <summary>
		/// The running scenario, or null when there is nothing to advance.
		/// </summary>
		private ADV.TextScenario? Scenario()
		{
			ADV.TextScenario? scenario = _advScene != null ? _advScene.Scenario : null;
			if (scenario != null && scenario.Regulate == null) scenario = null;
			return scenario;
		}

		/// <summary>
		/// Whether click-advance is blocked right now. Decided every frame from where
		/// the pointer is, not at the moment of the click: the scenario handles the
		/// click inside its own Update and there is no guarantee we run first, so the
		/// flag has to be correct before the button ever goes down.
		/// </summary>
		private bool BlockClickNow(ADV.TextScenario scenario)
		{
			switch (ADVFreeCameraPlugin.clickAdvance.Value)
			{
				case ClickAdvance.Never:
					return true;

				case ClickAdvance.TextAreaOnly:
					return PointerOverMessageWindow(scenario) == false;

				//Always blocked here. A tap is recognised on release and advanced by hand
				case ClickAdvance.TapOnly:
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// A quick tap advances the line, a press and hold moves the camera. Click is
		/// blocked the whole time in this mode, so the advance is made by hand once the
		/// button comes back up soon enough, and close enough to where it went down.
		/// </summary>
		private void TrackTapAdvance()
		{
			if (IsActive == false) return;
			if (ADVFreeCameraPlugin.clickAdvance.Value != ClickAdvance.TapOnly) return;

			if (Input.GetMouseButtonDown(0))
			{
				_tapTracking = true;
				_tapDownTime = Time.unscaledTime;
				_tapDownPos = Input.mousePosition;
				return;
			}

			if (Input.GetMouseButtonUp(0) == false || _tapTracking == false) return;
			_tapTracking = false;

			float held = Time.unscaledTime - _tapDownTime;
			float moved = Vector3.Distance(Input.mousePosition, _tapDownPos);

			if (held > ADVFreeCameraPlugin.tapSeconds.Value)
			{
				_lastTap = $"{held:F2}s held too long";
				return;
			}

			if (moved > ADVFreeCameraPlugin.tapPixels.Value)
			{
				_lastTap = $"{held:F2}s dragged {moved:F0}px";
				return;
			}

			_lastTap = $"{held:F2}s {(AdvanceOneLine() ? "advanced" : "nothing to advance")}";
		}

		/// <summary>
		/// Advance the dialogue the way a click would have. The block is lifted around
		/// the call and put straight back, so it makes no difference whether this path
		/// checks ClickNext - and it all happens inside one frame, so nothing else ever
		/// sees the flag clear.
		/// </summary>
		private bool AdvanceOneLine()
		{
			ADV.TextScenario? scenario = Scenario();
			if (scenario == null) return false;

			//A choice on screen is picked by clicking the option itself, which the
			//block never touched - advancing here as well would eat the next line
			if (scenario.IsChoice) return false;

			//With no frame on screen there is no line to advance, and asking for one
			//ends the conversation instead
			Rect frame;
			if (MessageWindowRect(scenario, out frame) == false) return false;

			//Paused, the frame is still up but the tap was meant for a menu button
			if (Time.timeScale <= 0f) return false;

			//A line arrives one letter at a time, and the first click finishes it off
			//rather than moving on. Claiming the text was already complete made the
			//scenario hold the request until the typewriter had finished by itself,
			//which is what queued up clicks and advanced several lines at once.
			ADV.TextController? text = scenario.TextController;

			if (text != null && text.IsCompleteDisplayText == false)
			{
				text.ForceCompleteDisplayText();
				return true;
			}

			bool restore = _clickBlocked != null;
			if (restore) scenario.Regulate.SubRegulate(ADV.Regulate.Controls.ClickNext);

			//MessageWindowProc is the games own click handler and does the bookkeeping
			//around a line ending, which RequestNextLine on its own does not - calling
			//that directly leaves the last voice line to start over. NextInfo is what a
			//click reports: display finished, next asked for, skip not held.
			ADV.TextScenario.NextInfo info = new ADV.TextScenario.NextInfo();
			info.Set(true, true, false);
			scenario.MessageWindowProc(info);

			if (restore) scenario.Regulate.AddRegulate(ADV.Regulate.Controls.ClickNext);
			return true;
		}

		/// <summary>
		/// Whether the pointer is over the message frame.
		/// </summary>
		private static bool PointerOverMessageWindow(ADV.TextScenario scenario)
		{
			Rect area;
			if (MessageWindowRect(scenario, out area) == false) return false;

			Vector3 mouse = Input.mousePosition;
			return area.Contains(new Vector2(mouse.x, mouse.y));
		}

		/// <summary>
		/// The message frame in screen pixels, y measured from the bottom. Read from
		/// the windows own RectTransform rather than a guessed height, so it follows
		/// the resolution and the UI scale with no calibration.
		/// </summary>
		private static bool MessageWindowRect(ADV.TextScenario scenario, out Rect area)
		{
			area = new Rect();

			RectTransform? window = scenario._messageWindow;
			if (window == null || window.gameObject.activeInHierarchy == false) return false;

			//The frame is not deactivated when it goes away, so being active is not
			//enough on its own - it can be switched off at the canvas, faded out, or
			//simply parked outside the screen
			Canvas? canvas = scenario._msgWindowCanvas;
			if (canvas != null && canvas.enabled == false) return false;

			CanvasGroup? group = window!.GetComponentInParent<CanvasGroup>();
			if (group != null && group.alpha < 0.05f) return false;

			Il2CppStructArray<Vector3> corners = new Il2CppStructArray<Vector3>(4);
			window!.GetWorldCorners(corners);

			//An overlay canvas is already in screen pixels; anything else has to be
			//projected through the camera that draws it
			Camera? uiCamera = null;
			if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) uiCamera = canvas.worldCamera;

			Vector3 bottomLeft = corners[0];
			Vector3 topRight = corners[2];

			if (uiCamera != null)
			{
				bottomLeft = uiCamera!.WorldToScreenPoint(bottomLeft);
				topRight = uiCamera!.WorldToScreenPoint(topRight);
			}

			area = Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);

			//Overlaps is false for an empty rect as well, so this covers a frame that
			//has been collapsed to nothing as well as one moved out of sight
			return area.Overlaps(new Rect(0f, 0f, Screen.width, Screen.height));
		}


		/// <summary>
		/// Hand click-advance back, and only to the exact scenario it was taken from.
		/// </summary>
		private void ReleaseClickBlock()
		{
			if (_clickBlocked == null) return;

			if (_clickBlocked.Regulate != null)
			{
				_clickBlocked.Regulate.SubRegulate(ADV.Regulate.Controls.ClickNext);
			}

			_clickBlocked = null;
		}


		/// <summary>
		/// Re-purpose the mouse wheel. Rotating with the wheel avoids holding a
		/// mouse button, and left click is the only button that advances the
		/// dialogue - so this is a way to turn the camera without touching it.
		/// </summary>
		private void RemapWheel()
		{
			if (_controller == null) return;

			WheelFunction wheel = ADVFreeCameraPlugin.wheelFunction.Value;

			if (wheel == WheelFunction.Zoom)
			{
				_controller._zoomSpeed = ADVFreeCameraPlugin.zoomSpeed.Value;
				return;
			}

			//Suppress the built-in zoom so both do not act on one scroll. This has
			//to run after ApplyLiveConfig, which writes _zoomSpeed every frame.
			_controller._zoomSpeed = 0f;

			float scroll = Input.GetAxis("Mouse ScrollWheel");
			if (scroll == 0f) return;

			Vector3 angles = _controller.CameraRot;
			float amount = scroll * ADVFreeCameraPlugin.zoomSpeed.Value * 40f;

			if (wheel == WheelFunction.RotateHorizontal) angles.y += amount * SignX();
			else angles.x += amount * SignY();

			_controller.CameraRot = angles;
		}

		/// <summary>
		/// Rotate with the middle button held, so the camera can be turned without
		/// left click.
		/// </summary>
		private void RemapMiddleButton()
		{
			if (_controller == null) return;
			if (ADVFreeCameraPlugin.middleButtonFunction.Value != MiddleButtonFunction.Rotate) return;

			if (Input.GetMouseButton(2) == false) return;

			//Suppress the built-in middle-drag pan only while the button is down, so
			//the right-drag dolly is untouched the rest of the time
			_controller._moveSpeed = 0f;

			float dx = Input.GetAxis("Mouse X") * ADVFreeCameraPlugin.rotateSpeed.Value * SignX();
			float dy = Input.GetAxis("Mouse Y") * ADVFreeCameraPlugin.rotateSpeed.Value * SignY();
			if (dx == 0f && dy == 0f) return;

			Vector3 angles = _controller.CameraRot;
			angles.y += dx;
			angles.x -= dy;
			_controller.CameraRot = angles;
		}

		/// <summary>
		/// Aim the free camera at a named bone on the character it is centred on,
		/// for trying candidates from the RuntimeUnityEditor console:
		///
		///   SVS_ADVFreeCamera.ADVFreeCameraComponent.AimAt("cf_j_hips")
		///
		/// GameObject.Find is no use here - it matches the first object of that name
		/// anywhere in the scene, usually a character on the map a hundred metres
		/// away, and the camera flies off to look at nothing.
		/// </summary>
		public static string AimAt(string boneName)
		{
			ADVFreeCameraComponent? self = Instance;
			if (self == null) return "CameraPlus is not running";
			if (self._controller == null) return "Switch the free camera on first";
			if (self._charaRoot == null) return "Set Focus Point to Character first";

			Transform? bone = FindBone(self._charaRoot, boneName);
			if (bone == null) return $"No bone called {boneName} on {self._charaRoot.name}";

			self._focusBone = bone;

			Vector3 point = bone.position;
			if (self._pivot != null) self._pivot.transform.position = point;
			self._controller.CameraPos = point;

			return $"{boneName} at {point}";
		}

		/// <summary>
		/// List the joint bones of the character currently centred on, with their
		/// heights, for the RuntimeUnityEditor console:
		///
		///   SVS_ADVFreeCamera.ADVFreeCameraComponent.Bones()
		/// </summary>
		/// <summary>
		/// List the joint bones of the character currently centred on, with their
		/// heights, for the RuntimeUnityEditor console:
		///
		///   SVS_ADVFreeCamera.ADVFreeCameraComponent.Bones()
		/// </summary>
		public static string Bones()
		{
			ADVFreeCameraComponent? self = Instance;
			if (self == null) return "Plugin is not running";
			if (self._headTarget == null) return "Switch the free camera on with Focus Point set to Character";

			Transform? root = SkeletonRoot(self._headTarget);
			if (root == null) return "No skeleton found";

			System.Text.StringBuilder builder = new System.Text.StringBuilder();
			builder.Append(root.name);
			Walk(root, 0, builder);
			return builder.ToString();
		}

		private static void Walk(Transform node, int depth, System.Text.StringBuilder builder)
		{
			string name = node.name;

			//Joint bones only: the rig carries hundreds of helper nodes that are no
			//use as a camera target
			if (name.StartsWith("cf_j_", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("cf_n_", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("cf_s_", StringComparison.OrdinalIgnoreCase))
			{
				builder.AppendLine().Append(new string(' ', depth * 2)).Append(name)
					.Append("  y=").Append(node.position.y.ToString("F3"));
			}

			for (int index = 0; index < node.childCount; index++)
			{
				Walk(node.GetChild(index), depth + 1, builder);
			}
		}

		private void OnGUI()
		{
			//Only shown while the camera is actually being moved, the way character
			//creation shows its target marker
			bool moving = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
			bool live = _inConversation && Time.timeScale > 0f;
			if (IsActive && live && moving && ADVFreeCameraPlugin.showCrosshair.Value) DrawCrosshair();

			if (_inConversation && ADVFreeCameraPlugin.showTextArea.Value) DrawTextArea();

			if (ADVFreeCameraPlugin.showOverlay.Value == false) return;

			float y = 10f;

			if (_inConversation)
			{
				string state = IsActive ? "ON" : "off";
				Draw(y, $"ADVFreeCamera: {state}   [{ADVFreeCameraPlugin.toggleKey.Value} to toggle]",
					IsActive ? Color.green : Color.white);
				y += 22f;
			}

			//Shown even with the camera off: the block only applies while it is on,
			//which is the first thing to check when a mode looks like it does nothing
			if (_inConversation && ADVFreeCameraPlugin.showReadout.Value)
			{
				ADV.TextScenario? scenario = Scenario();
				string gate = scenario != null ? scenario.Regulate.Control.ToString() : "no scenario";
				string held = _clickBlocked != null ? "ours" : "not ours";

				Rect frame;
				bool onScreen = scenario != null && MessageWindowRect(scenario!, out frame);

				Draw(y, $"click {ADVFreeCameraPlugin.clickAdvance.Value}   camera {(IsActive ? "ON" : "off")}   block {held}   Regulate {gate}", Color.yellow);
				y += 20f;
				string state = scenario == null ? "no scenario" : (onScreen ? "on screen" : "hidden");
				if (ADVFreeCameraPlugin.clickAdvance.Value == ClickAdvance.TapOnly) state += $"   tap {_lastTap}";

				Draw(y, $"frame {state}", Color.yellow);
				y += 20f;
			}

			if (IsActive && _camera != null && ADVFreeCameraPlugin.showReadout.Value)
			{
				Transform view = _camera.transform;
				Vector3 pivot = _pivot != null ? _pivot.transform.position : Vector3.zero;

				Draw(y, $"cam pos {Format(view.position)}   angles {Format(view.eulerAngles)}   fov {_camera.fieldOfView:F1}", Color.cyan);
				y += 20f;
				Draw(y, $"centre  {Format(pivot)}   radius {Vector3.Distance(view.position, pivot):F2}", Color.cyan);
				y += 20f;
			}

			float now = Time.unscaledTime;
			for (int index = _notices.Count - 1; index >= 0; index--)
			{
				if (_notices[index].Expires <= now) _notices.RemoveAt(index);
			}

			for (int index = 0; index < _notices.Count; index++)
			{
				Draw(y, _notices[index].Text, _notices[index].Colour);
				y += 20f;
			}

			GUI.color = Color.white;
		}

		private static string Format(Vector3 v)
		{
			return $"({v.x,7:F2},{v.y,7:F2},{v.z,7:F2})";
		}

		/// <summary>Drawn twice, offset by a pixel, so it reads on any background.</summary>
		private static void Draw(float y, string text, Color colour)
		{
			GUI.color = Color.black;
			GUI.Label(new Rect(11f, y + 1f, 900f, 24f), text);
			GUI.color = colour;
			GUI.Label(new Rect(10f, y, 900f, 24f), text);
		}

		/// <summary>
		/// The crosshair, drawn to match the one in character creation.
		///
		/// That one is a mesh on a CameraTarget object parented to the camera, with
		/// a four-pointed star texture. The object belongs to the character
		/// creation scene and is not loaded during a conversation, so the shape is
		/// generated here instead: an astroid, |x|^(1/2) + |y|^(1/2) &lt;= 1, which is
		/// the concave four-point star that texture uses.
		/// </summary>
		private static Texture2D? _crosshair;
		private static Texture2D? _solid;

		private static Texture2D Crosshair()
		{
			if (_crosshair != null) return _crosshair;

			const int size = 64;
			const float half = size * 0.5f;

			Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
			texture.hideFlags = HideFlags.HideAndDontSave;
			texture.filterMode = FilterMode.Bilinear;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					//Normalised to [-1, 1], with a half-pixel offset so the shape is
					//symmetrical about the centre rather than a pixel off
					float u = Mathf.Abs((x + 0.5f - half) / half);
					float v = Mathf.Abs((y + 0.5f - half) / half);

					float inside = Mathf.Sqrt(u) + Mathf.Sqrt(v);

					//Fade the last fraction so the points do not look ragged
					float alpha = Mathf.Clamp01((1f - inside) * 6f);
					texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
				}
			}

			texture.Apply();
			_crosshair = texture;
			return texture;
		}

		/// <summary>
		/// Outline the message frame, so the area that still advances the dialogue
		/// under TextAreaOnly can be seen rather than guessed at.
		/// </summary>
		private void DrawTextArea()
		{
			ADV.TextScenario? scenario = Scenario();
			if (scenario == null) return;

			Rect area;
			if (MessageWindowRect(scenario!, out area) == false) return;

			//GUI measures y from the top of the screen, everything else from the bottom
			float top = Screen.height - area.yMax;
			const float thickness = 2f;

			GUI.color = new Color(0f, 1f, 0.4f, 0.9f);
			GUI.DrawTexture(new Rect(area.xMin, top, area.width, thickness), Solid());
			GUI.DrawTexture(new Rect(area.xMin, top + area.height - thickness, area.width, thickness), Solid());
			GUI.DrawTexture(new Rect(area.xMin, top, thickness, area.height), Solid());
			GUI.DrawTexture(new Rect(area.xMax - thickness, top, thickness, area.height), Solid());
			GUI.color = Color.white;
		}

		/// <summary>
		/// One white pixel, stretched to draw the outline with.
		/// </summary>
		private static Texture2D Solid()
		{
			if (_solid != null) return _solid;

			Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			texture.hideFlags = HideFlags.HideAndDontSave;
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();
			_solid = texture;
			return texture;
		}


		private static void DrawCrosshair()
		{
			float size = 26f;
			float x = (Screen.width * 0.5f) - (size * 0.5f);
			float y = (Screen.height * 0.5f) - (size * 0.5f);

			GUI.color = Color.white;
			GUI.DrawTexture(new Rect(x, y, size, size), Crosshair());
		}



		/*INITIALIZATION*/
		private void Start()
		{
			Logging.LogInfo($"Ready. Press {ADVFreeCameraPlugin.toggleKey.Value} during a conversation.");
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				if (IsActive) Deactivate("component destroyed");

				if (_pivot != null)
				{
					Destroy(_pivot);
					_pivot = null;
				}

				Instance = null;
			}
		}

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
			}
			else
			{
				//Call Destroy() on the component, not the GameObject
				Destroy(this);
			}
		}
	}
}
