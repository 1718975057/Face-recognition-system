using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FaceRecognition
{
    /// <summary>
    /// 人脸识别核心管理器（基础部分：摄像头 + UI 叠加层）
    /// OpenCV 检测/识别功能在 FaceRecognitionManager.OpenCV.cs 中
    /// </summary>
    public partial class FaceRecognitionManager : MonoBehaviour
    {
        [Header("摄像头")]
        public int webcamIndex;
        public int webcamWidth = 640;
        public int webcamHeight = 480;
        public int targetFPS = 30;

        [Header("检测参数")]
        [Range(0.033f, 0.5f)]
        public float detectionInterval = 0.1f;
        public double scaleFactor = 1.1;
        public int minNeighbors = 6;
        public int minFaceSize = 60;
        [Range(1, 10)]
        public int maxFaces = 5;

        [Header("识别")]
        public double lbphThreshold = 80.0;         // LBPH 距离阈值（越小越严格，50-150 之间）

        [Header("UI")]
        public RawImage displayImage;
        public RectTransform overlayContainer;
        public RawImage debugImage;                // 调试用：显示 OpenCV 处理后的画面

        [Header("显示")]
        public bool debugDraw = true;
        public bool mirrorPreview = true;
        public bool flipVertical;
        [Range(0f, 1f)]
        public float smoothing = 0.85f;
        [Range(0f, 1f)]
        public float boxLingerTime = 0.5f;
        public float yOffset = 0.15f;
        public float boxExpansion = 0.3f;

        // 状态
        protected WebCamTexture _webcam;
        protected readonly List<FaceDetection> _detections = new List<FaceDetection>();
        protected readonly object _lock = new object();
        protected float _lastDetectTime;
        protected volatile bool _processing;
        protected Color32[] _pixels;
        protected int _texW, _texH;
        protected int _normW, _normH;
        protected int _rotationAngle;
        protected Color32[] _debugPixels;
        protected int _debugPixelVersion;
        protected int _lastDebugVersion;
        protected readonly object _debugLock = new object();
        protected Texture2D _debugTex;

        protected FaceDatabase _database;
        protected readonly Dictionary<int, string> _labelMap = new Dictionary<int, string>();

        // 叠加框对象池
        private readonly List<FaceBoxUI> _boxPool = new List<FaceBoxUI>();

        protected class FaceBoxUI
        {
            public GameObject root;
            public RectTransform rect;
            public Text label;
            public bool active;
            public bool claimed;
            public Vector2 currentPos;
            public Vector2 currentSize;
            public Vector2 targetPos;
            public Vector2 targetSize;
            public string targetText;
            public Color targetColor;
            public float lastSeenTime;
        }

        public FaceSystemMode Mode { get; set; } = FaceSystemMode.Recognition;

        // 能力标记
        public bool DetectionAvailable { get; protected set; }
        public string InitError { get; protected set; }
        public string DetectError { get; protected set; }
        public int DetectFrameCount { get; protected set; }

        #region 生命周期

        protected virtual void Start()
        {
            InitWebcam();
            InitOpenCV();
            LoadDatabase();
        }

        protected virtual void OnEnable()
        {
            if (_webcam != null && !_webcam.isPlaying) _webcam.Play();
        }

        protected virtual void OnDisable()
        {
            if (_webcam != null && _webcam.isPlaying) _webcam.Stop();
        }

        protected virtual void Update()
        {
            if (_webcam == null || !_webcam.isPlaying) return;

            if (_webcam.didUpdateThisFrame)
            {
                _texW = _webcam.width;
                _texH = _webcam.height;

                if (DetectionAvailable && Time.time - _lastDetectTime >= detectionInterval && !_processing)
                {
                    _lastDetectTime = Time.time;
                    _pixels = _webcam.GetPixels32();
                    _processing = true;
                    ProcessFrameAsync();
                }
            }

            RefreshOverlay();
            RefreshDebugImage();
        }

        private void RefreshDebugImage()
        {
            if (!debugDraw) return;
            var target = debugImage != null ? debugImage : displayImage;
            if (target == null) return;

            int version;
            Color32[] px;
            lock (_debugLock)
            {
                version = _debugPixelVersion;
                if (version == _lastDebugVersion) return;
                px = _debugPixels;
            }
            if (px == null || _normW <= 0 || _normH <= 0) return;

            _lastDebugVersion = version;
            int w = _normW, h = _normH;

            if (_debugTex == null || _debugTex.width != w || _debugTex.height != h)
            {
                if (_debugTex != null) Destroy(_debugTex);
                _debugTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }

            _debugTex.SetPixels32(px);
            _debugTex.Apply();
            target.texture = _debugTex;
        }

        protected virtual void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region 摄像头

        protected virtual void InitWebcam()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("未检测到摄像头");
                return;
            }

            webcamIndex = Mathf.Clamp(webcamIndex, 0, devices.Length - 1);
            _webcam = new WebCamTexture(devices[webcamIndex].name, webcamWidth, webcamHeight, targetFPS);
            _webcam.Play();

            _rotationAngle = _webcam.videoRotationAngle;
            _texW = _webcam.width;
            _texH = _webcam.height;

            if (displayImage != null)
            {
                displayImage.texture = _webcam;
                if (mirrorPreview)
                    displayImage.uvRect = new UnityEngine.Rect(1, 0, -1, 1);
            }

            Debug.Log($"摄像头: {devices[webcamIndex].name} | {_texW}x{_texH} | 旋转角: {_rotationAngle}°");
        }

        public virtual void SwitchCamera()
        {
            webcamIndex = (webcamIndex + 1) % WebCamTexture.devices.Length;
            _webcam?.Stop();
            DestroyImmediate(_webcam);
            InitWebcam();
        }

        public bool IsWebcamActive => _webcam != null && _webcam.isPlaying;

        #endregion

        #region OpenCV 初始化（虚方法，子类 / partial 覆盖）

        partial void InitOpenCVPartial();
        partial void ProcessFrameAsyncPartial();
        partial void RegisterFacePartial(string name);
        partial void DeleteFacePartial(string name);
        partial void TrainFromImagesPartial();
        partial void CleanupOpenCVPartial();

        protected void InitOpenCV()
        {
            InitOpenCVPartial();
        }

        protected void ProcessFrameAsync()
        {
            ProcessFrameAsyncPartial();
        }

        #endregion

        #region 数据库

        protected virtual void LoadDatabase()
        {
            string dbPath = System.IO.Path.Combine(Application.persistentDataPath, "face_database.json");
            if (System.IO.File.Exists(dbPath))
            {
                _database = JsonUtility.FromJson<FaceDatabase>(System.IO.File.ReadAllText(dbPath)) ?? new FaceDatabase();
            }
            else
            {
                _database = new FaceDatabase();
            }

            _labelMap.Clear();
            foreach (var r in _database.Records)
                _labelMap[r.LabelId] = r.Name;

            // 确保图片目录存在
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.persistentDataPath, "FaceImages"));

            if (_database.Records.Count > 0)
                TrainFromImagesPartial();
        }

        public void SaveDatabase()
        {
            string dbPath = System.IO.Path.Combine(Application.persistentDataPath, "face_database.json");
            System.IO.File.WriteAllText(dbPath, JsonUtility.ToJson(_database, true));
        }

        #endregion

        #region UI 叠加层

        protected virtual void RefreshOverlay()
        {
            if (overlayContainer == null) return;
            if (debugDraw)
            {
                // debug 模式：隐藏所有 UI 框，画面上的绿框来自 OpenCV 像素级绘制
                foreach (var b in _boxPool)
                    if (b.root.activeSelf) b.root.SetActive(false);
                return;
            }

            List<FaceDetection> dets;
            lock (_lock) { dets = new List<FaceDetection>(_detections); }

            float cw = overlayContainer.rect.width;
            float ch = overlayContainer.rect.height;
            if (cw <= 0 || ch <= 0 || _normW <= 0 || _normH <= 0) return;

            // 使用正向化后的图像尺寸（像素已在编码前旋转修正）
            float scaleX = cw / _normW;
            float scaleY = ch / _normH;
            float now = Time.time;

            // Step 1: 重置领取标记
            foreach (var b in _boxPool) b.claimed = false;

            // Step 2: 为每个检测匹配框
            for (int i = 0; i < dets.Count; i++)
            {
                var d = dets[i];

                float h = d.BoundingBox.height * scaleY;
                float w = d.BoundingBox.width * scaleX;
                float x = d.BoundingBox.x * scaleX;
                float yBase = ch - (d.BoundingBox.y + d.BoundingBox.height) * scaleY;

                // 框体膨胀 + Y 偏移
                float expandW = h * boxExpansion;
                yBase -= h * yOffset;
                h *= (1f + boxExpansion);
                w += expandW;
                x -= expandW * 0.5f;

                if (mirrorPreview)
                    x = cw - (d.BoundingBox.x + d.BoundingBox.width) * scaleX - expandW * 0.5f;

                var tp = new Vector2(x, yBase);
                var ts = new Vector2(w, h);
                string txt = d.IsRecognized
                    ? d.Label
                    : (Mode == FaceSystemMode.Recognition ? "Unknown" : "Face");
                Color clr = d.IsRecognized ? Color.green : Color.yellow;

                // 先找最近的活跃未领取框
                FaceBoxUI best = null;
                float bestDist = 150f; // 像素阈值
                foreach (var box in _boxPool)
                {
                    if (box.claimed || !box.active) continue;
                    float dist = Vector2.Distance(box.currentPos, tp);
                    if (dist < bestDist) { bestDist = dist; best = box; }
                }

                // 再找空闲框（非活跃）
                if (best == null)
                {
                    foreach (var box in _boxPool)
                    {
                        if (box.claimed || box.active) continue;
                        best = box; break;
                    }
                }

                // 检查是否与已领取框重叠（防止同一张脸出现两个框）
                if (best == null)
                {
                    bool tooClose = false;
                    foreach (var box in _boxPool)
                    {
                        if (!box.claimed || !box.active) continue;
                        if (Vector2.Distance(box.targetPos, tp) < Mathf.Max(box.targetSize.x, box.targetSize.y) * 0.6f)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose)
                        continue; // 跳过此检测，与已有框重复
                    best = AddNewBox();
                }

                best.claimed = true;
                best.active = true;
                best.lastSeenTime = now;
                best.targetPos = tp;
                best.targetSize = ts;
                best.targetText = txt;
                best.targetColor = clr;
            }

            // Step 3: 应用平滑 + 清理过期框
            // smoothing=1 → t≈0 极慢平滑; smoothing=0 → t=1 瞬移无平滑
            float t = 1f - Mathf.Pow(smoothing, Time.deltaTime * 60f);

            foreach (var box in _boxPool)
            {
                if (box.claimed)
                {
                    // 平滑插值
                    if (smoothing > 0.001f && box.currentSize != Vector2.zero)
                    {
                        box.currentPos = Vector2.Lerp(box.currentPos, box.targetPos, t);
                        box.currentSize = Vector2.Lerp(box.currentSize, box.targetSize, t);
                    }
                    else
                    {
                        box.currentPos = box.targetPos;
                        box.currentSize = box.targetSize;
                    }

                    box.rect.anchoredPosition = box.currentPos;
                    box.rect.sizeDelta = box.currentSize;
                    box.label.text = box.targetText;
                    box.label.color = box.targetColor;
                    box.root.SetActive(true);
                }
                else if (box.active && now - box.lastSeenTime > boxLingerTime)
                {
                    box.active = false;
                    box.root.SetActive(false);
                }
                else if (!box.active)
                {
                    if (box.root.activeSelf) box.root.SetActive(false);
                }
            }
        }

        private FaceBoxUI AddNewBox()
        {
            var go = new GameObject("FaceBox");
            go.transform.SetParent(overlayContainer, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 1, 0, 0f);
            img.raycastTarget = false;

            MakeBorderLine(go.transform, "top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 2));
            MakeBorderLine(go.transform, "bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 2));
            MakeBorderLine(go.transform, "left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(2, 0));
            MakeBorderLine(go.transform, "right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(2, 0));

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(1, 1);
            lrt.pivot = new Vector2(0.5f, 0);
            lrt.anchoredPosition = new Vector2(0, 4);
            lrt.sizeDelta = new Vector2(0, 24);
            var txt = labelGO.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 14;
            txt.color = Color.green;
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.raycastTarget = false;

            var boxRT = go.GetComponent<RectTransform>();
            boxRT.anchorMin = Vector2.zero;
            boxRT.anchorMax = Vector2.zero;
            boxRT.pivot = Vector2.zero;

            var box = new FaceBoxUI
            {
                root = go,
                rect = boxRT,
                label = txt,
                currentPos = Vector2.zero,
                currentSize = Vector2.zero
            };
            _boxPool.Add(box);
            return box;
        }

        private void MakeBorderLine(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = Color.green;
            img.raycastTarget = false;
        }

        #endregion

        #region 公开 API

        public int DetectedFaceCount
        {
            get { lock (_lock) return _detections.Count; }
        }

        public List<FaceDetection> GetDetections()
        {
            lock (_lock) return new List<FaceDetection>(_detections);
        }

        public FaceDatabase GetDatabase() => _database;

        public void RegisterFace(string name)
        {
            RegisterFacePartial(name);
        }

        public void DeleteFace(string name)
        {
            DeleteFacePartial(name);
        }

        #endregion

        #region 清理

        protected virtual void Cleanup()
        {
            _webcam?.Stop();
            if (_webcam != null) DestroyImmediate(_webcam);
            CleanupOpenCVPartial();
        }

        #endregion
    }
}
