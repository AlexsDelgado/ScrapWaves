using System;
using UnityEngine;

/// <summary>
/// Compatibility entry point retained for existing CI commands. The previous destructive
/// scene generator has been retired; production iteration now happens in the saved scene
/// and is checked by <see cref="TitleScreenAuthoringValidator"/>.
/// </summary>
public static class TitleScreenSceneBuilder
{
    public const string ScenePath = TitleScreenAuthoringValidator.ScenePath;

    [Obsolete("The production title scene is authored by hand. Use TitleScreenAuthoringValidator instead.")]
    public static void Rebuild()
    {
        Debug.LogError(
            "TitleScreenSceneBuilder.Rebuild is retired because rebuilding would overwrite authored menu tuning. " +
            "Use Tools/Scrap Waves/Validate Authored Title Screen.");
    }

    public static void Verify()
    {
        TitleScreenAuthoringValidator.Result result = TitleScreenAuthoringValidator.ValidateAsset();
        TitleScreenAuthoringValidator.Log(result);
        if (!result.IsValid)
            throw new InvalidOperationException($"Title screen authoring validation failed with {result.Errors.Count} error(s).");
    }
}
