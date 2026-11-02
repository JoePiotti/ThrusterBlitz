using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two big numbers on a score plane. Blue is on the left as you look at the
/// board, red on the right. The numbers face the center of the arena.
/// </summary>
public class Hd2ScoreBoard : MonoBehaviour
{
    static readonly Color Blue = new Color(0.35f, 0.62f, 1f, 1f);
    static readonly Color Red = new Color(0.92f, 0.16f, 0.12f, 1f);
    static string[] numbers;
    static Vector3 arenaCenter;
    static bool hasCenter;

    TextMesh blue;
    TextMesh red;
    int shownBlue = -1;
    int shownRed = -1;

    void Awake()
    {
        Build();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        bool control = Hd2ControlPoint.Present;
        int blueScore = control ? Hd2ControlPoint.BluePoints : Hd2Domination.BluePoints;
        int redScore = control ? Hd2ControlPoint.RedPoints : Hd2Domination.RedPoints;
        if (blueScore == shownBlue && redScore == shownRed)
            return;

        shownBlue = blueScore;
        shownRed = redScore;
        if (blue != null)
            blue.text = Number(blueScore);
        if (red != null)
            red.text = Number(redScore);
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

        Collider[] colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        blue = Place("BlueScore", Blue, face, boardUp, boardRight * 1.22f);
        red = Place("RedScore", Red, face, boardUp, -boardRight * 1.22f);
        if (blue != null)
            blue.text = Number(0);
        if (red != null)
            red.text = Number(0);
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
        text.fontSize = 96;
        text.characterSize = 0.12f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = color;
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (text.font != null)
                renderer.sharedMaterial = text.font.material;
        }

        return text;
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
