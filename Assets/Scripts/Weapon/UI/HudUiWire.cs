using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class HudUiWire
{
    public static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null)
            return null;

        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    public static T FindDeepComponent<T>(Transform root, string name) where T : Component
    {
        Transform t = FindDeepChild(root, name);
        return t != null ? t.GetComponent<T>() : null;
    }

    public static TextMeshProUGUI FindTmp(Transform root, string name) => FindDeepComponent<TextMeshProUGUI>(root, name);

    public static Image FindImage(Transform root, string name) => FindDeepComponent<Image>(root, name);

    public static Button FindButton(Transform root, string name) => FindDeepComponent<Button>(root, name);

    public static Slider FindSlider(Transform root, string name) => FindDeepComponent<Slider>(root, name);

    public static Toggle FindToggle(Transform root, string name) => FindDeepComponent<Toggle>(root, name);

    public static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
