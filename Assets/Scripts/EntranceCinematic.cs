using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Authored first-entrance presentation; gameplay remains frozen until weapon selection owns pause.</summary>
[DefaultExecutionOrder(-200)]
public sealed class EntranceCinematic : MonoBehaviour
{
    public static bool StartupHeld { get; private set; }
    private static bool _retry;
    public const float ImpactTime = 2.7f;
    public float PlaybackTime { get; private set; }
    [SerializeField] private Transform _player;
    [SerializeField] private Transform _visual;
    [SerializeField] private Camera _camera;
    [SerializeField] private AnimationClip _entrance;
    [SerializeField] private ParticleSystem _dust;
    [SerializeField] private Transform _shadow;
    [SerializeField] private GameObject _impact;
    [SerializeField] private AudioSource _audio;
    [SerializeField] private AudioClip _whistle;
    [SerializeField] private AudioClip _thud;
    [SerializeField] private Light _fill;
    [SerializeField] private Vector3 _cameraOffset = new(1.2f, 3.7f, 1.2f);
    [SerializeField] private Vector3 _landingLookOffset = new(-.25f,-1.35f,-.9f);
    [SerializeField, Range(8f,15f)] private float _duration = 13f;
    private bool _held, _captured, _restored, _focused = true;
    private bool _audioPaused;
    private float _authoredVolume;
    private float _previousScale;
    private Vector3 _cameraPosition, _visualPosition, _readyPlayerPosition, _readyCameraPosition;
    private Quaternion _cameraRotation, _visualRotation, _readyCameraRotation;
    private float _fov;
    private Behaviour[] _behaviours;
    private bool[] _enabled;
    private Rigidbody _body;
    private bool _kinematic;
    private RigidbodyInterpolation _interpolation;
    private bool _settled;
    private Transform[] _bones;
    private Vector3[] _positions;
    private Quaternion[] _rotations;
    private Transform[] _poseTargets;
    private Vector3 _landingLookPoint;
    public bool IsFinished { get; private set; }
    public float Duration => _duration;
    public static void PrepareRetry() => _retry = true;
    public static void PrepareNewRun() => _retry = false;
    public static bool SkipHeld => (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
        || (Gamepad.current != null && Gamepad.current.startButton.isPressed);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { StartupHeld = false; _retry = false; }
    private void Awake()
    {
        _previousScale = Time.timeScale > 0 ? Time.timeScale : 1f;
        StartupHeld = true;
        _dust?.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        Time.timeScale = 0f;
        GameplayPause.SetHeld(ref _held, true);
    }
    public IEnumerator PlayEntrance()
    {
        Capture();
        bool retry = _retry; _retry = false;
        if (!retry)
        {
            float elapsed = 0f;
            bool impactPlayed = false;
            float dustTime = 0f;
            _audio?.PlayOneShot(_whistle);
            while (elapsed < _duration)
            {
                bool running = GameplayPause.LockCount <= 1 && _focused;
                if(_audio!=null)_audio.volume=_authoredVolume*(UserSettingsService.Instance!=null?UserSettingsService.Instance.SfxVolume:1f);
                if (_audio != null && _audioPaused == running)
                {
                    _audioPaused = !running;
                    if (running) _audio.UnPause(); else _audio.Pause();
                }
                if (running)
                {
                    if (SkipHeld) break;
                    elapsed += Time.unscaledDeltaTime;
                    Sample(elapsed);
                    if (elapsed >= ImpactTime && !impactPlayed)
                    {
                        impactPlayed = true;
                        _dust?.Play(true);
                        _audio?.PlayOneShot(_thud);
                    }
                    // Simulate starts a burst even on a stopped emitter. Never advance
                    // the impact systems before contact; age only from the impact beat.
                    if(impactPlayed)
                    {
                        float age=elapsed-ImpactTime;
                        _dust?.Simulate(age-dustTime,true,false,false);
                        dustTime=age;
                    }
                }
                yield return null;
            }
        }
        FinishPresentation();
        IsFinished = true;
        // Keep the pause until the selector has actually shown its own modal.
        while (SkipHeld || GameplayPause.LockCount > 1 || !_focused) yield return null;
        yield return null;
    }
    public void ReleaseForSelection()
    {
        Time.timeScale = _previousScale;
        GameplayPause.SetHeld(ref _held, false);
    }
    public void CompleteStartup()
    {
        StartupHeld=false;
        if(_settled && _body!=null)StartCoroutine(RestoreInterpolationAfterPhysics());
    }
    private IEnumerator RestoreInterpolationAfterPhysics()
    {
        yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        if(_body!=null)_body.interpolation=_interpolation;
    }
    private void Capture()
    {
        if (_captured) return;
        _captured = true;
        _dust?.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(_audio!=null)_authoredVolume=_audio.volume;
        _cameraPosition = _camera.transform.position; _cameraRotation = _camera.transform.rotation; _fov = _camera.fieldOfView;
        _visualPosition = _visual.localPosition; _visualRotation = _visual.localRotation;
        _readyPlayerPosition=_player.position;
        var collider=_player.GetComponent<Collider>();
        if(collider!=null && Physics.Raycast(_player.position+Vector3.up*5,Vector3.down,out var floor,30,~(1<<_player.gameObject.layer),QueryTriggerInteraction.Ignore))
            _readyPlayerPosition.y=floor.point.y+(_player.position.y-collider.bounds.min.y)+.005f;
        _readyCameraPosition=_cameraPosition+(_readyPlayerPosition-_player.position);_readyCameraRotation=_cameraRotation;
        var orbitCamera=_camera.GetComponent<ThirdPersonCamera>();
        if(orbitCamera!=null)
        {
            float pitch=_cameraRotation.eulerAngles.x;if(pitch>180f)pitch-=360f;
            Quaternion behind=Quaternion.Euler(Mathf.Clamp(pitch,-20f,30f),_player.eulerAngles.y,0f);
            orbitCamera.GetFollowPose(_readyPlayerPosition,behind,out _readyCameraPosition,out _readyCameraRotation);
        }
        _bones = _visual.GetComponentsInChildren<Transform>(true);
        _positions = new Vector3[_bones.Length]; _rotations = new Quaternion[_bones.Length];
        for(int i=0;i<_bones.Length;i++){_positions[i]=_bones[i].localPosition;_rotations[i]=_bones[i].localRotation;}
        _behaviours = new Behaviour[] {_player.GetComponent<PlayerAnimationDriver>(), _visual.GetComponent<Animator>(),
            _player.GetComponent<PlayerMovement>(), _player.GetComponent<WeaponManager>(), _camera.GetComponent<ThirdPersonCamera>()};
        var behaviours=new System.Collections.Generic.List<Behaviour>(_behaviours);
        behaviours.AddRange(Object.FindObjectsByType<RearThreatPresenter>(FindObjectsInactive.Include,FindObjectsSortMode.None));
        _behaviours=behaviours.ToArray();
        _enabled = new bool[_behaviours.Length];
        for(int i=0;i<_behaviours.Length;i++) if(_behaviours[i]!=null){_enabled[i]=_behaviours[i].enabled;_behaviours[i].enabled=false;}
        _body = _player.GetComponent<Rigidbody>(); if(_body!=null){_kinematic=_body.isKinematic;_interpolation=_body.interpolation;_body.interpolation=RigidbodyInterpolation.None;_body.isKinematic=true;}
        var targets=new System.Collections.Generic.List<Transform>();
        foreach(var bone in _bones)if(bone.name=="spine" || bone.name=="spine.006" || bone.name=="shin.L" || bone.name=="shin.R" || bone.name=="foot.L" || bone.name=="foot.R")targets.Add(bone);
        _poseTargets=targets.ToArray();
        _entrance.SampleAnimation(_visual.gameObject,4f);_landingLookPoint=PoseCenter();
        for(int i=0;i<_bones.Length;i++){_bones[i].localPosition=_positions[i];_bones[i].localRotation=_rotations[i];}
    }
    private Vector3 PoseCenter()
    {
        if(_poseTargets.Length==0)return _player.position+_landingLookOffset;
        Vector3 sum=Vector3.zero;foreach(var bone in _poseTargets)sum+=bone.position;
        return sum/_poseTargets.Length;
    }
    public void Sample(float time)
    {
        Capture();PlaybackTime=Mathf.Clamp(time,0,_duration);
        _entrance.SampleAnimation(_visual.gameObject, Mathf.Clamp(time,0,_duration));
        float fall = Mathf.InverseLerp(2f,2.7f,time);
        _visual.localPosition += Vector3.up * (time < 2f ? 20f : time < 2.7f ? Mathf.Lerp(13f,0f,fall*fall) : 0f);
        if(_shadow!=null){_shadow.gameObject.SetActive(time<12.8f);float size=Mathf.Lerp(.35f,1f,Mathf.Clamp01(time/2.7f));_shadow.localScale=new Vector3(size,1,size);}
        if(_impact!=null)_impact.SetActive(time>=2.7f);
        Vector3 look = time<2.7f ? _landingLookPoint : PoseCenter();
        if(_fill!=null)_fill.enabled=true;
        float recovery=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(5.25f,7f,time));
        Vector3 recoveryLook=PoseCenter();
        float top=float.NegativeInfinity,bottom=float.PositiveInfinity;
        foreach(var target in _poseTargets){top=Mathf.Max(top,target.position.y);bottom=Mathf.Min(bottom,target.position.y);}
        if(_poseTargets.Length>0)recoveryLook.y=(top+bottom)*.5f;
        look=Vector3.Lerp(look,recoveryLook,recovery);
        Quaternion overhead=Quaternion.LookRotation(-_cameraOffset);
        // Match the approved upper-half crater composition: the curled character
        // sits slightly right of centre, with empty ground above and to either side.
        look+=((overhead*Vector3.up)*.24f-(overhead*Vector3.right)*.17f)*(1f-recovery);
        Vector3 start = look + Vector3.Lerp(_cameraOffset,new Vector3(2.6f,1.8f,2.6f),recovery);
        Quaternion rotation=Quaternion.LookRotation(look-start);
        float blend=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(9.5f,_duration,time));
        // The final standing visual is rebased onto the gameplay body at completion.
        // Return to that same grounded camera pose, rather than the elevated spawn.
        Vector3 gameplayView=_readyCameraPosition;
        // Orbit around the subject instead of cutting through it on a straight dolly.
        Vector3 position=look+Vector3.Slerp(start-look,gameplayView-look,blend);
        rotation=Quaternion.LookRotation(look-position);
        _camera.transform.SetPositionAndRotation(position,Quaternion.Slerp(rotation,_readyCameraRotation,blend));
        _camera.fieldOfView=Mathf.Lerp(Mathf.Lerp(40f,48f,recovery),_fov,blend)+Mathf.Sin(blend*Mathf.PI)*10f;
        if(time>=2.7f && time<3f && !(UserSettingsService.Instance != null && (UserSettingsService.Instance.ReducedMotion || !UserSettingsService.Instance.ScreenShake)))
            _camera.transform.position += _camera.transform.right * (Mathf.Sin(time*75f)*.08f*(1f-Mathf.InverseLerp(2.7f,3f,time)));
    }
    private void FinishPresentation()
    {
        // Skip/retry use exactly the same grounded ready pose as natural completion.
        Sample(_duration);_settled=true;
        Vector3 standingVisual=_visual.position;
        // Natural finish, skip and retry all restore the same final relaxed pose.
        // Interrupted playback still restores the original captured rig instead.
        for(int index=0;index<_bones.Length;index++)
        {
            _positions[index]=_bones[index].localPosition;
            _rotations[index]=_bones[index].localRotation;
        }
        _player.position=_readyPlayerPosition;
        _visualPosition=_player.InverseTransformPoint(standingVisual);
        if(_body!=null)_body.position=_player.position;
        _cameraPosition=_readyCameraPosition;_cameraRotation=_readyCameraRotation;
        Physics.SyncTransforms();
        _player.GetComponent<PlayerMovement>()?.SynchronizeGroundedPlacement();
        RestorePresentation();
        _player.GetComponent<PlayerAnimationDriver>()?.HoldEntranceIdle();
        if(_body!=null && !_body.isKinematic){_body.linearVelocity=Vector3.zero;_body.angularVelocity=Vector3.zero;}
        Physics.SyncTransforms();
    }
    public void RestorePresentation()
    {
        if(!_captured || _restored)return;
        _restored=true;
        for(int i=0;i<_bones.Length;i++)if(_bones[i]!=null){_bones[i].localPosition=_positions[i];_bones[i].localRotation=_rotations[i];}
        if(_visual!=null)_visual.SetLocalPositionAndRotation(_visualPosition,_visualRotation);
        if(_camera!=null){_camera.transform.SetPositionAndRotation(_cameraPosition,_cameraRotation);_camera.fieldOfView=_fov;}
        for(int i=0;i<_behaviours.Length;i++)if(_behaviours[i]!=null)_behaviours[i].enabled=_enabled[i];
        if(_body!=null){_body.isKinematic=_kinematic;if(!_settled)_body.interpolation=_interpolation;}
        _dust?.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);_audio?.Stop();
        if(_shadow!=null)_shadow.gameObject.SetActive(false);
        if(_impact!=null)_impact.SetActive(false);
        if(_fill!=null)_fill.enabled=false;
    }
    private void OnApplicationFocus(bool focused){_focused=focused;if(_audio!=null){if(focused)_audio.UnPause();else _audio.Pause();}}
    private void OnDisable()
    {
        RestorePresentation();
        if(_held){GameplayPause.SetHeld(ref _held,false);if(GameplayPause.LockCount==0)Time.timeScale=_previousScale;}
        if(_body!=null)_body.interpolation=_interpolation;
        StartupHeld=false;
    }
}
