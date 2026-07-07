using UnityEngine;

/// <summary>
/// Picks one sprite at random from a list and assigns it to this GameObject's
/// SpriteRenderer on spawn, so a single prefab can render as any of several
/// look-alike variants without needing a separate prefab per sprite.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class RandomSpriteRenderer : MonoBehaviour
{
    [Tooltip("Sprite options to choose from at random.")]
    [SerializeField] private Sprite[] sprites;

    private void Awake()
    {
        if (sprites == null || sprites.Length == 0) return;

        GetComponent<SpriteRenderer>().sprite = sprites[Random.Range(0, sprites.Length)];
    }
}
