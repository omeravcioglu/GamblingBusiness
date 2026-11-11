using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Store screenshots: F12 in Play mode (Editor and development builds) saves the frame as a 1920 x 1080 PNG, what
/// Steam wants for its gameplay screenshots. Saved in &lt;project&gt;/SteamStore/screenshots (the Editor) or
/// &lt;persistentDataPath&gt;/SteamStore/screenshots (a development build). Set the Game view to 1920x1080 for a sharp
/// shot; another 16:9 size is resized to 1920 x 1080, any other shape isn't saved. Created automatically.
/// </summary>
public class SteamScreenshot : MonoBehaviour
{
    /// <summary>Key that takes a screenshot (F9 is DebugTools' emergency unstick).</summary>
    public static KeyCode Key = KeyCode.F12;

    const int Width = 1920, Height = 1080;
    bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (FindAnyObjectByType<SteamScreenshot>() != null) return;
        var go = new GameObject("SteamScreenshot");
        DontDestroyOnLoad(go);
        go.AddComponent<SteamScreenshot>();
    }

    void Update()
    {
        if (!busy && Input.GetKeyDown(Key)) StartCoroutine(Capture());
    }

    /// <summary>Folder the screenshots go to.</summary>
    public static string Folder => Application.isEditor
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SteamStore", "screenshots"))
        : Path.Combine(Application.persistentDataPath, "SteamStore", "screenshots");

    IEnumerator Capture()
    {
        busy = true;
        yield return new WaitForEndOfFrame();
        Texture2D shot = null, sized = null;
        try
        {
            shot = ScreenCapture.CaptureScreenshotAsTexture();
            int w = shot.width, h = shot.height;
            if (Mathf.Abs(w / (float)h - Width / (float)Height) > 0.01f)
            {
                Debug.LogWarning("[SteamScreenshot] The game view is " + w + " x " + h + ", not 16:9: set it to 1920x1080. Not saved.");
                yield break;
            }
            sized = w == Width && h == Height ? shot : Resize(shot, Width, Height);
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            File.WriteAllBytes(path, sized.EncodeToPNG());
            string note = w == Width ? "" : " (resized from " + w + " x " + h + "; a 1920x1080 game view gives a sharper shot)";
            Debug.Log("[SteamScreenshot] Saved " + path + note);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (sized != null && sized != shot) Destroy(sized);
            if (shot != null) Destroy(shot);
            busy = false;
        }
    }

    /// <summary>Bilinear resize on the CPU (the bytes stay as captured, so colours don't shift).</summary>
    static Texture2D Resize(Texture2D source, int width, int height)
    {
        Color32[] src = source.GetPixels32();
        int sw = source.width, sh = source.height;
        var dst = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float fy = Mathf.Clamp((y + 0.5f) * sh / height - 0.5f, 0f, sh - 1f);
            int y0 = (int)fy, y1 = Mathf.Min(y0 + 1, sh - 1);
            float ty = fy - y0;
            for (int x = 0; x < width; x++)
            {
                float fx = Mathf.Clamp((x + 0.5f) * sw / width - 0.5f, 0f, sw - 1f);
                int x0 = (int)fx, x1 = Mathf.Min(x0 + 1, sw - 1);
                float tx = fx - x0;
                Color32 a = Color32.Lerp(src[y0 * sw + x0], src[y0 * sw + x1], tx);
                Color32 b = Color32.Lerp(src[y1 * sw + x0], src[y1 * sw + x1], tx);
                dst[y * width + x] = Color32.Lerp(a, b, ty);
            }
        }
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(dst);
        result.Apply();
        return result;
    }
}
