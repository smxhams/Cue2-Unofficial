using System.Linq;
using Cue2.Base.Classes;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Utilities;

/// <summary>
/// A utility class for UI elements that need to inspect Cue components.
/// </summary>
public partial class CueComponentChecker : Node
{
    
    /// <summary>
    /// Checks if the given Cue contains a component of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of ICueComponent to check for (e.g., AudioComponent).</typeparam>
    /// <param name="cue">The Cue instance to inspect.</param>
    /// <returns>True if at least one component of type T is present; otherwise, false.</returns>
    public static bool HasComponent<T>(Cue cue) where T : ICueComponent
    {
        if (cue == null)
        {
            GD.Print("CueComponentChecker:HasComponent - Attempted to check component on null Cue.");
            return false;
        }

        try
        {
            return cue.Components.OfType<T>().Any();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"CueComponentChecker:HasComponent - Error checking component: {ex.Message}");
            return false;
        }
    }
    
}