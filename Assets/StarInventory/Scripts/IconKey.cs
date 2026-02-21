using System;

[Serializable]
public readonly struct IconKey : IEquatable<IconKey>
{
    public readonly string itemId;
    public readonly string variantId;
    public readonly ViewPreset view;
    public readonly int resolution;
    public readonly string version;

    public IconKey(string itemId, string variantId, ViewPreset view, int resolution, string version)
    {
        this.itemId = itemId ?? "";
        this.variantId = variantId ?? "";
        this.view = view;
        this.resolution = resolution;
        this.version = version ?? "";
    }

    public bool Equals(IconKey other)
    {
        return itemId == other.itemId &&
               variantId == other.variantId &&
               view == other.view &&
               resolution == other.resolution &&
               version == other.version;
    }

    public override bool Equals(object obj) => obj is IconKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + itemId.GetHashCode();
            h = h * 31 + variantId.GetHashCode();
            h = h * 31 + (int)view;
            h = h * 31 + resolution;
            h = h * 31 + version.GetHashCode();
            return h;
        }
    }

    public override string ToString()
        => $"{version}|{resolution}|{itemId}|{variantId}|{view}";
}
