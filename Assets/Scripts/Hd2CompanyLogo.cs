using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Polygon Alley logo, shown once when the game starts (on the Title scene). It hangs in
/// front of the player on a backdrop the colour of the logo's background, which hides the
/// world, and drifts after the player's gaze, settling about 2 seconds after they look
/// somewhere new. Fades in, holds, then logo and backdrop fade out to the title screen. The
/// title menu ignores input while it shows.
/// Logo: Resources/Splash/PolygonAlleyLogo.png, material Resources/Splash/SplashLogo.
/// </summary>
public class Hd2CompanyLogo : MonoBehaviour
{
    const float FadeIn = 0.6f;
    const float Hold = 2.4f;
    const float FadeOut = 0.8f;
    const float Distance = 2.2f;
    const float Size = 1.25f;
    // Catch-up rate toward where the player looks: ~86% after 1 s, ~98% after 2 s.
    const float FollowRate = 2f;
    static readonly Color Backdrop = new Color(20f / 255f, 18f / 255f, 28f / 255f, 1f);

    static bool shown;

    /// <summary>True while the startup logo is on screen.</summary>
    public static bool Showing { get; private set; }

    Transform head;
    Transform logo;
    Transform backdrop;
    Material logoMaterial;
    Material backdropMaterial;
    Vector3 facing;
    float startedAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        shown = false;
        Showing = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (shown)
            return;
        shown = true;
        // Only when the game starts at the title (not when testing a map in the editor).
        if (SceneManager.GetActiveScene().name != "Title")
            return;
        Material source = Resources.Load<Material>("Splash/SplashLogo");
        if (source == null)
            return;

        var root = new GameObject("CompanyLogo");
        root.AddComponent<Hd2CompanyLogo>().Build(source);
    }

    void Build(Material source)
    {
        Showing = true;

        backdropMaterial = new Material(source) { name = "SplashBackdrop", mainTexture = Texture2D.whiteTexture };
        backdropMaterial.SetFloat("_Cull", 0f);
        backdropMaterial.color = Backdrop;
        backdropMaterial.renderQueue = 4000;
        backdrop = Shape(PrimitiveType.Sphere, "Backdrop", backdropMaterial, Vector3.one * 8f);

        logoMaterial = new Material(source) { name = "SplashLogo (instance)" };
        logoMaterial.color = new Color(1f, 1f, 1f, 0f);
        logoMaterial.renderQueue = 4001;
        logo = Shape(PrimitiveType.Quad, "Logo", logoMaterial, Vector3.one * Size);
    }

    Transform Shape(PrimitiveType type, string label, Material material, Vector3 scale)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = label;
        Collider collider = shape.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        Renderer renderer = shape.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        shape.transform.SetParent(transform, false);
        shape.transform.localScale = scale;
        return shape.transform;
    }

    void LateUpdate()
    {
        if (head == null)
        {
            Camera view = Camera.main;
            if (view == null)
                return;
            head = view.transform;
        }

        Vector3 look = head.forward;
        if (startedAt < 0f)
        {
            startedAt = Time.unscaledTime;
            facing = look;
        }

        float t = Time.unscaledTime - startedAt;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        facing = Vector3.Slerp(facing, look, 1f - Mathf.Exp(-FollowRate * dt)).normalized;

        backdrop.position = head.position;
        logo.position = head.position + facing * Distance;
        Vector3 up = Mathf.Abs(Vector3.Dot(facing, Vector3.up)) > 0.98f ? head.up : Vector3.up;
        logo.rotation = Quaternion.LookRotation(facing, up);

        float outStart = FadeIn + Hold;
        float logoAlpha = Mathf.Clamp01(t / FadeIn) * (1f - Mathf.Clamp01((t - outStart) / FadeOut));
        float backAlpha = 1f - Mathf.Clamp01((t - outStart) / FadeOut);
        logoMaterial.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, logoAlpha));
        Color back = Backdrop;
        back.a = Mathf.SmoothStep(0f, 1f, backAlpha);
        backdropMaterial.color = back;

        if (t >= outStart + FadeOut)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        Showing = false;
        if (logoMaterial != null)
            Destroy(logoMaterial);
        if (backdropMaterial != null)
            Destroy(backdropMaterial);
    }
}
