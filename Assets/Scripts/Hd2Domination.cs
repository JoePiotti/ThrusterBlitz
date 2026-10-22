using UnityEngine;

/// <summary>
/// Rome domination. A team wins when buttons A, B, and C are all that team's
/// color for five seconds. Letting one go, or a neutral button, clears the timer.
/// </summary>
public class Hd2Domination : MonoBehaviour
{
    public const float HoldSeconds = 5f;

    public static bool Decided { get; private set; }

    Hd2TeamButton pointA;
    Hd2TeamButton pointB;
    Hd2TeamButton pointC;
    Hd2Team pending = Hd2Team.None;
    float heldFor;
    TextMesh banner;
    Transform bannerAnchor;

    void LateUpdate()
    {
        if (!Application.isPlaying || Decided)
        {
            PlaceBanner();
            return;
        }

        if (!Ready())
            return;

        Hd2Team team = SharedTeam();
        if (team == Hd2Team.None)
        {
            pending = Hd2Team.None;
            heldFor = 0f;
            return;
        }

        if (pending != team)
        {
            pending = team;
            heldFor = 0f;
        }

        heldFor += Time.deltaTime;
        if (heldFor >= HoldSeconds)
            Declare(team);
    }

    bool Ready()
    {
        if (pointA != null && pointB != null && pointC != null)
            return true;

        var all = Hd2TeamButton.All;
        for (int i = 0; i < all.Count; i++)
        {
            Hd2TeamButton point = all[i];
            if (point == null)
                continue;
            if (point.Label == "A")
                pointA = point;
            else if (point.Label == "B")
                pointB = point;
            else if (point.Label == "C")
                pointC = point;
        }

        return pointA != null && pointB != null && pointC != null;
    }

    Hd2Team SharedTeam()
    {
        Hd2Team team = pointA.Owner;
        if (team == Hd2Team.None || pointB.Owner != team || pointC.Owner != team)
            return Hd2Team.None;
        return team;
    }

    void Declare(Hd2Team team)
    {
        if (Decided)
            return;

        Decided = true;
        Camera view = Camera.main;
        if (view == null)
        {
            var players = FindObjectsByType<Hd2Locomotion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (players != null && players.Length > 0)
                view = players[0].GetComponentInChildren<Camera>();
        }

        if (view == null)
            return;

        bannerAnchor = view.transform;
        var bannerObject = new GameObject("DominationWinner");
        bannerObject.transform.SetParent(bannerAnchor, false);
        bannerObject.transform.localPosition = new Vector3(0f, -0.08f, 2f);
        bannerObject.transform.localRotation = Quaternion.identity;
        banner = bannerObject.AddComponent<TextMesh>();
        banner.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        banner.fontSize = 64;
        banner.characterSize = 0.045f;
        banner.anchor = TextAnchor.MiddleCenter;
        banner.alignment = TextAlignment.Center;
        banner.color = pointA.MarkerColor;
        banner.text = team == Hd2Team.Blue ? "Blue wins" : "Red wins";
        var renderer = banner.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (banner.font != null)
                renderer.sharedMaterial = banner.font.material;
        }
    }

    void PlaceBanner()
    {
        if (banner == null || bannerAnchor == null)
            return;
        banner.transform.localPosition = new Vector3(0f, -0.08f, 2f);
        banner.transform.localRotation = Quaternion.identity;
    }
}
