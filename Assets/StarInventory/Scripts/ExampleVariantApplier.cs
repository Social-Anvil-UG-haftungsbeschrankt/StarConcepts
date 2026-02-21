using UnityEngine;

public sealed class ExampleVariantApplier : MonoBehaviour, IIconVariantApplier
{
    // Variant rule: enable only the child GameObject whose name matches variantId (others off).
    // If variantId empty -> enable all.
    public void Apply(GameObject instance, string variantId)
    {
        if (instance == null) return;

        if (string.IsNullOrEmpty(variantId))
        {
            SetAll(instance.transform, true);
            return;
        }

        for (int i = 0; i < instance.transform.childCount; i++)
        {
            var c = instance.transform.GetChild(i);
            c.gameObject.SetActive(c.name == variantId);
        }
    }

    private static void SetAll(Transform root, bool on)
    {
        root.gameObject.SetActive(on);
        for (int i = 0; i < root.childCount; i++) SetAll(root.GetChild(i), on);
    }
}
