using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Small unscaled-time press response for artwork-backed UI buttons.
/// </summary>
public sealed class SimpleButtonPressAnimation : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] private float pressedScale = 0.92f;
    [SerializeField] private float transitionDuration = 0.07f;

    private Coroutine scaleCoroutine;

    public void OnPointerDown(PointerEventData eventData)
    {
        AnimateTo(Mathf.Clamp(pressedScale, 0.75f, 1f));
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        AnimateTo(1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        AnimateTo(1f);
    }

    private void OnDisable()
    {
        if (scaleCoroutine != null)
        {
            StopCoroutine(scaleCoroutine);
            scaleCoroutine = null;
        }

        transform.localScale = Vector3.one;
    }

    private void AnimateTo(float targetScale)
    {
        if (scaleCoroutine != null)
            StopCoroutine(scaleCoroutine);
        scaleCoroutine = StartCoroutine(ScaleTo(targetScale));
    }

    private IEnumerator ScaleTo(float targetScale)
    {
        Vector3 startScale = transform.localScale;
        Vector3 endScale = Vector3.one * targetScale;
        float duration = Mathf.Max(0.01f, transitionDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            transform.localScale = Vector3.LerpUnclamped(startScale, endScale, eased);
            yield return null;
        }

        transform.localScale = endScale;
        scaleCoroutine = null;
    }
}
