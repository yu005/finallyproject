using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlantGrowPlayback : MonoBehaviour
{
    [SerializeField] Animator _animator = null;
    [SerializeField] bool _playOnStart = false;

    void Reset()
    {
        _animator = GetComponent<Animator>();
    }

    void Awake()
    {
        if (_animator == null)
            _animator = GetComponent<Animator>();

        if (_playOnStart)
            PlayOnce();
        else
            ResetToFirstFrame();
    }

    public void BindAnimator(Animator animator)
    {
        _animator = animator;
    }

    public void PlayOnce()
    {
        if (_animator == null) return;

        _animator.enabled = true;
        _animator.speed = 1.0f;
        _animator.Play(0, 0, 0);
        _animator.Update(0);
    }

    public void ResetToFirstFrame()
    {
        if (_animator == null) return;

        _animator.enabled = true;
        _animator.speed = 0.0f;
        _animator.Play(0, 0, 0);
        _animator.Update(0);
    }
}
