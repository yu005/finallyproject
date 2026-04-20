using UnityEngine;

namespace MediaPipe.HandPose {

//
// Public part of the hand pipeline class
//

partial class HandPipeline
{
    #region Detection data accessors

    public const int MaxHandCount = 2;
    public const int KeyPointCount = 21;

    public enum KeyPoint
    {
        Wrist,
        Thumb1,  Thumb2,  Thumb3,  Thumb4,
        Index1,  Index2,  Index3,  Index4,
        Middle1, Middle2, Middle3, Middle4,
        Ring1,   Ring2,   Ring3,   Ring4,
        Pinky1,  Pinky2,  Pinky3,  Pinky4
    }

    int GetReadCacheIndex(int handIndex, int pointIndex)
      => handIndex * KeyPointCount + pointIndex;

    public Vector3 GetKeyPoint(int handIndex, KeyPoint point)
      => ReadCache[GetReadCacheIndex(handIndex, (int)point)];

    public Vector3 GetKeyPoint(int handIndex, int index)
      => ReadCache[GetReadCacheIndex(handIndex, index)];

    public Vector3 GetKeyPoint(KeyPoint point)
      => GetKeyPoint(0, point);

    public Vector3 GetKeyPoint(int index)
      => GetKeyPoint(0, index);

    public int TrackedHandCount
      => _trackedCount;

    public bool IsHandTracked(int handIndex)
      => handIndex >= 0 && handIndex < MaxHandCount && _trackedSlots[handIndex];

    #endregion

    #region GPU-side resource accessors

    public ComputeBuffer KeyPointBuffer
      => _buffer.filter;

    public ComputeBuffer HandRegionBuffer
      => _buffer.region;

    public ComputeBuffer HandRegionCropBuffer
      => _detector.landmark.InputBuffer;

    #endregion

    #region Public properties and methods

    public bool UseAsyncReadback { get; set; } = true;

    public HandPipeline(ResourceSet resources)
      => AllocateObjects(resources);

    public void Dispose()
      => DeallocateObjects();

    public void ProcessImage(Texture image)
      => RunPipeline(image);

    #endregion
}

} // namespace MediaPipe.HandPose
