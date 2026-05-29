using UnityEngine;
using UnityEngine.UI;

namespace FaceRecognition
{
    /// <summary>
    /// 人脸识别 UI 交互控制器 —— 提供类似 OpenCV 的操作体验
    /// </summary>
    public class FaceRecognitionUI : MonoBehaviour
    {
        [Header("核心")]
        public FaceRecognitionManager manager;

        [Header("UI 元素")]
        public RawImage cameraPreview;
        public Text statusText;
        public Text faceCountText;
        public InputField nameInput;
        public Button registerBtn;
        public Button switchCamBtn;
        public Button modeBtn;
        public Button deleteBtn;

        public string ModeLabel
        {
            get
            {
                return manager != null ? manager.Mode switch
                {
                    FaceSystemMode.DetectionOnly => "仅检测",
                    FaceSystemMode.Recognition => "识别",
                    FaceSystemMode.Registration => "注册",
                    _ => "识别"
                } : "识别";
            }
        }

        private void Start()
        {
            if (manager == null)
                manager = FindAnyObjectByType<FaceRecognitionManager>();

            if (registerBtn) registerBtn.onClick.AddListener(OnRegister);
            if (switchCamBtn) switchCamBtn.onClick.AddListener(() => manager?.SwitchCamera());
            if (modeBtn) modeBtn.onClick.AddListener(OnToggleMode);
            if (deleteBtn) deleteBtn.onClick.AddListener(OnDeleteAll);
        }

        private void Update()
        {
            if (manager == null || !manager.IsWebcamActive) return;

            int count = manager.DetectedFaceCount;

            if (statusText != null)
            {
                if (!string.IsNullOrEmpty(manager.InitError))
                {
                    statusText.text = $"<color=red>初始化错误: {manager.InitError}</color>";
                    return;
                }
                if (!string.IsNullOrEmpty(manager.DetectError))
                {
                    statusText.text = $"<color=red>检测异常: {manager.DetectError}</color>";
                    return;
                }

                var dets = manager.GetDetections();
                statusText.text = $"模式: {ModeLabel} | 检测到 {count} 张人脸 | 已处理 {manager.DetectFrameCount} 帧";

                foreach (var d in dets)
                {
                    if (d.IsRecognized)
                        statusText.text += $"\n✓ {d.Label} ({d.Confidence:P0})";
                }
            }

            if (faceCountText != null)
                faceCountText.text = count > 0 ? $"{count}" : "";
        }

        private void OnRegister()
        {
            if (manager == null) return;
            string name = nameInput != null ? nameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("请输入姓名");
                return;
            }

            manager.RegisterFace(name);
            if (nameInput) nameInput.text = "";
        }

        private void OnToggleMode()
        {
            if (manager == null) return;
            manager.Mode = manager.Mode == FaceSystemMode.Recognition
                ? FaceSystemMode.DetectionOnly
                : FaceSystemMode.Recognition;

            if (modeBtn)
            {
                var label = modeBtn.GetComponentInChildren<Text>();
                if (label) label.text = $"模式: {ModeLabel}";
            }
        }

        private void OnDeleteAll()
        {
            if (manager == null) return;
            var db = manager.GetDatabase();
            var names = db.Records.ConvertAll(r => r.Name);
            foreach (var n in names)
                manager.DeleteFace(n);
            Debug.Log("已清除所有人脸数据");
        }

        public void Screenshot()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath,
                $"screenshot_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"截图已保存: {path}");
        }
    }
}
