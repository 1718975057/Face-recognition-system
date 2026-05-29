using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

namespace FaceRecognition.Editor
{
    public class FaceRecognitionSetup : EditorWindow
    {
        [MenuItem("Tools/Face Recognition/Setup Scene")]
        public static void SetupScene()
        {
            // 1. Canvas
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var cgo = new GameObject("Canvas");
                canvas = cgo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = cgo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.matchWidthOrHeight = 0.5f;
                cgo.AddComponent<GraphicRaycaster>();
            }

            // 2. EventSystem
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Transform root = canvas.transform;

            // 3. 摄像头预览层（RawImage + 叠加层）
            GameObject previewPanel = CreatePanel("PreviewPanel", root);
            var previewRT = previewPanel.GetComponent<RectTransform>();
            previewRT.anchorMin = Vector2.zero;
            previewRT.anchorMax = Vector2.one;
            previewRT.offsetMin = new Vector2(20, 110);
            previewRT.offsetMax = new Vector2(-20, -20);

            // RawImage（显示摄像头画面）
            GameObject rawImageGO = new GameObject("CameraFeed");
            rawImageGO.transform.SetParent(previewPanel.transform, false);
            rawImageGO.transform.localRotation = Quaternion.identity;
            rawImageGO.transform.localScale = Vector3.one;
            var rawRT = rawImageGO.AddComponent<RectTransform>();
            rawRT.anchorMin = Vector2.zero;
            rawRT.anchorMax = Vector2.one;
            rawRT.offsetMin = Vector2.zero;
            rawRT.offsetMax = Vector2.zero;
            RawImage rawImage = rawImageGO.AddComponent<RawImage>();
            rawImage.raycastTarget = false;

            // 叠加层（检测框）
            GameObject overlayGO = new GameObject("Overlay");
            overlayGO.transform.SetParent(previewPanel.transform, false);
            var overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            // 4. 状态面板（顶部）
            GameObject statusPanel = CreatePanel("StatusPanel", root);
            var statusRT = statusPanel.GetComponent<RectTransform>();
            statusRT.anchorMin = new Vector2(0, 1);
            statusRT.anchorMax = new Vector2(1, 1);
            statusRT.pivot = new Vector2(0.5f, 1);
            statusRT.offsetMin = new Vector2(20, -100);
            statusRT.offsetMax = new Vector2(-20, 0);
            statusPanel.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            Text statusText = CreateText("StatusText", statusPanel.transform, "人脸识别系统就绪", font, 16, TextAnchor.MiddleLeft);
            statusText.GetComponent<RectTransform>().offsetMin = new Vector2(15, 5);
            statusText.GetComponent<RectTransform>().offsetMax = new Vector2(-15, -5);
            statusText.raycastTarget = false;

            // 5. 控制栏（底部）
            GameObject controlBar = CreatePanel("ControlBar", root);
            var controlRT = controlBar.GetComponent<RectTransform>();
            controlRT.anchorMin = new Vector2(0, 0);
            controlRT.anchorMax = new Vector2(1, 0);
            controlRT.offsetMin = new Vector2(20, 15);
            controlRT.offsetMax = new Vector2(-20, 95);
            controlBar.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            var hlg = controlBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.padding = new RectOffset(15, 15, 10, 10);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            // 按钮和输入框
            var modeBtn = CreateStyledButton("ModeBtn", controlBar.transform, "模式: 识别", font);
            var nameInput = CreateInputField("NameInput", controlBar.transform, "输入姓名...", font);
            var registerBtn = CreateStyledButton("RegisterBtn", controlBar.transform, "注册人脸", font);
            var switchBtn = CreateStyledButton("SwitchCamBtn", controlBar.transform, "切换摄像头", font);
            var screenshotBtn = CreateStyledButton("ScreenshotBtn", controlBar.transform, "截图", font);

            // 6. 创建 Manager + UI
            GameObject managerGO = new GameObject("FaceRecognitionSystem");
            var manager = managerGO.AddComponent<FaceRecognitionManager>();
            var ui = managerGO.AddComponent<FaceRecognitionUI>();

            manager.displayImage = rawImage;
            manager.overlayContainer = overlayRT;

            ui.manager = manager;
            ui.cameraPreview = rawImage;
            ui.statusText = statusText;
            ui.nameInput = nameInput.GetComponent<InputField>();
            ui.registerBtn = registerBtn.GetComponent<Button>();
            ui.switchCamBtn = switchBtn.GetComponent<Button>();
            ui.modeBtn = modeBtn.GetComponent<Button>();
            screenshotBtn.GetComponent<Button>().onClick.AddListener(() => ui.Screenshot());

            Selection.activeGameObject = managerGO;
            EditorGUIUtility.PingObject(managerGO);

            Debug.Log(
                "========================================\n" +
                "  人脸识别场景已搭建完成！\n" +
                "========================================\n" +
                "下一步操作：\n" +
                "1. 通过 NuGet 安装 OpenCvSharp4 到项目\n" +
                "2. 下载 haarcascade_frontalface_default.xml\n" +
                "   放到 Assets/StreamingAssets/ 目录\n" +
                "3. 点击 Play 运行\n" +
                "========================================"
            );
        }

        // ---- 工具方法 ----

        static GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0f);
            img.raycastTarget = false;
            return go;
        }

        static Text CreateText(string name, Transform parent, string content, Font font, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = align;
            return text;
        }

        static GameObject CreateStyledButton(string name, Transform parent, string label, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.5f, 0.9f);
            go.AddComponent<Button>();
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 120;
            le.minHeight = 40;
            le.flexibleWidth = 0;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = font;
            t.fontSize = 14;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var trt = labelGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return go;
        }

        static GameObject CreateInputField(string name, Transform parent, string placeholder, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var input = go.AddComponent<InputField>();
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 140;
            le.minHeight = 40;
            le.flexibleWidth = 0;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var txt = textGO.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 14;
            txt.color = Color.black;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 2);
            trt.offsetMax = new Vector2(-10, -2);

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phTxt = phGO.AddComponent<Text>();
            phTxt.text = placeholder;
            phTxt.font = font;
            phTxt.fontSize = 14;
            phTxt.fontStyle = FontStyle.Italic;
            phTxt.color = Color.gray;
            phTxt.alignment = TextAnchor.MiddleLeft;
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(10, 2);
            phRT.offsetMax = new Vector2(-10, -2);

            input.textComponent = txt;
            input.placeholder = phTxt;

            return go;
        }

        [MenuItem("Tools/Face Recognition/Download Cascade File")]
        public static void DownloadCascade()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StreamingAssets"))
                AssetDatabase.CreateFolder("Assets", "StreamingAssets");

            string url = "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml";
            Application.OpenURL(url);
            Debug.Log($"正在浏览器中打开下载链接...\n下载后请将文件保存到: Assets/StreamingAssets/");
        }
    }
}
