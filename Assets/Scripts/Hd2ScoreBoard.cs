using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two big numbers on a score plane. Blue is on the left as you look at the
/// board, red on the right. The numbers face the center of the arena.
/// </summary>
public class Hd2ScoreBoard : MonoBehaviour
{
    const float Fill = 0.9f;
    static string[] numbers;
    static Vector3 arenaCenter;
    static bool hasCenter;

    TextMesh blue;
    TextMesh red;
    int shownBlue = -1;
    int shownRed = -1;
    Vector3 blueScale = Vector3.one;
    Vector3 redScale = Vector3.one;
    float maxWide = 4.6f;

    void Awake()
    {
        Build();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        int blueScore = BlueScore();
        int redScore = RedScore();
        if (blueScore == shownBlue && redScore == shownRed)
            return;

        shownBlue = blueScore;
        shownRed = redScore;
        Show(blue, blueScale, blueScore);
        Show(red, redScale, redScore);
    }

    public void Build()
    {
        CacheCenter();
        Vector3 face = transform.up;
        Vector3 toCenter = arenaCenter - transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.01f && Vector3.Dot(new Vector3(face.x, 0f, face.z), toCenter) < 0f)
        {
            transform.Rotate(180f, 0f, 0f, Space.Self);
            face = transform.up;
        }

        Vector3 boardUp = Vector3.ProjectOnPlane(Vector3.up, face);
        if (boardUp.sqrMagnitude < 0.0001f)
            boardUp = Vector3.up;
        boardUp.Normalize();
        // Viewed from the arena center, +boardRight is the viewer's left.
        Vector3 boardRight = Vector3.Cross(boardUp, face).normalized;
        BoardSpan(boardUp, boardRight, out float height, out float width);

        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        float gap = width * 0.25f;
        blue = Place("BlueScore", Hd2Locomotion.BlueArmor, face, boardUp, boardRight * gap);
        red = Place("RedScore", Hd2Locomotion.RedArmor, face, boardUp, -boardRight * gap);
        maxWide = width * 0.46f;
        Fit(blue, height * Fill, maxWide);
        Fit(red, height * Fill, maxWide);
        if (blue != null)
            blueScale = blue.transform.localScale;
        if (red != null)
            redScale = red.transform.localScale;
        Show(blue, blueScale, 0);
        Show(red, redScale, 0);
        shownBlue = 0;
        shownRed = 0;
    }

    TextMesh Place(string label, Color color, Vector3 face, Vector3 boardUp, Vector3 offset)
    {
        Transform existing = transform.Find(label);
        GameObject go = existing != null ? existing.gameObject : new GameObject(label);
        if (existing == null)
            go.transform.SetParent(transform, false);

        // Text reads from the side opposite its forward axis, same as the practice labels.
        Vector3 worldPos = transform.position + offset + face * 0.08f;
        Quaternion worldRot = Quaternion.LookRotation(-face, boardUp);
        go.transform.SetParent(null, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        go.transform.localScale = Vector3.one;
        go.transform.SetParent(transform, true);
        if (Vector3.Dot(Vector3.Cross(go.transform.right, go.transform.up), go.transform.forward) < 0f)
        {
            Vector3 scale = go.transform.localScale;
            scale.x = -scale.x;
            go.transform.localScale = scale;
        }

        TextMesh text = go.GetComponent<TextMesh>();
        if (text == null)
            text = go.AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 128;
        text.characterSize = 0.1f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = color;
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Depth-tested text material: the built-in font material draws on top of
            // everything, so the scores showed through walls and the base roofs.
            if (text.font != null)
                renderer.sharedMaterial = Hd2AmmoReadout.WorldTextMaterial(text.font);
        }

        return text;
    }

    // Sets the number at its normal size, then shrinks it if it would run past its half
    // of the board (three-digit scores), so it never hangs off the edge or into the other.
    void Show(TextMesh text, Vector3 baseScale, int value)
    {
        if (text == null)
            return;

        text.transform.localScale = baseScale;
        text.text = Number(value);
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        float wide = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z);
        if (wide > maxWide && wide > 0.001f)
            text.transform.localScale = baseScale * (maxWide / wide);
    }

    static int BlueScore()
    {
        if (Hd2Shootout.Present)
            return Hd2Shootout.BlueKills;
        if (Hd2ControlPoint.Present)
            return Hd2ControlPoint.BluePoints;
        return Hd2Domination.BluePoints;
    }

    static int RedScore()
    {
        if (Hd2Shootout.Present)
            return Hd2Shootout.RedKills;
        if (Hd2ControlPoint.Present)
            return Hd2ControlPoint.RedPoints;
        return Hd2Domination.RedPoints;
    }

    void BoardSpan(Vector3 boardUp, Vector3 boardRight, out float height, out float width)
    {
        Vector3 alongX = transform.TransformVector(Vector3.right * 10f);
        Vector3 alongZ = transform.TransformVector(Vector3.forward * 10f);
        height = Mathf.Abs(Vector3.Dot(alongX, boardUp)) + Mathf.Abs(Vector3.Dot(alongZ, boardUp));
        width = Mathf.Abs(Vector3.Dot(alongX, boardRight)) + Mathf.Abs(Vector3.Dot(alongZ, boardRight));
        if (height < 0.2f)
            height = 4f;
        if (width < 0.2f)
            width = 10f;
    }

    static void Fit(TextMesh text, float targetHeight, float maxWidth)
    {
        if (text == null)
            return;

        text.text = "8";
        if (text.font != null)
            text.font.RequestCharactersInTexture("0123456789", text.fontSize, FontStyle.Normal);
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        if (renderer == null)
            return;

        float glyph = renderer.bounds.size.y;
        if (glyph < 0.001f)
        {
            text.characterSize = targetHeight * 10f / Mathf.Max(1, text.fontSize);
            text.text = "0";
            return;
        }

        float factor = targetHeight / glyph;
        text.transform.localScale *= factor;
        text.text = "88";
        float wide = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z);
        if (wide > maxWidth && wide > 0.001f)
            text.transform.localScale *= maxWidth / wide;
        text.text = "0";
    }

    static void CacheCenter()
    {
        if (hasCenter)
            return;

        Hd2SpawnPoint[] points = FindObjectsByType<Hd2SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
                continue;
            sum += points[i].transform.position;
            count++;
        }

        arenaCenter = count > 0 ? sum / count : Vector3.zero;
        hasCenter = count > 0;
    }

    static string Number(int value)
    {
        if (numbers == null)
        {
            numbers = new string[251];
            for (int i = 0; i < numbers.Length; i++)
                numbers[i] = i.ToString();
        }

        if (value < 0)
            value = 0;
        if (value >= numbers.Length)
            value = numbers.Length - 1;
        return numbers[value];
    }
}
