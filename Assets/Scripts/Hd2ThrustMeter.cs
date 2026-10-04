using UnityEngine;

/// <summary>
/// Segmented thrust bar drawn under the pointer target. No HUD.
/// Starts at 3 charges. A blitz spends one whole block. A jump spends a fraction of a block.
/// A missing block returns over rechargeSeconds unless the player is sprinting.
/// A kill adds one immediately, even while sprinting.
/// Pickups can later raise the maximum to 5 and refill.
/// </summary>
public class Hd2ThrustMeter : MonoBehaviour
{
    public int startingCharges = 3;
    public int maxCharges = 3;
    public int chargeCap = 5;
    public float rechargeSeconds = 3f;

    float charges;
    Transform blocksRoot;
    readonly Transform[] blocks = new Transform[5];
    static readonly Color filled = new Color(0.25f, 0.85f, 1f, 1f);
    static readonly Color empty = new Color(0.12f, 0.14f, 0.16f, 1f);
    Material filledMaterial;
    Material emptyMaterial;

    public float Charges => charges;
    public int MaxCharges => maxCharges;

    void Awake()
    {
        maxCharges = Mathf.Clamp(maxCharges, 1, chargeCap);
        charges = Mathf.Clamp(startingCharges, 0, maxCharges);
        filledMaterial = MakeMaterial(filled);
        emptyMaterial = MakeMaterial(empty);
    }

    void LateUpdate()
    {
        var locomotion = GetComponent<Hd2Locomotion>();
        bool sprinting = locomotion != null && locomotion.IsSprinting;
        if (!sprinting && charges < maxCharges)
        {
            charges = Mathf.Min(maxCharges, charges + Time.deltaTime / Mathf.Max(0.01f, rechargeSeconds));
            Refresh();
        }
    }

    public bool TrySpend()
    {
        return SpendUpTo(1f) >= 1f - 0.001f;
    }

    public float SpendUpTo(float amount)
    {
        if (amount <= 0f || charges <= 0f)
            return 0f;

        float spent = Mathf.Min(amount, charges);
        charges -= spent;
        Refresh();
        return spent;
    }

    public void AddCharge()
    {
        if (charges >= maxCharges)
            return;

        charges++;
        Refresh();
    }

    public void SetMaxCharges(int maximum, bool refill)
    {
        maxCharges = Mathf.Clamp(maximum, 1, chargeCap);
        if (refill)
            charges = maxCharges;
        else
            charges = Mathf.Min(charges, maxCharges);
        Refresh();
    }

    public void ResetToBase()
    {
        maxCharges = Mathf.Clamp(startingCharges, 1, chargeCap);
        charges = maxCharges;
        Refresh();
    }

    public void ShowUnderTarget(Vector3 feet, Transform view)
    {
        EnsureBlocks();
        if (blocksRoot == null)
            return;

        Vector3 toward = Vector3.back;
        if (view != null)
        {
            toward = view.position - feet;
            toward.y = 0f;
        }

        if (toward.sqrMagnitude < 0.001f)
            toward = Vector3.back;
        toward.Normalize();

        blocksRoot.position = feet + toward * 0.38f + Vector3.up * 0.06f;
        blocksRoot.rotation = Quaternion.LookRotation(toward);

        blocksRoot.gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        if (blocksRoot != null)
            blocksRoot.gameObject.SetActive(false);
    }

    void EnsureBlocks()
    {
        if (blocksRoot != null)
            return;

        var root = new GameObject("ThrustMeter");
        root.transform.SetParent(transform, false);
        blocksRoot = root.transform;

        for (int i = 0; i < blocks.Length; i++)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "ThrustBlock" + (i + 1);
            var collider = block.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            block.transform.SetParent(blocksRoot, false);
            blocks[i] = block.transform;
        }

        Hide();
    }

    void Refresh()
    {
        if (blocksRoot == null)
            return;

        const float width = 0.084f;
        const float gap = 0.0144f;
        float step = width + gap;
        int fullBars = Mathf.Clamp(Mathf.FloorToInt(charges + 0.0001f), 0, maxCharges);
        float partial = Mathf.Clamp01(charges - fullBars);

        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] == null)
                continue;

            bool shown = i < maxCharges;
            blocks[i].gameObject.SetActive(shown);
            if (!shown)
                continue;

            float amount = i < fullBars ? 1f : (i == fullBars && partial > 0.001f ? partial : 0.08f);
            float shownWidth = width * Mathf.Clamp(amount, 0.08f, 1f);
            float slotCenter = ((maxCharges - 1) * 0.5f - i) * step;
            float userLeftEdge = slotCenter + width * 0.5f;
            blocks[i].localScale = new Vector3(shownWidth, 0.0144f, 0.024f);
            blocks[i].localPosition = new Vector3(userLeftEdge - shownWidth * 0.5f, 0f, 0f);

            var renderer = blocks[i].GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = i < fullBars || (i == fullBars && partial > 0.001f) ? filledMaterial : emptyMaterial;
        }
    }

    static Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var material = new Material(shader);
        material.color = color;
        return material;
    }
}
