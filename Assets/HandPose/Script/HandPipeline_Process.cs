using UnityEngine;

namespace MediaPipe.HandPose {

//
// Image processing part of the hand pipeline class
//

partial class HandPipeline
{
    void SelectDetections()
    {
        for (var i = 0; i < MaxHandCount; i++)
            _slotDetection[i] = -1;

        var detections = _detector.palm.Detections;
        if (detections.Length == 0) return;

        var best1 = -1;
        var best2 = -1;
        var score1 = float.NegativeInfinity;
        var score2 = float.NegativeInfinity;

        for (var i = 0; i < detections.Length; i++)
        {
            var score = detections[i].score;

            if (score > score1)
            {
                best2 = best1;
                score2 = score1;
                best1 = i;
                score1 = score;
            }
            else if (score > score2)
            {
                best2 = i;
                score2 = score;
            }
        }

        if (best1 < 0) return;

        if (best2 >= 0)
        {
            var x1 = detections[best1].center.x;
            var x2 = detections[best2].center.x;

            if (x1 <= x2)
            {
                _slotDetection[0] = best1;
                _slotDetection[1] = best2;
            }
            else
            {
                _slotDetection[0] = best2;
                _slotDetection[1] = best1;
            }
        }
        else
        {
            _slotDetection[0] = best1;
        }
    }

    void RunPipeline(Texture input)
    {
        var cs = _resources.compute;

        // Letterboxing scale factor
        var scale = new Vector2
          (Mathf.Max((float)input.height / input.width, 1),
           Mathf.Max(1, (float)input.width / input.height));

        // Image scaling and padding
        cs.SetInt("_spad_width", InputWidth);
        cs.SetVector("_spad_scale", scale);
        cs.SetTexture(_kernelSpad, "_spad_input", input);
        cs.SetBuffer(_kernelSpad, "_spad_output", _detector.palm.InputBuffer);
        cs.Dispatch(_kernelSpad, InputWidth / 8, InputWidth / 8, 1);

        // Palm detection
        _detector.palm.ProcessInput();
        SelectDetections();

        _trackedCount = 0;

        for (var hand = 0; hand < MaxHandCount; hand++)
        {
            var detectionIndex = _slotDetection[hand];
            var tracked = detectionIndex >= 0;
            _trackedSlots[hand] = tracked;

            if (!tracked)
            {
                cs.SetInt("_clear_slot", hand);
                cs.SetBuffer(_kernelClear, "_post_output", _buffer.filter);
                cs.Dispatch(_kernelClear, 1, 1, 1);
                continue;
            }

            _trackedCount++;

            // Hand region bounding box update
            cs.SetFloat("_bbox_dt", Time.deltaTime);
            cs.SetInt("_bbox_detection_index", detectionIndex);
            cs.SetInt("_bbox_region_index", hand);
            cs.SetBuffer(_kernelBbox, "_bbox_count", _detector.palm.CountBuffer);
            cs.SetBuffer(_kernelBbox, "_bbox_palm", _detector.palm.DetectionBuffer);
            cs.SetBuffer(_kernelBbox, "_bbox_region", _buffer.region);
            cs.Dispatch(_kernelBbox, 1, 1, 1);

            // Hand region cropping
            cs.SetInt("_crop_slot", hand);
            cs.SetTexture(_kernelCrop, "_crop_input", input);
            cs.SetBuffer(_kernelCrop, "_crop_region", _buffer.region);
            cs.SetBuffer(_kernelCrop, "_crop_output", _detector.landmark.InputBuffer);
            cs.Dispatch(_kernelCrop, CropSize / 8, CropSize / 8, 1);

            // Hand landmark detection
            _detector.landmark.ProcessInput();

            // Key point postprocess
            cs.SetFloat("_post_dt", Time.deltaTime);
            cs.SetFloat("_post_scale", scale.y);
            cs.SetInt("_post_slot", hand);
            cs.SetBuffer(_kernelPost, "_post_input", _detector.landmark.OutputBuffer);
            cs.SetBuffer(_kernelPost, "_post_region", _buffer.region);
            cs.SetBuffer(_kernelPost, "_post_output", _buffer.filter);
            cs.Dispatch(_kernelPost, 1, 1, 1);
        }

        // Read cache invalidation
        InvalidateReadCache();
    }
}

} // namespace MediaPipe.HandPose
