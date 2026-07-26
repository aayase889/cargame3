using System.Collections;
using UnityEngine;

/// <summary>
/// Lightweight windshield-eye rig shared by every colored puzzle car. The
/// windshield itself is the eye white; only the pupils animate above the glass.
/// Everything lives in car-local space so it follows every car orientation.
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
    private Vector2 windshieldSize;
    private Vector2 gazeLimits;
    private float nextGazeTime;
    private bool initialized;

    public void Initialize(
        Vector3 localWindshieldPosition,
        Vector2 localWindshieldSize,
        float localWindshieldTiltDegrees,
        Vector2 windshieldSurfaceScale,
        bool useModelWindshieldFrame)
    {
        if (initialized) return;
        initialized = true;
        windshieldSize = new Vector2(
            Mathf.Max(0.2f, localWindshieldSize.x),
            Mathf.Max(0.12f, localWindshieldSize.y));

        GameObject rootObject = new GameObject("Integrated Windshield Eye Rig");
        eyeRoot = rootObject.transform;
        eyeRoot.SetParent(transform, false);
        eyeRoot.localPosition = localWindshieldPosition;
        eyeRoot.localRotation = Quaternion.Euler(localWindshieldTiltDegrees, 0f, 0f);
        eyeRoot.localScale = Vector3.one;

        if (!useModelWindshieldFrame)
        {
            CreateWindshieldPanel(
                "Windshield Eye Frame",
                Vector3.zero,
                windshieldSize,
                GetEyeOutlineMaterial(),
                eyeRoot);
        }
        Vector2 eyeSurfaceSize = useModelWindshieldFrame
            ? Vector2.Scale(windshieldSize, windshieldSurfaceScale)
            : windshieldSize * 0.89f;
        CreateWindshieldPanel(
            "Windshield Eye Surface",
            new Vector3(0f, useModelWindshieldFrame ? 0.010f : 0.018f, 0f),
            eyeSurfaceSize,
            GetEyeWhiteMaterial(),
            eyeRoot);

        float pupilOffset = windshieldSize.x * 0.19f;
        Vector3 pupilScale = new Vector3(
            windshieldSize.x * 0.20f,
            0.034f,
            windshieldSize.y * 0.49f);
        leftPupilBasePosition = new Vector3(-pupilOffset, 0.033f, 0f);
        rightPupilBasePosition = new Vector3(pupilOffset, 0.033f, 0f);
        leftPupil = CreateEyeSphere("Left Pupil", leftPupilBasePosition, pupilScale, GetPupilMaterial(), eyeRoot);
        rightPupil = CreateEyeSphere("Right Pupil", rightPupilBasePosition, pupilScale, GetPupilMaterial(), eyeRoot);
        CreateEyeSphere("Left Pupil Highlight", new Vector3(-0.20f, 0.50f, 0.15f), new Vector3(0.28f, 0.30f, 0.28f), GetHighlightMaterial(), leftPupil);
        CreateEyeSphere("Right Pupil Highlight", new Vector3(-0.20f, 0.50f, 0.15f), new Vector3(0.28f, 0.30f, 0.28f), GetHighlightMaterial(), rightPupil);

        // The windshield remains fixed during a blink. Squashing only the
        // pupils makes the character blink without turning the glass back into
        // two separate floating eyeballs.
        blinkParts = new[] { leftPupil, rightPupil };
        blinkBaseScales = new Vector3[blinkParts.Length];
        for (int index = 0; index < blinkParts.Length; index++)
            blinkBaseScales[index] = blinkParts[index].localScale;

        gazeLimits = new Vector2(
            Mathf.Max(0f, eyeSurfaceSize.x * 0.49f - pupilOffset - pupilScale.x * 0.5f - 0.006f),
            Mathf.Max(0f, eyeSurfaceSize.y * 0.49f - pupilScale.z * 0.5f - 0.006f));
        ChooseNewGaze();
        gazeOffset = gazeTarget;
        ApplyPupilPositions();
        StartCoroutine(BlinkLoop());
    }

    private void Update()
    {
        if (!initialized || leftPupil == null || rightPupil == null) return;

        if (Time.time >= nextGazeTime)
            ChooseNewGaze();

        gazeOffset = Vector2.SmoothDamp(gazeOffset, gazeTarget, ref gazeVelocity, 0.18f, 0.5f, Time.deltaTime);
        ApplyPupilPositions();
    }

    private void ApplyPupilPositions()
    {
        Vector3 offset = new Vector3(gazeOffset.x, 0f, gazeOffset.y);
        leftPupil.localPosition = leftPupilBasePosition + offset;
        rightPupil.localPosition = rightPupilBasePosition + offset;
    }

    private void ChooseNewGaze()
    {
        gazeTarget = new Vector2(
            Random.Range(-gazeLimits.x, gazeLimits.x),
            Random.Range(-gazeLimits.y, gazeLimits.y));
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

    private static Transform CreateWindshieldPanel(
        string objectName,
        Vector3 localPosition,
        Vector2 localSize,
        Material material,
        Transform parent)
    {
        GameObject panel = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = localPosition;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(localSize.x, 1f, localSize.y);

        // A softly rounded, slightly tapered windshield silhouette. The mesh
        // lies directly on the car's XZ surface instead of sitting upright or
        // hovering above it like the previous eyeball spheres.
        Vector3[] vertices =
        {
            new Vector3(-0.32f, 0f,  0.50f),
            new Vector3( 0.32f, 0f,  0.50f),
            new Vector3( 0.40f, 0f,  0.46f),
            new Vector3( 0.43f, 0f,  0.36f),
            new Vector3( 0.49f, 0f, -0.34f),
            new Vector3( 0.43f, 0f, -0.46f),
            new Vector3( 0.33f, 0f, -0.50f),
            new Vector3(-0.33f, 0f, -0.50f),
            new Vector3(-0.43f, 0f, -0.46f),
            new Vector3(-0.49f, 0f, -0.34f),
            new Vector3(-0.43f, 0f,  0.36f),
            new Vector3(-0.40f, 0f,  0.46f)
        };
        int[] triangles =
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 5,
            0, 5, 6,
            0, 6, 7,
            0, 7, 8,
            0, 8, 9,
            0, 9, 10,
            0, 10, 11
        };
        Vector2[] uv = new Vector2[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
            uv[index] = new Vector2(vertices[index].x + 0.5f, vertices[index].z + 0.5f);

        Mesh mesh = new Mesh { name = $"{objectName} Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        panel.GetComponent<MeshFilter>().sharedMesh = mesh;
        panel.GetComponent<MeshRenderer>().sharedMaterial = material;
        return panel.transform;
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
