using UnityEngine;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Strips scene cameras and lights that a DCC export left on the robot model.
/// An imported camera has no headset tracking, and URP still draws it to the
/// headset when its depth matches CenterEye.
/// </summary>
public static class Hd2RobotCleanup
{
    public static void StripImportedDevices(GameObject robot)
    {
        if (robot == null)
            return;

        Camera[] cameras = robot.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || IsTrackedPlayerCamera(camera))
                continue;

            camera.enabled = false;
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
            TrackedPoseDriver driver = camera.GetComponent<TrackedPoseDriver>();
            if (driver != null)
                driver.enabled = false;

            if (camera.transform == robot.transform)
                Object.Destroy(camera);
            else
                Object.Destroy(camera.gameObject);
        }

        AudioListener[] listeners = robot.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null || IsTrackedPlayerCamera(listener.GetComponent<Camera>()))
                continue;
            listener.enabled = false;
            Object.Destroy(listener);
        }

        TrackedPoseDriver[] drivers = robot.GetComponentsInChildren<TrackedPoseDriver>(true);
        for (int i = 0; i < drivers.Length; i++)
        {
            TrackedPoseDriver driver = drivers[i];
            if (driver == null || IsTrackedPlayerCamera(driver.GetComponent<Camera>()))
                continue;
            driver.enabled = false;
            Object.Destroy(driver);
        }

        Light[] lights = robot.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null)
                continue;
            light.enabled = false;
            if (light.transform == robot.transform || light.GetComponent<Renderer>() != null)
                Object.Destroy(light);
            else
                Object.Destroy(light.gameObject);
        }
    }

    static bool IsTrackedPlayerCamera(Camera camera)
    {
        return camera != null
            && camera.CompareTag("MainCamera")
            && camera.GetComponent<TrackedPoseDriver>() != null;
    }
}
