using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using OpenCvSharp;
using OpenCvSharp.Face;

namespace FaceRecognition
{
    public partial class FaceRecognitionManager
    {
        private CascadeClassifier _cascade;
        private FaceRecognizer _recognizer;
        private int _detectCallCount;

        #region 初始化
        partial void InitOpenCVPartial()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, "haarcascade_frontalface_default.xml");
                Debug.Log($"[FaceRec] StreamingAssets path: {path}");
                Debug.Log($"[FaceRec] File exists: {System.IO.File.Exists(path)}");
                if (System.IO.File.Exists(path))
                {
                    // 复制到无中文无空格的临时路径，避免 OpenCV 原生 fopen 编码问题
                    string safePath = System.IO.Path.Combine(Application.temporaryCachePath, "cascade.xml");
                    try
                    {
                        System.IO.File.Copy(path, safePath, true);
                        Debug.Log($"[FaceRec] 级联复制到: {safePath}");
                    }
                    catch { safePath = path; }
                    _cascade = new CascadeClassifier(safePath);
                    Debug.Log($"[FaceRec] Cascade 构造完成 | Empty={_cascade.Empty()}");
                    if (_cascade.Empty())
                    {
                        InitError = $"级联加载失败, 路径: {safePath}";
                        Debug.LogError($"[FaceRec] {InitError}");
                    }
                    else
                    {
                        Debug.Log("[FaceRec] Haar Cascade 已加载");
                    }
                }
                else
                {
                    InitError = $"找不到级联文件: {path}";
                    Debug.LogError($"[FaceRec] {InitError}");
                }
                _recognizer = LBPHFaceRecognizer.Create(1, 8, 8, 8, lbphThreshold);
                DetectionAvailable = _cascade != null && !_cascade.Empty();
            }
            catch (DllNotFoundException e)
            {
                InitError = $"缺少原生 DLL: {e.Message}";
                Debug.LogError($"[FaceRec] {InitError}");
            }
            catch (TypeInitializationException e)
            {
                InitError = $"OpenCvSharp 类型初始化失败 (可能缺少 VC++ Runtime 或 OpenCV DLL): {e.InnerException?.Message ?? e.Message}";
                Debug.LogError($"[FaceRec] {InitError}");
            }
            catch (Exception e)
            {
                InitError = $"OpenCV 初始化失败: {e.Message}\n{e.StackTrace}";
                Debug.LogError($"[FaceRec] {InitError}");
            }
        }
        #endregion

        #region 像素处理
        // Color32[] → byte[]（逐像素拷贝，build 和 editor 行为一致）
        private static byte[] Color32ToBytes(Color32[] src)
        {
            var bytes = new byte[src.Length * 4];
            for (int i = 0; i < src.Length; i++)
            {
                int j = i * 4;
                bytes[j] = src[i].r; bytes[j + 1] = src[i].g;
                bytes[j + 2] = src[i].b; bytes[j + 3] = src[i].a;
            }
            return bytes;
        }

        // byte[] → BGR Mat（GCHandle 固定内存，CvtColor 后数据已拷贝到 bgr）
        private static void BytesToBgr(byte[] bytes, int w, int h, out Mat bgr)
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var rgba = new Mat(h, w, MatType.CV_8UC4, handle.AddrOfPinnedObject());
                bgr = new Mat();
                Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
                rgba.Dispose();
            }
            finally { handle.Free(); }
        }

        private static Color32[] MatToColor32(Mat mat)
        {
            using var rgb = new Mat();
            Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGBA);
            int n = rgb.Width * rgb.Height;
            var db = new byte[n * 4];
            Marshal.Copy(rgb.Data, db, 0, db.Length);
            var dp = new Color32[n];
            for (int i = 0; i < n; i++) { int j = i * 4; dp[i] = new Color32(db[j], db[j + 1], db[j + 2], db[j + 3]); }
            return dp;
        }
        #endregion

        #region 检测处理
        partial void ProcessFrameAsyncPartial()
        {
            if (_pixels == null || _cascade == null) return;
            int w = _texW, h = _texH;
            if (w == 0 || h == 0) return;

            _detectCallCount++;
            if (_detectCallCount == 1)
                Debug.Log($"[FaceRec] 首次检测触发 | 尺寸={w}x{h} | 旋转角={_rotationAngle}° | DebugDraw={debugDraw}");

            var raw = new Color32[_pixels.Length];
            Array.Copy(_pixels, raw, raw.Length);

            int rot = _rotationAngle;
            if (flipVertical) rot = (rot + 180) % 360;
            var norm = NormalizePixels(raw, w, h, rot, out int nw, out int nh);
            _normW = nw; _normH = nh;
            var bytes = Color32ToBytes(norm);

            // 主线程同步执行检测（Mono 打包后 OpenCV 对象有线程亲和性）
            try
            {
                ProcessPixels(bytes, nw, nh);
                DetectError = null;
            }
            catch (Exception e)
            {
                DetectError = $"检测异常: {e.Message}";
                Debug.LogError($"[FaceRec] {DetectError}\n{e.StackTrace}");
            }

            _processing = false;
        }

        private void ProcessPixels(byte[] bytes, int w, int h)
        {
            BytesToBgr(bytes, w, h, out var bgr);
            using var debug = bgr.Clone();
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);
            bgr.Dispose();

            // 防止 Mono GC 过早回收 cascade 原生句柄
            GC.KeepAlive(_cascade);

            var faces = _cascade.DetectMultiScale(gray, scaleFactor, minNeighbors,
                HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(minFaceSize, minFaceSize), null);
            var filtered = ApplyNMS(faces, maxFaces);

            var list = new List<FaceDetection>();
            foreach (var face in filtered)
            {
                var det = new FaceDetection { BoundingBox = new UnityEngine.Rect(face.X, face.Y, face.Width, face.Height) };
                if (Mode == FaceSystemMode.Recognition && _recognizer != null && !_recognizer.Empty)
                {
                    using var roi = gray[face];
                    using var rs = new Mat();
                    Cv2.Resize(roi, rs, new OpenCvSharp.Size(100, 100));
                    _recognizer.Predict(rs, out int label, out double dist);
                    if (label >= 0 && _labelMap.TryGetValue(label, out string name))
                    { det.LabelId = label; det.Label = name; det.Confidence = Math.Max(0, 1.0 - dist / lbphThreshold); }
                }
                list.Add(det);
            }
            _detections.Clear();
            _detections.AddRange(list);

            foreach (var f in filtered) Cv2.Rectangle(debug, f, Scalar.LimeGreen, 2);
            _debugPixels = MatToColor32(debug);
            _debugPixelVersion++;

            DetectFrameCount++;
            if (DetectFrameCount == 1)
                Debug.Log($"[FaceRec] 首次检测完成 | 检测到 {list.Count} 张人脸 | 识别模式: {Mode}");
        }
        #endregion

        #region 人脸注册
        partial void RegisterFacePartial(string name)
        {
            if (_webcam == null || _cascade == null) { Debug.LogWarning("未就绪"); return; }
            int w = _texW, h = _texH;
            if (w == 0 || h == 0) return;

            var raw = _webcam.GetPixels32();
            int rot = _rotationAngle;
            if (flipVertical) rot = (rot + 180) % 360;
            var norm = NormalizePixels(raw, w, h, rot, out int nw, out int nh);
            var bytes = Color32ToBytes(norm);

            BytesToBgr(bytes, nw, nh, out var bgr);
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            var faces = _cascade.DetectMultiScale(gray, scaleFactor, minNeighbors,
                HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(minFaceSize, minFaceSize), null);
            var filtered = ApplyNMS(faces, 1);
            if (filtered.Length == 0) { Debug.LogWarning("未检测到人脸"); bgr.Dispose(); return; }

            var face = filtered[0];
            int fx = Math.Max(0, face.X), fy = Math.Max(0, face.Y);
            int fw = Math.Min(face.Width, nw - fx), fh = Math.Min(face.Height, nh - fy);
            using var roi = bgr[new OpenCvSharp.Rect(fx, fy, fw, fh)];

            string dir = System.IO.Path.Combine(Application.persistentDataPath, "FaceImages");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
            Cv2.ImWrite(path, roi);
            bgr.Dispose();

            var rec = _database.Records.Find(r => r.Name == name);
            if (rec == null) { rec = new FaceRecord { Name = name, LabelId = _database.Records.Count }; _database.Records.Add(rec); }
            rec.ImagePaths.Add(path);
            _labelMap[rec.LabelId] = name;

            TrainFromImagesPartial();
            SaveDatabase();
            Mode = FaceSystemMode.Recognition;
            Debug.Log($"[FaceRec] 已注册: {name}");
        }
        #endregion

        #region 删除 & 训练
        partial void DeleteFacePartial(string name)
        {
            var rec = _database.Records.Find(r => r.Name == name);
            if (rec != null) foreach (var p in rec.ImagePaths) if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            _database.Records.RemoveAll(r => r.Name == name);
            _labelMap.Clear();
            foreach (var r in _database.Records) _labelMap[r.LabelId] = r.Name;
            TrainFromImagesPartial();
            SaveDatabase();
        }

        partial void TrainFromImagesPartial()
        {
            if (_database.Records.Count == 0) return;
            var imgs = new List<Mat>(); var lbls = new List<int>();
            foreach (var rec in _database.Records)
                foreach (var p in rec.ImagePaths)
                {
                    if (!System.IO.File.Exists(p)) continue;
                    var img = Cv2.ImRead(p, ImreadModes.Grayscale);
                    if (img.Empty()) { img.Dispose(); continue; }
                    var rs = new Mat(); Cv2.Resize(img, rs, new OpenCvSharp.Size(100, 100));
                    Cv2.EqualizeHist(rs, rs); imgs.Add(rs); lbls.Add(rec.LabelId); img.Dispose();
                }
            if (imgs.Count == 0) return;
            _recognizer?.Dispose();
            _recognizer = LBPHFaceRecognizer.Create(1, 8, 8, 8, lbphThreshold);
            _recognizer.Train(imgs, lbls);
            foreach (var m in imgs) m.Dispose();
        }

        partial void CleanupOpenCVPartial() { _cascade?.Dispose(); _cascade = null; _recognizer?.Dispose(); _recognizer = null; }
        #endregion

        #region 旋转 & NMS
        private Color32[] NormalizePixels(Color32[] src, int w, int h, int rot, out int outW, out int outH)
        {
            outW = w; outH = h;
            if (rot != 90 && rot != 180 && rot != 270) return src;
            var dst = new Color32[w * h];
            if (rot == 180)
            {
                for (int y = 0; y < h; y++) Array.Copy(src, (h - 1 - y) * w, dst, y * w, w);
            }
            else
            {
                outW = h; outH = w;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int si = y * w + x;
                        int di = rot == 90 ? (w - 1 - x) * outW + y : x * outW + (h - 1 - y);
                        dst[di] = src[si];
                    }
            }
            return dst;
        }

        private static OpenCvSharp.Rect[] ApplyNMS(OpenCvSharp.Rect[] rects, int maxCount)
        {
            if (rects.Length <= 1) return rects;
            System.Array.Sort(rects, (a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));
            var kept = new List<OpenCvSharp.Rect>();
            for (int i = 0; i < rects.Length; i++)
            {
                bool dup = false;
                for (int j = 0; j < kept.Count; j++)
                    if (IsDup(rects[i], kept[j])) { dup = true; break; }
                if (!dup) { kept.Add(rects[i]); if (kept.Count >= maxCount) break; }
            }
            return kept.ToArray();
        }
        private static bool IsDup(OpenCvSharp.Rect a, OpenCvSharp.Rect b)
        {
            float cxA = a.X + a.Width * 0.5f, cyA = a.Y + a.Height * 0.5f;
            float cxB = b.X + b.Width * 0.5f, cyB = b.Y + b.Height * 0.5f;
            return Math.Abs(cxA - cxB) < Math.Max(a.Width, b.Width) * 0.6f
                && Math.Abs(cyA - cyB) < Math.Max(a.Height, b.Height) * 0.6f;
        }
        #endregion
    }
}
