using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds solid-color placeholder Tile assets at runtime/in-editor so tilemap
/// generation is testable before real sprite art exists. Knows nothing about
/// noise or islands — swap in imported Tile assets on the generator's
/// Inspector fields later and this factory simply stops being called.
/// </summary>
public static class PlaceholderTileFactory
{
    public static Tile CreateSolidColorTile(Color color, string tileName, int textureSize = 8)
    {
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = tileName + "_Texture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        sprite.name = tileName + "_Sprite";

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = tileName;
        tile.sprite = sprite;
        tile.color = Color.white;

        return tile;
    }
}
