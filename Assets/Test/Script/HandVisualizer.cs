using UnityEngine;
using UnityEngine.UI;
using Klak.TestTools;
using MediaPipe.HandPose;

public sealed class HandVisualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] ImageSource _source = null;
    [Space]
    [SerializeField] ResourceSet _resources = null;
    [SerializeField] Shader _keyPointShader = null;
    [SerializeField] Shader _handRegionShader = null;
    [Space]
    [SerializeField] RawImage _mainUI = null;
    [SerializeField] RawImage _cropUI = null;

    [Space]
    [Header("美術素材")]
    [SerializeField] Texture2D _seedTexture = null;
    [SerializeField] Color _seedTint = Color.white;
    [SerializeField] Vector2 _seedSize = new Vector2(0.16f, 0.16f);
    [SerializeField] bool _autoLoadSeedFromResources = true;
    [SerializeField] string _seedResourceName = "Seeds_Cereals";

    [SerializeField] Texture2D _soilTexture = null;
    [SerializeField] bool _useSoilTexture = false;
    [SerializeField] bool _autoLoadSoilFromResources = true;
    [SerializeField, Range(1, 12)] float _soilTiling = 4;
    [SerializeField] bool _enableGrassOverlay = false;
    [SerializeField] Texture2D _grassTexture = null;
    [SerializeField] bool _autoLoadGrassFromResources = true;
    [SerializeField] string _grassResourceName = "GrassPatch";
    [SerializeField] Color _grassTint = new Color(0.58f, 0.83f, 0.42f, 1);

    [Space]
    [Header("終章儀式（雙手）")]
    [SerializeField, Range(0.3f, 2.5f)] float _placeHoldSeconds = 1.0f;
    [SerializeField, Range(0.3f, 3.0f)] float _placeDropAnimationSeconds = 1.4f;
    [SerializeField, Range(2, 6)] int _placeRequiredDetections = 3;
    [SerializeField, Range(0.05f, 2.0f)] float _maxHandSpeedForPlace = 0.45f;
    [SerializeField, Range(0.0f, 0.4f)] float _placementHeightLimit = 0.10f;
    [SerializeField, Range(1.0f, 1.8f)] float _openPalmRatioThreshold = 1.22f;
    [SerializeField, Range(0.0f, 0.3f)] float _edgeOnThresholdRelax = 0.14f;
    [SerializeField, Range(0.0f, 0.2f)] float _openPalmHysteresis = 0.08f;
    [SerializeField, Range(0.02f, 0.4f)] float _liftStartDistance = 0.10f;
    [SerializeField, Range(0.08f, 0.6f)] float _liftFullDistance = 0.30f;
    [SerializeField, Range(0.003f, 0.08f)] float _placeDetectDownDistance = 0.018f;
    [SerializeField, Range(0.002f, 0.06f)] float _placeRearmLiftDistance = 0.010f;
    [SerializeField, Range(0.10f, 1.2f)] float _placeMinBeatInterval = 0.32f;
    [SerializeField, Range(0.03f, 0.6f)] float _placeMinStrokeSeconds = 0.14f;
    [SerializeField, Range(0.15f, 2.5f)] float _placeMaxStrokeSpeed = 0.75f;
    [SerializeField, Range(0.02f, 0.8f)] float _gestureLostGraceSeconds = 0.28f;
    [SerializeField, Range(0.02f, 1.0f)] float _placementDecayPerSecond = 0.35f;
    [SerializeField, Range(0.05f, 0.6f)] float _centerSmoothing = 0.20f;
    [SerializeField, Range(0.3f, 2.0f)] float _rhythmTextShowSeconds = 0.75f;
    [SerializeField] bool _enableRhythmAudio = true;
    [SerializeField, Range(0f, 1f)] float _rhythmAudioVolume = 0.9f;
    [SerializeField] AudioClip _rhythmCount1Clip = null;
    [SerializeField] AudioClip _rhythmCount2Clip = null;
    [SerializeField] AudioClip _rhythmCount3Clip = null;
    [SerializeField] AudioClip _rhythmFallbackClip = null;
    [SerializeField, Range(220f, 1600f)] float _rhythmFallbackBeepHz = 880f;
    [SerializeField, Range(0.04f, 0.35f)] float _rhythmFallbackBeepSeconds = 0.11f;

    [SerializeField] bool _showCropPreview = false;
    [SerializeField] bool _drawLandmarks = true;
    [SerializeField] bool _invertY = false;

    #endregion

    #region Private members

    const int DualHandCount = 2;
    static readonly int[] TipIndices = { 4, 8, 12, 16, 20 };
    static readonly int[] PipIndices = { 3, 6, 10, 14, 18 };

    HandPipeline _pipeline;
    (Material keys, Material region) _material;

    Material _seedMaterial;
    Material _sproutMaterial;
    Material _bloomMaterial;
    Material _groundMaterial;
    Material _grassMaterial;
    Texture2D _runtimeGroundTexture;

    GameObject _seedObject;
    GameObject _sproutObject;
    GameObject _bloomObject;
    GameObject _groundObject;
    GameObject _grassObject;

    Camera _mainCamera;
    AudioSource _rhythmAudioSource;
    AudioClip _runtimeRhythmBeepClip;

    enum RitualState
    {
        WaitingPlace,
        Placing,
        PlaceAnimating,
        WaitingGrow,
        Growing,
        Bloomed
    }

    struct HandSignal
    {
        public bool tracked;
        public Vector2 center;
        public float speed;
        public bool openPalm;
    }

    RitualState _state;
    float _placeTimer;
    float _growth;
    const float SeedFixedX = 0f;
    readonly float _seedGroundY = -0.33f;
    float _referenceYLeft;
    float _referenceYRight;
    float _placingMaxDrop;
    float _placeLostTimer;
    float _placeAnimTimer;
    int _placeDetectedCount;
    bool _placeNeedRearm;
    float _placeStrokeTimer;
    float _placeBeatCooldown;
    string _rhythmText = "";
    float _rhythmTextTimer;

    HandSignal[] _hands;
    bool[] _hasPrevCenter;
    Vector2[] _prevCenter;
    bool[] _hasSmoothedCenter;
    Vector2[] _smoothedCenter;
    float[] _smoothedSpeed;
    float[] _opennessRatio;
    int[] _extendedCount;
    bool[] _openPalmLatch;

    string _status = "";

    #endregion

    #region MonoBehaviour implementation

    void Awake()
    {
        EnsureRuntimeArrays();
        EnsureAudioListenerExists();
    }

    void Start()
    {
        _mainCamera = Camera.main;
        EnsureRuntimeArrays();
        EnsureRhythmAudioSource();

        _pipeline = new HandPipeline(_resources);
        _material = (new Material(_keyPointShader), new Material(_handRegionShader));

        _material.keys.SetBuffer("_KeyPoints", _pipeline.KeyPointBuffer);
        _material.region.SetBuffer("_Image", _pipeline.HandRegionCropBuffer);

        if (_cropUI != null)
        {
            _cropUI.material = _material.region;
            _cropUI.gameObject.SetActive(_showCropPreview);
        }

        if (_mainUI != null)
            _mainUI.color = new Color(1, 1, 1, 0.78f);

        if (_seedTexture == null && _autoLoadSeedFromResources)
            _seedTexture = Resources.Load<Texture2D>(_seedResourceName);

        if (_soilTexture == null && _autoLoadSoilFromResources)
            _soilTexture = TryLoadResourceTexture("Ground091_2K_Color", "Ground091_1K_Color", "Ground091_Color", "Ground091");
        if (_enableGrassOverlay && _grassTexture == null && _autoLoadGrassFromResources)
            _grassTexture = TryLoadResourceTexture(_grassResourceName, "GrassPatch");
        if (_enableGrassOverlay && _grassTexture == null && _autoLoadGrassFromResources)
        {
            var grassSprites = Resources.LoadAll<Sprite>(_grassResourceName);
            if (grassSprites != null && grassSprites.Length > 0 && grassSprites[0] != null)
                _grassTexture = grassSprites[0].texture;
        }

        _state = RitualState.WaitingPlace;
        _status = "請伸出雙手，準備開始儀式。";

        CreateRitualObjects();
    }

    void OnDestroy()
    {
        _pipeline?.Dispose();

        if (_material.keys != null) Destroy(_material.keys);
        if (_material.region != null) Destroy(_material.region);

        if (_seedMaterial != null) Destroy(_seedMaterial);
        if (_sproutMaterial != null) Destroy(_sproutMaterial);
        if (_bloomMaterial != null) Destroy(_bloomMaterial);
        if (_groundMaterial != null) Destroy(_groundMaterial);
        if (_grassMaterial != null) Destroy(_grassMaterial);

        if (_seedObject != null) Destroy(_seedObject);
        if (_sproutObject != null) Destroy(_sproutObject);
        if (_bloomObject != null) Destroy(_bloomObject);
        if (_groundObject != null) Destroy(_groundObject);
        if (_grassObject != null) Destroy(_grassObject);

        if (_runtimeRhythmBeepClip != null) Destroy(_runtimeRhythmBeepClip);
        if (_runtimeGroundTexture != null) Destroy(_runtimeGroundTexture);
    }

    void LateUpdate()
    {
        if (_pipeline == null || _source == null || _source.Texture == null) return;

        if (_rhythmTextTimer > 0)
            _rhythmTextTimer = Mathf.Max(0, _rhythmTextTimer - Time.deltaTime);

        _pipeline.ProcessImage(_source.Texture);

        if (_mainUI != null) _mainUI.texture = _source.Texture;
        if (_cropUI != null) _cropUI.texture = _source.Texture;

        for (var i = 0; i < DualHandCount; i++)
            _hands[i] = EvaluateHand(i);

        UpdateStateMachine(_hands[0], _hands[1]);
        UpdateRitualVisuals();
    }

    void OnRenderObject()
    {
        if (!_drawLandmarks || _material.keys == null || _pipeline == null) return;

        for (var hand = 0; hand < DualHandCount; hand++)
        {
            if (!_pipeline.IsHandTracked(hand)) continue;

            _material.keys.SetInt("_KeyPointOffset", hand * HandPipeline.KeyPointCount);

            _material.keys.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 96, 21);

            _material.keys.SetPass(1);
            Graphics.DrawProceduralNow(MeshTopology.Lines, 2, 4 * 5 + 1);
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(20, 20, 1040, 132), "");

        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = new Color(0.85f, 0.95f, 0.95f, 1) }
        };

        GUI.Label
        (
            new Rect(35, 34, 990, 34),
            $"終章：安放與綻放（雙手） | {_status}",
            titleStyle
        );

        var trackedCount = _pipeline != null ? _pipeline.TrackedHandCount : 0;

        GUI.Label
        (
            new Rect(35, 72, 990, 28),
            $"追蹤: {trackedCount}/2 | 左手 開掌比: {_opennessRatio[0]:F2} 伸指: {_extendedCount[0]}/5 速度: {_smoothedSpeed[0]:F2} 開掌: {(_openPalmLatch[0] ? "是" : "否")}",
            hintStyle
        );

        GUI.Label
        (
            new Rect(35, 98, 990, 28),
            $"追蹤: {trackedCount}/2 | 右手 開掌比: {_opennessRatio[1]:F2} 伸指: {_extendedCount[1]}/5 速度: {_smoothedSpeed[1]:F2} 開掌: {(_openPalmLatch[1] ? "是" : "否")} | 下移次數: {_placeDetectedCount}/{Mathf.Max(1, _placeRequiredDetections)}（按 R 重置）",
            hintStyle
        );

        if (_rhythmTextTimer > 0 && !string.IsNullOrEmpty(_rhythmText))
        {
            var alpha = Mathf.Clamp01(_rhythmTextTimer / Mathf.Max(_rhythmTextShowSeconds, 0.001f));
            var rhythmStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.96f, 0.72f, alpha) }
            };

            GUI.Label
            (
                new Rect((Screen.width - 640) * 0.5f, Screen.height * 0.16f, 640, 80),
                _rhythmText,
                rhythmStyle
            );
        }
    }

    #endregion

    #region Ritual logic

    HandSignal EvaluateHand(int handIndex)
    {
        if (handIndex < 0 || handIndex >= DualHandCount) return default;

        var pipelineTracked = _pipeline.IsHandTracked(handIndex);

        if (!pipelineTracked)
        {
            _hasPrevCenter[handIndex] = false;
            _hasSmoothedCenter[handIndex] = false;
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            _openPalmLatch[handIndex] = false;
            return default;
        }

        Vector3 Get3D(int index)
        {
            var p = _pipeline.GetKeyPoint(handIndex, index);
            if (_invertY) p.y = -p.y;
            return p;
        }

        var wrist = Get3D(0);
        var indexMcp = Get3D(5);
        var middleMcp = Get3D(9);
        var ringMcp = Get3D(13);
        var pinkyMcp = Get3D(17);

        var center3 = (wrist + indexMcp + middleMcp + ringMcp + pinkyMcp) / 5.0f;
        var center = new Vector2(center3.x, center3.y);

        var palmSpan = Vector3.Distance(indexMcp, pinkyMcp);
        var palmLength = Vector3.Distance(wrist, middleMcp);
        var tracked = palmSpan > 0.035f && palmLength > 0.035f;

        if (!tracked)
        {
            _hasPrevCenter[handIndex] = false;
            _hasSmoothedCenter[handIndex] = false;
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            _openPalmLatch[handIndex] = false;
            return default;
        }

        if (_hasSmoothedCenter[handIndex])
            center = Vector2.Lerp(_smoothedCenter[handIndex], center, Mathf.Clamp01(_centerSmoothing));

        _smoothedCenter[handIndex] = center;
        _hasSmoothedCenter[handIndex] = true;

        if (_hasPrevCenter[handIndex])
        {
            var speed = Vector2.Distance(center, _prevCenter[handIndex]) / Mathf.Max(Time.deltaTime, 0.0001f);
            _smoothedSpeed[handIndex] = Mathf.Lerp(_smoothedSpeed[handIndex], speed, 0.25f);
        }
        else
        {
            _smoothedSpeed[handIndex] = 0;
        }

        _prevCenter[handIndex] = center;
        _hasPrevCenter[handIndex] = true;

        var tipAvg = 0.0f;
        var pipAvg = 0.0f;
        var extended = 0;

        for (var i = 0; i < TipIndices.Length; i++)
        {
            var tip = Get3D(TipIndices[i]);
            var pip = Get3D(PipIndices[i]);

            var tipDist = Vector3.Distance(tip, wrist);
            var pipDist = Vector3.Distance(pip, wrist);

            tipAvg += tipDist;
            pipAvg += pipDist;

            if (tipDist > pipDist * 1.07f) extended++;
        }

        tipAvg /= TipIndices.Length;
        pipAvg /= TipIndices.Length;

        _extendedCount[handIndex] = extended;
        _opennessRatio[handIndex] = tipAvg / Mathf.Max(pipAvg, 0.001f);

        // 水平手勢抗性：手掌越側向，門檻越放寬
        var palmNormal = Vector3.Cross(indexMcp - wrist, pinkyMcp - wrist);
        var palmNormalN = palmNormal.sqrMagnitude > 1e-6f ? palmNormal.normalized : Vector3.forward;
        var edgeOn = Mathf.Clamp01(1.0f - Mathf.Abs(palmNormalN.z));
        var adaptiveThreshold = _openPalmRatioThreshold - _edgeOnThresholdRelax * edgeOn;

        // 動作過程抗抖：手速變快時，稍微放寬開掌門檻，避免一下就掉追蹤
        var speedRelax = Mathf.Clamp01(_smoothedSpeed[handIndex] / Mathf.Max(_maxHandSpeedForPlace, 0.0001f));
        adaptiveThreshold -= 0.10f * speedRelax;

        // 防抖：進入/退出不同門檻
        var openEnter = _opennessRatio[handIndex] >= adaptiveThreshold && extended >= 3;
        var openExit = _opennessRatio[handIndex] >= (adaptiveThreshold - _openPalmHysteresis) && extended >= 2;

        _openPalmLatch[handIndex] = _openPalmLatch[handIndex] ? openExit : openEnter;

        return new HandSignal
        {
            tracked = true,
            center = center,
            speed = _smoothedSpeed[handIndex],
            openPalm = _openPalmLatch[handIndex]
        };
    }

    bool IsPlacementReady(HandSignal hand)
      => hand.tracked && hand.openPalm;

    void UpdateStateMachine(HandSignal left, HandSignal right)
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRitual();
            return;
        }

        var bothPlacement = IsPlacementReady(left) && IsPlacementReady(right);

        switch (_state)
        {
            case RitualState.WaitingPlace:
                _status = $"雙手維持開掌，向下偵測 {Mathf.Max(1, _placeRequiredDetections)} 次後安放種子。";
                _placeTimer = Mathf.Max(0, _placeTimer - Time.deltaTime * 2);
                if (bothPlacement)
                {
                    _state = RitualState.Placing;
                    _referenceYLeft = left.center.y;
                    _referenceYRight = right.center.y;
                    _placingMaxDrop = 0;
                    _placeLostTimer = 0;
                    _placeAnimTimer = 0;
                    _placeNeedRearm = false;
                    _placeStrokeTimer = 0;
                    _placeBeatCooldown = 0;
                    _placeTimer = 0;
                }
                break;

            case RitualState.Placing:
                _status = $"安放手勢中…請向下 {Mathf.Max(0, _placeRequiredDetections - _placeDetectedCount)} 次。";
                _placeBeatCooldown = Mathf.Max(0, _placeBeatCooldown - Time.deltaTime);
                if (bothPlacement)
                {
                    if (_placeLostTimer > 0)
                    {
                        _referenceYLeft = left.center.y;
                        _referenceYRight = right.center.y;
                        _placingMaxDrop = 0;
                        _placeNeedRearm = false;
                        _placeStrokeTimer = 0;
                    }

                    _placeLostTimer = 0;

                    if (!_placeNeedRearm)
                    {
                        _placeStrokeTimer += Time.deltaTime;

                        var dropLeft = _referenceYLeft - left.center.y;
                        var dropRight = _referenceYRight - right.center.y;
                        var drop = Mathf.Max(dropLeft, dropRight);

                        _placingMaxDrop = Mathf.Max(_placingMaxDrop, drop);

                        if (_placingMaxDrop >= _placeDetectDownDistance)
                        {
                            var strokeSpeed = Mathf.Max(left.speed, right.speed);
                            var tooFast =
                                _placeStrokeTimer < _placeMinStrokeSeconds ||
                                strokeSpeed > _placeMaxStrokeSpeed ||
                                _placeBeatCooldown > 0;

                            if (tooFast)
                            {
                                _status = "太快了，請放慢節奏再向下。";
                                _rhythmText = "太快";
                                _rhythmTextTimer = Mathf.Min(0.45f, _rhythmTextShowSeconds);
                                _placeNeedRearm = true;
                                _placingMaxDrop = 0;
                                _referenceYLeft = left.center.y;
                                _referenceYRight = right.center.y;
                                _placeStrokeTimer = 0;
                                break;
                            }

                            _placeDetectedCount++;
                            var isLastBeat = _placeDetectedCount >= Mathf.Max(1, _placeRequiredDetections);
                            TriggerRhythmCue(_placeDetectedCount, isLastBeat);
                            _placeNeedRearm = true;
                            _placingMaxDrop = 0;
                            _referenceYLeft = left.center.y;
                            _referenceYRight = right.center.y;
                            _placeStrokeTimer = 0;
                            _placeBeatCooldown = _placeMinBeatInterval;

                            if (isLastBeat)
                            {
                                _state = RitualState.PlaceAnimating;
                                _placeAnimTimer = 0;
                                _status = "偵測完成，種子安放中…";
                            }
                        }
                    }
                    else
                    {
                        var liftLeft = left.center.y - _referenceYLeft;
                        var liftRight = right.center.y - _referenceYRight;
                        var lift = Mathf.Max(liftLeft, liftRight);

                        if (lift >= _placeRearmLiftDistance)
                        {
                            _placeNeedRearm = false;
                            _referenceYLeft = left.center.y;
                            _referenceYRight = right.center.y;
                            _placeStrokeTimer = 0;
                        }
                    }
                }
                else
                {
                    _placeLostTimer += Time.deltaTime;
                    _placeTimer = Mathf.Max(0, _placeTimer - Time.deltaTime * _placementDecayPerSecond);

                    if (_placeLostTimer >= _gestureLostGraceSeconds)
                        _status = "偵測中斷（進度保留），雙手回到畫面可繼續。";
                    else
                        _status = "偵測短暫中斷（進度保留），雙手回到畫面可繼續。";
                }
                break;

            case RitualState.PlaceAnimating:
                _status = "安放動畫中…種子正在慢慢落地。";
                _placeAnimTimer += Time.deltaTime;
                var placeAnimDuration = Mathf.Max(_placeDropAnimationSeconds, 0.01f);
                var placeAnimT = Mathf.Clamp01(_placeAnimTimer / placeAnimDuration);
                _placeTimer = placeAnimT * _placeHoldSeconds;

                if (placeAnimT >= 0.995f)
                {
                    _placeTimer = _placeHoldSeconds;
                    _state = RitualState.WaitingGrow;

                    if (left.tracked) _referenceYLeft = left.center.y;
                    if (right.tracked) _referenceYRight = right.center.y;

                    _status = "安放完成，請雙手向上托舉滋養種子。";
                }
                break;

            case RitualState.WaitingGrow:
                _status = "雙手向上托舉，開始發芽。";
                if (left.tracked && right.tracked)
                {
                    var liftLeft = left.center.y - _referenceYLeft;
                    var liftRight = right.center.y - _referenceYRight;
                    var lift = Mathf.Min(liftLeft, liftRight);
                    var target = Mathf.InverseLerp(_liftStartDistance, _liftFullDistance, lift);
                    _growth = Mathf.Max(_growth, target);

                    if (_growth > 0.01f) _state = RitualState.Growing;
                }
                break;

            case RitualState.Growing:
                _status = "生長中…請持續雙手向上托舉。";
                if (left.tracked && right.tracked)
                {
                    var liftLeft = left.center.y - _referenceYLeft;
                    var liftRight = right.center.y - _referenceYRight;
                    var lift = Mathf.Min(liftLeft, liftRight);
                    var target = Mathf.InverseLerp(_liftStartDistance, _liftFullDistance, lift);
                    _growth = Mathf.Clamp01(Mathf.Max(_growth, target));
                }

                if (_growth >= 0.995f)
                {
                    _growth = 1;
                    _state = RitualState.Bloomed;
                    _status = "綻放完成！";
                }
                break;

            case RitualState.Bloomed:
                _status = "已綻放！按 R 可重新開始。";
                break;
        }
    }

    void ResetRitual()
    {
        _state = RitualState.WaitingPlace;
        _placeTimer = 0;
        _growth = 0;
        _referenceYLeft = 0;
        _referenceYRight = 0;
        _placingMaxDrop = 0;
        _placeLostTimer = 0;
        _placeAnimTimer = 0;
        _placeDetectedCount = 0;
        _placeNeedRearm = false;
        _placeStrokeTimer = 0;
        _placeBeatCooldown = 0;
        _rhythmText = "";
        _rhythmTextTimer = 0;

        for (var i = 0; i < _openPalmLatch.Length; i++)
        {
            _openPalmLatch[i] = false;
            _hasPrevCenter[i] = false;
            _hasSmoothedCenter[i] = false;
            _smoothedSpeed[i] = 0;
        }

        _status = "已重置，請再次伸出雙手。";
    }

    void EnsureRhythmAudioSource()
    {
        EnsureAudioListenerExists();

        _rhythmAudioSource = GetComponent<AudioSource>();
        if (_rhythmAudioSource == null)
            _rhythmAudioSource = gameObject.AddComponent<AudioSource>();

        _rhythmAudioSource.playOnAwake = false;
        _rhythmAudioSource.loop = false;
        _rhythmAudioSource.spatialBlend = 0;
        _rhythmAudioSource.volume = Mathf.Clamp01(_rhythmAudioVolume);

        if (_runtimeRhythmBeepClip == null)
            _runtimeRhythmBeepClip = BuildRuntimeBeepClip();
    }

    void EnsureAudioListenerExists()
    {
        var listeners = FindObjectsOfType<AudioListener>(true);
        if (listeners != null && listeners.Length > 0) return;

        var host = _mainCamera != null ? _mainCamera.gameObject : gameObject;
        if (host.GetComponent<AudioListener>() == null)
            host.AddComponent<AudioListener>();
    }

    AudioClip GetRhythmClip(int count)
    {
        if (count == 1 && _rhythmCount1Clip != null) return _rhythmCount1Clip;
        if (count == 2 && _rhythmCount2Clip != null) return _rhythmCount2Clip;
        if (count >= 3 && _rhythmCount3Clip != null) return _rhythmCount3Clip;
        if (_rhythmFallbackClip != null) return _rhythmFallbackClip;
        return _runtimeRhythmBeepClip;
    }

    AudioClip BuildRuntimeBeepClip()
    {
        const int sampleRate = 44100;
        var duration = Mathf.Clamp(_rhythmFallbackBeepSeconds, 0.04f, 0.35f);
        var freq = Mathf.Clamp(_rhythmFallbackBeepHz, 220f, 1600f);
        var samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
        var data = new float[samples];

        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)sampleRate;
            var envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / Mathf.Max(1f, samples - 1f)));
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.32f;
        }

        var clip = AudioClip.Create("RhythmFallbackBeep", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void TriggerRhythmCue(int count, bool finalBeat)
    {
        var clampedCount = Mathf.Clamp(count, 1, Mathf.Max(1, _placeRequiredDetections));
        _rhythmText = finalBeat ? $"第{clampedCount}次，安放完成！" : $"第{clampedCount}次";
        _rhythmTextTimer = _rhythmTextShowSeconds;

        if (!_enableRhythmAudio || _rhythmAudioSource == null) return;

        _rhythmAudioSource.volume = Mathf.Clamp01(_rhythmAudioVolume);
        _rhythmAudioSource.pitch = finalBeat ? 1.28f : 1.0f + 0.12f * (clampedCount - 1);
        var clip = GetRhythmClip(clampedCount);
        if (clip != null)
            _rhythmAudioSource.PlayOneShot(clip, Mathf.Clamp01(_rhythmAudioVolume));
    }

    #endregion

    #region Visual objects

    void EnsureRuntimeArrays()
    {
        if (_hands == null || _hands.Length != DualHandCount)
            _hands = new HandSignal[DualHandCount];

        if (_hasPrevCenter == null || _hasPrevCenter.Length != DualHandCount)
            _hasPrevCenter = new bool[DualHandCount];

        if (_prevCenter == null || _prevCenter.Length != DualHandCount)
            _prevCenter = new Vector2[DualHandCount];

        if (_hasSmoothedCenter == null || _hasSmoothedCenter.Length != DualHandCount)
            _hasSmoothedCenter = new bool[DualHandCount];

        if (_smoothedCenter == null || _smoothedCenter.Length != DualHandCount)
            _smoothedCenter = new Vector2[DualHandCount];

        if (_smoothedSpeed == null || _smoothedSpeed.Length != DualHandCount)
            _smoothedSpeed = new float[DualHandCount];

        if (_opennessRatio == null || _opennessRatio.Length != DualHandCount)
            _opennessRatio = new float[DualHandCount];

        if (_extendedCount == null || _extendedCount.Length != DualHandCount)
            _extendedCount = new int[DualHandCount];

        if (_openPalmLatch == null || _openPalmLatch.Length != DualHandCount)
            _openPalmLatch = new bool[DualHandCount];
    }

    Texture2D TryLoadResourceTexture(params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var tex = Resources.Load<Texture2D>(names[i]);
            if (tex != null) return tex;
        }

        return null;
    }

    void ApplyMainTexture(Material mat, Texture tex)
    {
        if (mat == null || tex == null) return;

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    }

    void ApplyMainColor(Material mat, Color color)
    {
        if (mat == null) return;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }

    void SetMainTextureTiling(Material mat, Vector2 tiling)
    {
        if (mat == null) return;

        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
        if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
    }

    void SetMainTextureOffset(Material mat, Vector2 offset)
    {
        if (mat == null) return;

        if (mat.HasProperty("_BaseMap")) mat.SetTextureOffset("_BaseMap", offset);
        if (mat.HasProperty("_MainTex")) mat.SetTextureOffset("_MainTex", offset);
    }

    Material CreateLitMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, color);
        return mat;
    }

    Material CreateGroundMaterial()
    {
        var baseColor = new Color(0.46f, 0.30f, 0.17f, 1);

        if (_useSoilTexture && _soilTexture != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            ApplyMainColor(mat, baseColor);
            ApplyMainTexture(mat, _soilTexture);
            SetMainTextureTiling(mat, new Vector2(_soilTiling, _soilTiling));
            return mat;
        }
        else
        {
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            ApplyMainTexture(mat, GetOrCreateProceduralSoilTexture(baseColor));
            return mat;
        }
    }

    Texture2D GetOrCreateProceduralSoilTexture(Color baseColor)
    {
        if (_runtimeGroundTexture != null) return _runtimeGroundTexture;

        const int size = 256;
        _runtimeGroundTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _runtimeGroundTexture.wrapMode = TextureWrapMode.Repeat;
        _runtimeGroundTexture.filterMode = FilterMode.Bilinear;

        for (var y = 0; y < size; y++)
        {
            var v = y / (float)(size - 1);
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)(size - 1);

                var n1 = Mathf.PerlinNoise(u * 5.6f + 0.2f, v * 5.6f + 0.7f);
                var n2 = Mathf.PerlinNoise(u * 13.5f + 1.2f, v * 13.5f + 2.1f);
                var n3 = Mathf.PerlinNoise(u * 33.0f + 3.8f, v * 33.0f + 4.6f);

                var grain = n1 * 0.60f + n2 * 0.30f + n3 * 0.10f;
                var shade = Mathf.Lerp(0.68f, 1.22f, grain);

                if (n3 > 0.78f) shade *= 0.78f;      // 深色土粒
                else if (n3 < 0.16f) shade *= 1.10f; // 淺色細粒

                var c = baseColor * shade;
                c.r = Mathf.Clamp01(c.r);
                c.g = Mathf.Clamp01(c.g);
                c.b = Mathf.Clamp01(c.b);
                c.a = 1;

                _runtimeGroundTexture.SetPixel(x, y, c);
            }
        }

        _runtimeGroundTexture.Apply(false, false);
        return _runtimeGroundTexture;
    }

    Material CreateGrassMaterial()
    {
        var shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, _grassTint);

        if (_grassTexture != null)
        {
            ApplyMainTexture(mat, _grassTexture);
            SetMainTextureTiling(mat, Vector2.one);
            SetMainTextureOffset(mat, Vector2.zero);
        }
        else
        {
            Debug.LogWarning("HandVisualizer: 沒有載入到草地貼圖，將使用純色草地。");
            if (_soilTexture != null) ApplyMainTexture(mat, _soilTexture);
        }

        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0);     // 雙面顯示
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0); // 透明草地避免深度遮擋
        mat.renderQueue = 3001;

        return mat;
    }

    Material CreateSeedMaterial()
    {
        var shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, _seedTint);
        ApplyMainTexture(mat, _seedTexture);

        return mat;
    }

    void CreateRitualObjects()
    {
        _groundMaterial = CreateGroundMaterial();
        if (_enableGrassOverlay)
            _grassMaterial = CreateGrassMaterial();
        _sproutMaterial = CreateLitMaterial(new Color(0.52f, 0.90f, 0.50f, 1));
        _bloomMaterial = CreateLitMaterial(new Color(0.98f, 0.68f, 0.88f, 1));

        _seedMaterial = CreateSeedMaterial();

        _groundObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _groundObject.name = "RitualGround";
        _groundObject.transform.position = new Vector3(0, _seedGroundY - 0.08f, 2.33f);
        _groundObject.transform.localScale = new Vector3(1.02f, 0.25f, 1f);
        _groundObject.GetComponent<MeshRenderer>().material = _groundMaterial;

        if (_enableGrassOverlay)
        {
            _grassObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _grassObject.name = "RitualGrass";
            _grassObject.transform.position = new Vector3(0, _seedGroundY - 0.15f, 2.28f);
            _grassObject.transform.localScale = new Vector3(1.00f, 0.26f, 1);
            _grassObject.GetComponent<MeshRenderer>().material = _grassMaterial;
        }

        _seedObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _seedObject.name = "Seed2D";
        _seedObject.GetComponent<MeshRenderer>().material = _seedMaterial;
        _seedObject.transform.localScale = new Vector3(_seedSize.x, _seedSize.y, 1);

        _sproutObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _sproutObject.name = "Sprout";
        _sproutObject.GetComponent<MeshRenderer>().material = _sproutMaterial;

        _bloomObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _bloomObject.name = "Bloom";
        _bloomObject.GetComponent<MeshRenderer>().material = _bloomMaterial;
        _bloomObject.transform.localScale = Vector3.one * 0.04f;
    }

    void UpdateRitualVisuals()
    {
        var dropT = Mathf.Clamp01(_placeTimer / Mathf.Max(_placeHoldSeconds, 0.0001f));
        var seedY = Mathf.Lerp(0.18f, _seedGroundY, dropT);

        if (_state == RitualState.WaitingGrow ||
            _state == RitualState.Growing ||
            _state == RitualState.Bloomed)
        {
            seedY = _seedGroundY;
        }

        var seedPos = new Vector3(SeedFixedX, seedY, 2.4f);

        if (_seedObject != null)
        {
            _seedObject.transform.position = seedPos;
            _seedObject.transform.localScale = new Vector3(_seedSize.x, _seedSize.y, 1);

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
                _seedObject.transform.rotation = Quaternion.LookRotation(-_mainCamera.transform.forward, _mainCamera.transform.up);
        }

        if (_groundObject != null)
        {
            _groundObject.transform.position = new Vector3(seedPos.x, _seedGroundY - 0.08f, 2.33f);

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
                _groundObject.transform.rotation = Quaternion.LookRotation(-_mainCamera.transform.forward, _mainCamera.transform.up);
        }

        if (_grassObject != null)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
                _grassObject.transform.rotation = Quaternion.LookRotation(-_mainCamera.transform.forward, _mainCamera.transform.up);
        }

        var stemHeight = Mathf.Lerp(0.001f, 0.28f, _growth);
        var stemScale = new Vector3(0.02f, stemHeight / 2, 0.02f);
        var stemPos = new Vector3(seedPos.x, _seedGroundY + stemHeight / 2 + 0.02f, 2.4f);

        if (_sproutObject != null)
        {
            _sproutObject.transform.position = stemPos;
            _sproutObject.transform.localScale = stemScale;
        }

        var bloomScale = Mathf.Lerp(0.02f, 0.08f, _growth);
        if (_bloomObject != null)
        {
            _bloomObject.transform.localScale = Vector3.one * bloomScale;
            _bloomObject.transform.position = new Vector3(seedPos.x, _seedGroundY + stemHeight + 0.06f, 2.4f);
        }
    }

    #endregion
}
