using System.Collections;
using UnityEngine;

/// <summary>
/// Lightweight character-eye rig shared by every colored puzzle car. The eyes
/// live in car-local space, so glances and blinks follow every car orientation.
/// </summary>
public sealed class CarEyeController : MonoBehaviour
{
    private static Material eyeWhiteMaterial;
    private static Material eyeOutlineMaterial;
    private static Material pupilMaterial;
    private static Material highlightMaterial;

    private Transform eyeRoot;
    private Transform leftPupil;
    private Transform rightPupil;
    private Transform[] blinkParts;
    private Vector3[] blinkBaseScales;
    private Vector3 leftPupilBasePosition;
    private Vector3 rightPupilBasePosition;
    private Vector2 gazeOffset;
    private Vector2 gazeTarget;
    private Vector2 gazeVelocity;
    private float nextGazeTime;
    private bool initialized;

    public void Initialize(Vector3 localWindshieldPosition)
    {
        if (initialized) return;
        initialized = true;

        GameObject rootObject = new GameObject("Animated Eye Rig");
        eyeRoot = rootObject.transform;
        eyeRoot.SetParent(transform, false);
        eyeRoot.localPosition = localWindshieldPosition;
        eyeRoot.localRotation = Quaternion.identity;
        eyeRoot.localScale = Vector3.one;

        Transform leftOutline = CreateEyeSphere("Left Eye Outline", new Vector3(-0.20f, -0.01f, 0f), new Vector3(0.48f, 0.055f, 0.35f), GetEyeOutlineMaterial(), eyeRoot);
        Transform rightOutline = CreateEyeSphere("Right Eye Outline", new Vector3(0.20f, -0.01f, 0f), new Vector3(0.48f, 0.055f, 0.35f), GetEyeOutlineMaterial(), eyeRoot);
        Transform leftWhite = CreateEyeSphere("Left Eye White", new Vector3(-0.20f, 0.025f, 0f), new Vector3(0.43f, 0.07f, 0.31f), GetEyeWhiteMaterial(), eyeRoot);
        Transform rightWhite = CreateEyeSphere("Right Eye White", new Vector3(0.20f, 0.025f, 0f), new Vector3(0.43f, 0.07f, 0.31f), GetEyeWhiteMaterial(), eyeRoot);

        leftPupilBasePosition = new Vector3(-0.20f, 0.083f, 0.015f);
        rightPupilBasePosition = new Vector3(0.20f, 0.083f, 0.015f);
        leftPupil = CreateEyeSphere("Left Pupil", leftPupilBasePosition, new Vector3(0.16f, 0.075f, 0.15f), GetPupilMaterial(), eyeRoot);
        rightPupil = CreateEyeSphere("Right Pupil", rightPupilBasePosition, new Vector3(0.16f, 0.075f, 0.15f), GetPupilMaterial(), eyeRoot);
        CreateEyeSphere("Left Pupil Highlight", new Vector3(-0.20f, 0.50f, 0.15f), new Vector3(0.28f, 0.30f, 0.28f), GetHighlightMaterial(), leftPupil);
        CreateEyeSphere("Right Pupil Highlight", new Vector3(-0.20f, 0.50f, 0.15f), new Vector3(0.28f, 0.30f, 0.28f), GetHighlightMaterial(), rightPupil);

        blinkParts = new[] { leftOutline, rightOutline, leftWhite, rightWhite, leftPupil, rightPupil };
        blinkBaseScales = new Vector3[blinkParts.Length];
        for (int index = 0; index < blinkParts.Length; index++)
            blinkBaseScales[index] = blinkParts[index].localScale;

        ChooseNewGaze();
        StartCoroutine(BlinkLoop());
    }

    private void Update()
    {
        if (!initialized || leftPupil == null || rightPupil == null) return;

        if (Time.time >= nextGazeTime)
            ChooseNewGaze();

        gazeOffset = Vector2.SmoothDamp(gazeOffset, gazeTarget, ref gazeVelocity, 0.18f, 0.5f, Time.deltaTime);
        Vector3 offset = new Vector3(gazeOffset.x, 0f, gazeOffset.y);
        leftPupil.localPosition = leftPupilBasePosition + offset;
        rightPupil.localPosition = rightPupilBasePosition + offset;
    }

    private void ChooseNewGaze()
    {
        gazeTarget = new Vector2(Random.Range(-0.065f, 0.065f), Random.Range(-0.042f, 0.045f));
        nextGazeTime = Time.time + Random.Range(0.85f, 2.25f);
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(2.2f, 5.0f));
            yield return BlinkOnce();

            if (Random.value < 0.16f)
            {
                yield return new WaitForSeconds(0.10f);
                yield return BlinkOnce();
            }
        }
    }

    private IEnumerator BlinkOnce()
    {
        const float duration = 0.18f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float openness = 1f - Mathf.Sin(progress * Mathf.PI) * 0.94f;
            SetBlinkOpenness(openness);
            yield return null;
        }

        SetBlinkOpenness(1f);
    }

    private void SetBlinkOpenness(float openness)
    {
        if (blinkParts == null) return;
        for (int index = 0; index < blinkParts.Length; index++)
        {
            if (blinkParts[index] == null) continue;
            Vector3 scale = blinkBaseScales[index];
            scale.z *= Mathf.Max(0.06f, openness);
            blinkParts[index].localScale = scale;
        }
    }

    private static Transform CreateEyeSphere(string objectName, Vector3 localPosition, Vector3 localScale, Material material, Transform parent)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = objectName;
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = localPosition;
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale = localScale;
        sphere.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        return sphere.transform;
    }

    private static Material GetEyeWhiteMaterial()
    {
        if (eyeWhiteMaterial == null)
        {
            eyeWhiteMaterial = CarPrototype3D.CreateMaterial(new Color(0.98f, 0.97f, 0.91f));
            eyeWhiteMaterial.name = "Runtime Car Eye White";
            SetLowSmoothness(eyeWhiteMaterial);
        }
        return eyeWhiteMaterial;
    }

    private static Material GetEyeOutlineMaterial()
    {
        if (eyeOutlineMaterial == null)
        {
            eyeOutlineMaterial = CarPrototype3D.CreateMaterial(new Color(0.055f, 0.07f, 0.09f));
            eyeOutlineMaterial.name = "Runtime Car Eye Outline";
            SetLowSmoothness(eyeOutlineMaterial);
        }
        return eyeOutlineMaterial;
    }

    private static Material GetPupilMaterial()
    {
        if (pupilMaterial == null)
        {
            pupilMaterial = CarPrototype3D.CreateMaterial(new Color(0.025f, 0.03f, 0.045f));
            pupilMaterial.name = "Runtime Car Pupil";
            if (pupilMaterial.HasProperty("_Smoothness")) pupilMaterial.SetFloat("_Smoothness", 0.58f);
        }
        return pupilMaterial;
    }

    private static Material GetHighlightMaterial()
    {
        if (highlightMaterial == null)
        {
            highlightMaterial = CarPrototype3D.CreateMaterial(Color.white);
            highlightMaterial.name = "Runtime Car Pupil Highlight";
            if (highlightMaterial.HasProperty("_Smoothness")) highlightMaterial.SetFloat("_Smoothness", 0.72f);
        }
        return highlightMaterial;
    }

    private static void SetLowSmoothness(Material material)
    {
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.18f);
    }
}
