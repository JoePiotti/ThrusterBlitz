using System.Collections;
using UnityEngine;

/// <summary>
/// Greybox target. Each hit flashes the board, bumps a world-space count,
/// and leaves a dark mark parented to this object at the hit point.
/// Marks stay until the target is destroyed.
/// </summary>
public class Hd2ShootTarget : MonoBehaviour
{
    public Renderer board;
    public float markSize = 0.055f;
    public float markLift = 0.012f;

    int hits;
    TextMesh score;
    MaterialPropertyBlock block;
    Color baseColor = new Color(0.92f, 0.9f, 0.82f, 1f);
    Coroutine flash;
    static Material markMaterial;

    void Awake()
    {
        if (board == null)
            board = GetComponentInChildren<Renderer>();

        block = new MaterialPropertyBlock();
        if (board != null && board.sharedMaterial != null && board.sharedMaterial.HasProperty("_BaseColor"))
            baseColor = board.sharedMaterial.GetColor("_BaseColor");

        var scoreObject = new GameObject("HitScore");
        scoreObject.transform.SetParent(transform, false);
        scoreObject.transform.localPosition = new Vector3(0f, 2.15f, -0.12f);
        scoreObject.transform.localRotation = Quaternion.identity;
        score = scoreObject.AddComponent<TextMesh>();
        score.fontSize = 48;
        score.characterSize = 0.04f;
        score.anchor = TextAnchor.MiddleCenter;
        score.alignment = TextAlignment.Center;
        score.color = Color.white;
        score.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        score.text = "Hits 0";
    }

    public void RegisterHit(Vector3 point, Vector3 normal)
    {
        hits++;
        if (score != null)
            score.text = "Hits " + hits;

        LeaveMark(point, normal);
        if (flash != null)
            StopCoroutine(flash);
        flash = StartCoroutine(Flash());
    }

    void LeaveMark(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = -transform.forward;

        var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mark.name = "HitMark";
        var collider = mark.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        mark.transform.SetParent(transform, true);
        mark.transform.position = point + normal.normalized * markLift;
        mark.transform.rotation = Quaternion.LookRotation(normal.normalized);
        mark.transform.localScale = Vector3.one * markSize;

        var renderer = mark.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = MarkMaterial();
    }

    static Material MarkMaterial()
    {
        if (markMaterial != null)
            return markMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        markMaterial = new Material(shader);
        markMaterial.color = new Color(0.08f, 0.07f, 0.06f, 1f);
        return markMaterial;
    }

    IEnumerator Flash()
    {
        SetBoardColor(Color.white);
        yield return new WaitForSeconds(0.08f);
        SetBoardColor(baseColor);
        flash = null;
    }

    void SetBoardColor(Color color)
    {
        if (board == null)
            return;
        board.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        board.SetPropertyBlock(block);
    }
}
