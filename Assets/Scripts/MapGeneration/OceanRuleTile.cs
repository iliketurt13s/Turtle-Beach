using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A Rule Tile that counts ANY tile as itself, and only an empty cell as not
/// itself.
///
/// This exists so the ocean can be drawn cheaply. The shoreline ring has to be
/// a Rule Tile — it picks a different sprite depending on which way the land is
/// — but the open ocean stretching out to the map's edge is tens of thousands
/// of identical cells, and making all of them Rule Tiles would mean evaluating
/// rules against eight neighbours apiece for a sprite that never varies. So
/// IslandGenerator paints a narrow band of this tile around the shallows and
/// fills the rest with plain Tiles.
///
/// A stock RuleTile can't be mixed like that: its This test is `other == this`,
/// so the plain tile immediately seaward of the band reads as "not me" and the
/// band draws a second, wrong coastline facing the open sea — the same edge it
/// correctly draws on the landward side. Matching on OCCUPANCY instead is exact
/// here, because the Water tilemap holds nothing but ocean: every cell on it is
/// one of the ocean tiles, and land and shallow water are left empty on it
/// precisely so this question has an answer. "Is there a tile here?" and "is
/// this still ocean?" are the same question on that map.
///
/// Deliberately configuration-free — there's no sibling list to keep in step
/// with which tiles the generator happens to paint, and no way to wire it
/// wrongly. If the Water tilemap ever holds something that ISN'T ocean, this is
/// the assumption that breaks.
///
/// The stock RuleTile editor handles this class unchanged (its CustomEditor is
/// declared with editorForChildClasses: true), so rules are authored exactly as
/// on any other Rule Tile.
/// </summary>
[CreateAssetMenu(fileName = "OceanRuleTile", menuName = "2D/Tiles/Ocean Rule Tile")]
public class OceanRuleTile : RuleTile
{
    public override bool RuleMatch(int neighbor, TileBase other)
    {
        // Same unwrap the base class does first: an override tile stands in for
        // the rule tile it was created from.
        if (other is RuleOverrideTile overrideTile) other = overrideTile.m_InstanceTile;

        bool isOcean = other != null;

        switch (neighbor)
        {
            case TilingRuleOutput.Neighbor.This: return isOcean;
            case TilingRuleOutput.Neighbor.NotThis: return !isOcean;
        }

        // Any other neighbour type (Don't Care, or one added by a future Unity
        // version) is the base class's business, not this override's.
        return base.RuleMatch(neighbor, other);
    }
}
