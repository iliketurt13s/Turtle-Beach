using UnityEngine;

/// <summary>
/// Forces turtles' normal ("Turtle") layer to never physically collide with
/// either building layer ("Building" or "InteractableBuilding"), regardless
/// of whatever the Editor's Physics 2D layer collision matrix currently has
/// configured — so an idle/wandering/nest-guarding turtle never gets
/// physically blocked by a building it isn't actively approaching. Every
/// currently-active building (Watchtower included) actually sits on the
/// plain "Building" layer already, and "TurtleInteracting" (switched to only
/// while approaching a specific interactable building — see
/// TurtleAgent.UpdateBuildingCollision) is deliberately left free to collide
/// with both building layers: that's what lets an approaching turtle
/// actually reach and physically bump the rune/Watchtower it's targeting: do
/// not add that pair here.
///
/// Also forces every turtle-vs-turtle layer pairing to never collide, so
/// turtles pass through each other exactly like they already pass through
/// non-interactable buildings — needed because a turtle's own shell collider
/// isn't always on "Turtle": it temporarily switches to "TurtleInteracting"
/// while approaching an interactable building (see UpdateBuildingCollision),
/// so all three pairings a turtle's shell can end up in (Turtle-Turtle,
/// Turtle-TurtleInteracting, TurtleInteracting-TurtleInteracting) need their
/// own exclusion, or two turtles caught on different sides of that layer
/// switch would still physically collide.
///
/// No GameObject or Inspector wiring needed — runs once automatically before
/// the first scene loads.
/// </summary>
public static class TurtleBuildingCollisionSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        int turtle = LayerMask.NameToLayer("Turtle");
        int building = LayerMask.NameToLayer("Building");
        int interactableBuilding = LayerMask.NameToLayer("InteractableBuilding");
        int turtleInteracting = LayerMask.NameToLayer("TurtleInteracting");

        IgnoreIfValid(turtle, building);
        IgnoreIfValid(turtle, interactableBuilding);

        IgnoreIfValid(turtle, turtle);
        IgnoreIfValid(turtle, turtleInteracting);
        IgnoreIfValid(turtleInteracting, turtleInteracting);
    }

    private static void IgnoreIfValid(int layerA, int layerB)
    {
        if (layerA < 0 || layerB < 0) return;
        Physics2D.IgnoreLayerCollision(layerA, layerB, true);
    }
}
