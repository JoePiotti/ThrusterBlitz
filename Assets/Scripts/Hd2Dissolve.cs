using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dissolves a dead body instead of making it vanish: every mesh under the root gets the
/// HD2/Dissolve material (keeping its texture and color), which breaks up in noise with a
/// glowing edge in the given color. Only the dissolving body pays for it, for well under
/// a second. Bots keep their materials in a Saved list and get them back on respawn;
/// a player's ragdoll copy is destroyed afterwards, so it needs nothing back.
/// </summary>
public static class Hd2Dissolve
{
    public const float Seconds = 0.8f;

    public sealed class Saved
    {
        public readonly List<Renderer> renderers = new List<Renderer>();
        public readonly List<Material[]> materials = new List<Material[]>();
        public readonly List<Material> made = new List<Material>();
    }

    static Material template;
    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    static readonly int EdgeId = Shader.PropertyToID("_EdgeColor");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>Runs the dissolve on everything under root. Pass a Saved to be able to Restore.</summary>
    public static IEnumerator Run(Transform root, Color edge, Saved saved = null)
    {
        if (root == null)
            yield break;
        if (template == null)
            template = Resources.Load<Material>("Dissolve");
        if (template == null)
            yield break;

        var mine = saved ?? new Saved();
        var swapped = new List<Material>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        Color glow = edge.maxColorComponent > 0.001f ? edge / edge.maxColorComponent : Color.white;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer))
                continue;
            if (renderer.GetComponent<TextMesh>() != null)
                continue;

            Material[] original = renderer.sharedMaterials;
            var replaced = new Material[original.Length];
            for (int m = 0; m < original.Length; m++)
            {
                var material = new Material(template);
                CopyLook(original[m], material);
                material.SetColor(EdgeId, glow);
                material.SetFloat(DissolveId, 0f);
                replaced[m] = material;
                swapped.Add(material);
                mine.made.Add(material);
            }

            mine.renderers.Add(renderer);
            mine.materials.Add(original);
            renderer.sharedMaterials = replaced;
        }

        float elapsed = 0f;
        while (elapsed < Seconds)
        {
            elapsed += Time.deltaTime;
            // Ease in: the first holes open slowly, then the body goes quickly.
            float t = Mathf.Clamp01(elapsed / Seconds);
            float amount = t * t * (3f - 2f * t) * 1.05f;
            for (int i = 0; i < swapped.Count; i++)
            {
                if (swapped[i] != null)
                    swapped[i].SetFloat(DissolveId, amount);
            }

            yield return null;
        }

        if (saved == null)
            Restore(mine);
    }

    /// <summary>Gives the bodies their own materials back and frees the dissolve copies.</summary>
    public static void Restore(Saved saved)
    {
        if (saved == null)
            return;
        for (int i = 0; i < saved.renderers.Count; i++)
        {
            if (saved.renderers[i] != null)
                saved.renderers[i].sharedMaterials = saved.materials[i];
        }

        for (int i = 0; i < saved.made.Count; i++)
        {
            if (saved.made[i] != null)
                Object.Destroy(saved.made[i]);
        }

        saved.renderers.Clear();
        saved.materials.Clear();
        saved.made.Clear();
    }

    static void CopyLook(Material from, Material to)
    {
        if (from == null)
            return;
        Texture map = from.HasProperty(BaseMapId) ? from.GetTexture(BaseMapId)
            : from.HasProperty(MainTexId) ? from.GetTexture(MainTexId) : null;
        if (map != null)
            to.SetTexture(BaseMapId, map);
        if (from.HasProperty(BaseColorId))
            to.SetColor(BaseColorId, from.GetColor(BaseColorId));
        else if (from.HasProperty(ColorId))
            to.SetColor(BaseColorId, from.GetColor(ColorId));
    }
}
