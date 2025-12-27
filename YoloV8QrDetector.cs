using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers;

namespace QrScanService
{
    public class YoloV8QrDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;

        // ⚠️ BẮT BUỘC PHẢI LÀ 640 (Trừ khi bạn train lại model với size khác)
        private const int INPUT_SIZE = 640;

        // Bạn có thể giữ độ tin cậy thấp một chút để nó bắt nhạy hơn
        private const float CONF_THRESHOLD = 0.45f;
        private const float NMS_THRESHOLD = 0.45f;

        public YoloV8QrDetector(string onnxPath)
        {
            var options = new SessionOptions();
            try { options.AppendExecutionProvider_DML(0); }
            catch { options.AppendExecutionProvider_CPU(); }

            _session = new InferenceSession(onnxPath, options);
            _inputName = _session.InputMetadata.Keys.First();
        }

        public List<Rect> Detect(Mat frame)
        {
            if (frame.Empty()) return new List<Rect>();

            var inputTensor = Preprocess(frame);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) };
            using var results = _session.Run(inputs);

            var output = results.First().AsTensor<float>();
            return Postprocess(output, frame.Width, frame.Height);
        }

        private DenseTensor<float> Preprocess(Mat src)
        {
            using var resized = new Mat();
            Cv2.Resize(src, resized, new Size(INPUT_SIZE, INPUT_SIZE));
            Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, INPUT_SIZE, INPUT_SIZE });

            unsafe
            {
                using (MemoryHandle pin = tensor.Buffer.Pin())
                {
                    float* pTensor = (float*)pin.Pointer;
                    byte* pData = (byte*)resized.DataPointer;

                    int channelSize = INPUT_SIZE * INPUT_SIZE;
                    for (int i = 0; i < channelSize; i++)
                    {
                        pTensor[i] = pData[i * 3] / 255f;
                        pTensor[i + channelSize] = pData[i * 3 + 1] / 255f;
                        pTensor[i + 2 * channelSize] = pData[i * 3 + 2] / 255f;
                    }
                }
            }
            return tensor;
        }

        private List<Rect> Postprocess(Tensor<float> output, int imgW, int imgH)
        {
            var boxes = new List<Rect2d>();
            var scores = new List<float>();

            int dimensions = output.Dimensions[1];
            int anchors = output.Dimensions[2];

            float xFactor = (float)imgW / INPUT_SIZE;
            float yFactor = (float)imgH / INPUT_SIZE;

            for (int i = 0; i < anchors; i++)
            {
                float maxConf = 0;
                for (int j = 4; j < dimensions; j++)
                {
                    if (output[0, j, i] > maxConf) maxConf = output[0, j, i];
                }

                if (maxConf < CONF_THRESHOLD) continue;

                float cx = output[0, 0, i];
                float cy = output[0, 1, i];
                float w = output[0, 2, i];
                float h = output[0, 3, i];

                boxes.Add(new Rect2d(
                    (cx - w / 2.0) * xFactor,
                    (cy - h / 2.0) * yFactor,
                    w * xFactor,
                    h * yFactor
                ));
                scores.Add(maxConf);
            }

            CvDnn.NMSBoxes(boxes, scores, CONF_THRESHOLD, NMS_THRESHOLD, out int[] indices);

            return indices.Select(idx => {
                var b = boxes[idx];
                return new Rect(
                    (int)Math.Max(0, b.X),
                    (int)Math.Max(0, b.Y),
                    (int)Math.Min(imgW - b.X, b.Width),
                    (int)Math.Min(imgH - b.Y, b.Height)
                );
            }).ToList();
        }

        public void Dispose() => _session?.Dispose();
    }
}