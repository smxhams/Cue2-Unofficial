using System;
using Godot;

namespace Cue2.Shared;

public static class SceneLoader
{
    
    // Loads or retrieves a cached PackedScene and instantiates it.
    public static Node LoadScene(string path, out string errorMessage)
    {
        PackedScene scene = LoadPackedSceneInternal(path, out errorMessage);
        if (scene == null)
        {
            return null; // Error message already set by LoadPackedSceneInternal
        }

        try
        {
            Node instance = scene.Instantiate();
            if (instance == null)
            {
                errorMessage = $"Failed to instantiate scene from path: {path}.";
                return null;
            }

            return instance;
        }
        catch (Exception ex)
        {
            errorMessage = $"An error occurred while instantiating scene at path: {path}. Exception: {ex.Message}";
            return null;
        }
    }


    // Loads or retrieves a cached PackedScene without instantiating it.

    public static PackedScene LoadPackedScene(string path, out string errorMessage)
    {
        return LoadPackedSceneInternal(path, out errorMessage);
    }


    // Internal helper method to load or retrieve a cached PackedScene.
    private static PackedScene LoadPackedSceneInternal(string path, out string errorMessage)
    {
        errorMessage = null;

        // Validate input
        if (string.IsNullOrEmpty(path))
        {
            errorMessage = "Scene path cannot be null or empty.";
            return null;
        }

        try
        {
            PackedScene scene;

            if (ResourceLoader.HasCached(path))
            {
                scene = (PackedScene)ResourceLoader.Load(path);
                //GD.Print($"Retrieved cached PackedScene: {path}");
            }
            else
            {
                scene = GD.Load<PackedScene>(path);
                //GD.Print($"Loaded PackedScene freshly and cached it: {path}");
            }

            if (scene == null)
            {
                errorMessage = $"Failed to load PackedScene at path: {path}. Resource not found or invalid.";
                return null;
            }

            return scene;
        }
        catch (Exception ex)
        {
            errorMessage = $"An error occurred while loading PackedScene at path: {path}. Exception: {ex.Message}";
            return null;
        }
    }
}