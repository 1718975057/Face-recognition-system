using System;
using System.Collections.Generic;

namespace FaceRecognition
{
    [Serializable]
    public class FaceDetection
    { 
        public int LabelId;
        public UnityEngine.Rect BoundingBox;
        public string Label;
        public double Confidence;
        public bool IsRecognized => !string.IsNullOrEmpty(Label);
    }

    [Serializable]
    public class FaceRecord
    {
        public string Name;
        public int LabelId;
        public List<string> ImagePaths = new List<string>();   // 注册的人脸图片路径
    }

    [Serializable]
    public class FaceDatabase
    {
        public List<FaceRecord> Records = new List<FaceRecord>();
    }

    public enum FaceSystemMode
    {
        DetectionOnly,
        Recognition,
        Registration
    }
}
