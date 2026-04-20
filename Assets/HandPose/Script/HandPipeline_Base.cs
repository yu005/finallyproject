using MediaPipe.BlazePalm;
using MediaPipe.HandLandmark;
using UnityEngine;
using UnityEngine.Rendering;

namespace MediaPipe.HandPose {

//
// Basic implementation of the hand pipeline class
//

sealed partial class HandPipeline : System.IDisposable
{
    #region Private objects

    const int CropSize = HandLandmarkDetector.ImageSize;
    int InputWidth => _detector.palm.ImageSize;

    ResourceSet _resources;
    (PalmDetector palm, HandLandmarkDetector landmark) _detector;
    (ComputeBuffer region, ComputeBuffer filter) _buffer;
    GlobalKeyword _keywordNchw;
    int _kernelSpad;
    int _kernelBbox;
    int _kernelCrop;
    int _kernelPost;
    int _kernelClear;
    int[] _slotDetection = new int[MaxHandCount];
    bool[] _trackedSlots = new bool[MaxHandCount];
    int _trackedCount;

    #endregion

    #region Object allocation/deallocation

    void AllocateObjects(ResourceSet resources)
    {
        _resources = resources;

        _detector = (new PalmDetector(_resources.blazePalm),
                     new HandLandmarkDetector(_resources.handLandmark));

        var regionStructSize = sizeof(float) * 24;
        var filterBufferLength = HandLandmarkDetector.VertexCount * MaxHandCount * 2;

        _buffer = (new ComputeBuffer(MaxHandCount, regionStructSize),
                   new ComputeBuffer(filterBufferLength, sizeof(float) * 4));

        _kernelSpad = _resources.compute.FindKernel("spad_kernel");
        _kernelBbox = _resources.compute.FindKernel("bbox_kernel");
        _kernelCrop = _resources.compute.FindKernel("crop_kernel");
        _kernelPost = _resources.compute.FindKernel("post_kernel");
        _kernelClear = _resources.compute.FindKernel("clear_kernel");

        _keywordNchw = GlobalKeyword.Create("NCHW_INPUT");
        Shader.SetKeyword(_keywordNchw, _detector.palm.InputIsNCHW);
    }

    void DeallocateObjects()
    {
        _detector.palm.Dispose();
        _detector.landmark.Dispose();
        _buffer.region.Dispose();
        _buffer.filter.Dispose();
    }

    #endregion
}

} // namespace MediaPipe.HandPose
