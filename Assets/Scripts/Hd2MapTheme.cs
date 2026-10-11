using UnityEngine;

/// <summary>
/// Swaps floor, wall, ramp, trim, and water materials on the deathmatch map.
/// The choice is remembered on this headset.
/// </summary>
public class Hd2MapTheme : MonoBehaviour
{
    public enum Role
    {
        Floor,
        Wall,
        Ramp,
        Trim
    }

    [System.Serializable]
    public class Slot
    {
        public string label;
        public Material floor;
        public Material wall;
        public Material ramp;
        public Material trim;
        public Material water;
        public Material floorB;
        public Material floorC;
        public Material wallB;
        public Material wallC;
    }

    const string PrefKey = "Hd2MapTheme";

    public Slot[] themes;
    public int current;

    public static Hd2MapTheme Find()
    {
        return FindFirstObjectByType<Hd2MapTheme>();
    }

    void Awake()
    {
        int saved = PlayerPrefs.GetInt(PrefKey, current);
        Apply(saved, false);
    }

    public void Apply(int index)
    {
        Apply(index, true);
    }

    public void Apply(int index, bool remember)
    {
        if (themes == null || themes.Length == 0)
            return;

        index = Mathf.Clamp(index, 0, themes.Length - 1);
        current = index;
        if (remember)
        {
            PlayerPrefs.SetInt(PrefKey, index);
            PlayerPrefs.Save();
        }

        Slot slot = themes[index];
        var surfaces = GetComponentsInChildren<Hd2MapSurface>(true);
        for (int i = 0; i < surfaces.Length; i++)
        {
            var renderer = surfaces[i].GetComponent<Renderer>();
            if (renderer == null)
                continue;
            int variant = Variant(surfaces[i].transform, surfaces[i].role);
            Material material = MaterialFor(slot, surfaces[i].role, variant);
            if (material != null)
                renderer.sharedMaterial = material;
        }

        Transform water = transform.Find("Water");
        if (water != null && slot.water != null)
        {
            var renderer = water.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = slot.water;
        }
    }

    // 0 keeps the main material, 1 is the wood alternate, 2 is the earth or sandstone alternate.
    static int Variant(Transform panel, Role role)
    {
        Vector3 position = panel.position;
        if (role == Role.Wall)
        {
            if (position.y < 2f)
                return 2;
            if (position.y < 9f)
                return 0;
            return 1;
        }

        if (role == Role.Floor)
        {
            var renderer = panel.GetComponent<Renderer>();
            float top = renderer != null
                ? renderer.bounds.max.y
                : position.y + Mathf.Abs(panel.lossyScale.y) * 0.5f;
            if (top < 0f)
                return 2;
            if (top < 3f)
                return 0;
            return 1;
        }

        return 0;
    }

    static Material MaterialFor(Slot slot, Role role, int variant)
    {
        switch (role)
        {
            case Role.Wall:
                if (variant == 1 && slot.wallB != null)
                    return slot.wallB;
                if (variant == 2 && slot.wallC != null)
                    return slot.wallC;
                return slot.wall;
            case Role.Ramp:
                return slot.ramp;
            case Role.Trim:
                return slot.trim;
            default:
                if (variant == 1 && slot.floorB != null)
                    return slot.floorB;
                if (variant == 2 && slot.floorC != null)
                    return slot.floorC;
                return slot.floor;
        }
    }
}
