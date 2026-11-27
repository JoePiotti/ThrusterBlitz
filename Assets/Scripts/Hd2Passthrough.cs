using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Mixed reality for the start menu: on the Quest the spaceship cabin's walls, floor, ceiling
/// and wall trim are hidden and the player's real room shows through (Meta passthrough via
/// Unity OpenXR: Meta, features "Meta Quest: Session" and "Meta Quest: Camera" for Android).
/// Only the console, its screen and their lights stay, standing in the room in front of the
/// player. Hd2MainMenu still places them where the player stands, facing the way they look.
///
/// Everything it adds lives in the Title scene, so loading a map ends passthrough: the AR
/// session and camera manager are destroyed with the scene and the map uses its own camera.
/// In the Editor (no passthrough on desktop) the full cabin and skybox stay as before.
/// </summary>
public static class Hd2Passthrough
{
    /// <summary>True on the headset: the menu runs in passthrough.</summary>
    public static bool Active => Application.platform == RuntimePlatform.Android;

    // Cabin parts that make up the room itself. Everything else (console, screen, lights) stays.
    static readonly string[] Shell =
    {
        "Floor", "Ceiling", "BackWall", "FrontWall", "WallLow", "WallHigh", "WallBack", "WallFront"
    };

    /// <summary>
    /// Turns passthrough on for the menu camera and hides the cabin shell. Returns false (and
    /// changes nothing) off the headset.
    /// </summary>
    public static bool Begin(Camera view, Transform cabin)
    {
        if (!Active || view == null)
            return false;

        // AR Foundation needs one session in the scene, and a camera manager on the camera
        // that draws the passthrough behind everything.
        if (Object.FindFirstObjectByType<ARSession>() == null)
            new GameObject("AR Session").AddComponent<ARSession>();
        if (view.GetComponent<ARCameraManager>() == null)
            view.gameObject.AddComponent<ARCameraManager>();

        // The real room shows wherever nothing is drawn: clear to fully transparent black.
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0f, 0f, 0f, 0f);
        var data = view.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
            data.renderPostProcessing = false;

        HideShell(cabin);
        return true;
    }

    static void HideShell(Transform cabin)
    {
        if (cabin == null)
            return;
        Transform room = cabin.Find("ShipRoom");
        if (room == null)
            room = cabin;

        Bounds console = ConsoleBounds(room);
        for (int i = 0; i < room.childCount; i++)
        {
            Transform part = room.GetChild(i);
            bool shell = System.Array.IndexOf(Shell, part.name) >= 0;
            // Trim strips run along the walls; keep only the ones on the console itself.
            if (part.name == "Strip")
                shell = !console.Contains(part.position);
            if (shell)
                part.gameObject.SetActive(false);
        }
    }

    // The console and its screen stand, with a little margin, so their trim strips stay.
    static Bounds ConsoleBounds(Transform room)
    {
        Bounds bounds = new Bounds(room.position, Vector3.zero);
        bool any = false;
        string[] keep = { "ConsoleBase", "ConsoleDesk", "ScreenStand", "ScreenBezel" };
        for (int i = 0; i < keep.Length; i++)
        {
            Transform part = room.Find(keep[i]);
            var renderer = part != null ? part.GetComponent<Renderer>() : null;
            if (renderer == null)
                continue;
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        bounds.Expand(0.1f);
        return bounds;
    }
}
