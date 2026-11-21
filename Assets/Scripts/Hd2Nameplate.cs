using UnityEngine;

/// <summary>
/// Floating name above a fighter's head, facing this headset's camera. Shown only when the
/// fighter is alive, within 30 m, and not hidden behind a wall. Added to bots and to other
/// players' bodies; never to your own.
/// </summary>
public class Hd2Nameplate : MonoBehaviour
{
    public const float ShowDistance = 30f;
    const float AboveHead = 0.38f;
    const float SightCheckSeconds = 0.15f;

    Transform head;
    Hd2Health health;
    System.Func<string> nameOf;
    System.Func<Hd2Team> teamOf;
    TextMesh label;
    MeshRenderer labelRenderer;
    string shownName;
    Hd2Team shownTeam = (Hd2Team)(-1);
    float nextSightCheck;
    bool inSight = true;

    static Camera viewer;
    static float nextViewerSearch;
    static readonly RaycastHit[] hits = new RaycastHit[12];

    public static Hd2Nameplate Attach(Transform owner, Transform head, Hd2Health health,
        System.Func<string> nameOf, System.Func<Hd2Team> teamOf)
    {
        if (owner == null)
            return null;
        var plate = owner.GetComponent<Hd2Nameplate>();
        if (plate == null)
            plate = owner.gameObject.AddComponent<Hd2Nameplate>();
        plate.head = head;
        plate.health = health;
        plate.nameOf = nameOf;
        plate.teamOf = teamOf;
        plate.Build();
        return plate;
    }

    void Build()
    {
        if (label != null)
            return;

        var go = new GameObject("Nameplate");
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        label = go.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 96;
        label.characterSize = 0.012f;
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.fontStyle = FontStyle.Bold;
        labelRenderer = go.GetComponent<MeshRenderer>();
        labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        labelRenderer.receiveShadows = false;
        if (label.font != null)
            labelRenderer.sharedMaterial = Hd2AmmoReadout.WorldTextMaterial(label.font);
        labelRenderer.enabled = false;
    }

    void LateUpdate()
    {
        if (label == null)
            return;

        Camera cam = Viewer();
        Vector3 top = (head != null ? head.position : transform.position + Vector3.up * 1.7f) + Vector3.up * AboveHead;
        bool alive = health == null || !health.IsDead;
        float distance = cam != null ? Vector3.Distance(cam.transform.position, top) : float.MaxValue;
        bool show = cam != null && alive && distance < ShowDistance;
        if (show && Time.time >= nextSightCheck)
        {
            nextSightCheck = Time.time + SightCheckSeconds;
            inSight = ClearView(cam.transform.position, top);
        }

        show = show && inSight;
        if (labelRenderer.enabled != show)
            labelRenderer.enabled = show;
        if (!show)
            return;

        RefreshText();
        Transform plate = label.transform;
        plate.position = top;
        Vector3 away = top - cam.transform.position;
        if (away.sqrMagnitude > 0.0001f)
            plate.rotation = Quaternion.LookRotation(away, Vector3.up);
        // Grow a little with distance so far names stay readable.
        float scale = Mathf.Clamp(distance / 10f, 1f, 2.5f);
        Vector3 parentScale = transform.lossyScale;
        float unit = Mathf.Abs(parentScale.x) > 0.0001f ? Mathf.Abs(parentScale.x) : 1f;
        plate.localScale = Vector3.one * (scale / unit);
    }

    void RefreshText()
    {
        string name = nameOf != null ? nameOf() : "";
        Hd2Team team = teamOf != null ? teamOf() : Hd2Team.None;
        if (name != shownName)
        {
            shownName = name;
            label.text = name;
        }

        if (team != shownTeam)
        {
            shownTeam = team;
            Color tint = team == Hd2Team.None ? Color.white : Hd2Locomotion.ArmorOf(team);
            label.color = Color.Lerp(tint, Color.white, 0.45f);
        }
    }

    bool ClearView(Vector3 from, Vector3 to)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        if (length < 0.01f)
            return true;

        int count = Physics.RaycastNonAlloc(from, along / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        Transform viewerRoot = viewer != null ? viewer.transform.root : null;
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].transform;
            if (hit == null || hit.IsChildOf(transform))
                continue;
            if (viewerRoot != null && hit.IsChildOf(viewerRoot))
                continue;
            // Another fighter in the way does not hide the name, only the world does.
            if (hit.GetComponentInParent<Hd2Health>() != null)
                continue;
            return false;
        }

        return true;
    }

    static Camera Viewer()
    {
        if (viewer != null && viewer.isActiveAndEnabled)
            return viewer;

        viewer = null;
        if (Time.unscaledTime < nextViewerSearch)
            return null;
        nextViewerSearch = Time.unscaledTime + 1f;
        var player = FindFirstObjectByType<Hd2Locomotion>();
        if (player != null)
            viewer = player.GetComponentInChildren<Camera>();
        if (viewer == null)
            viewer = Camera.main;
        return viewer;
    }
}
