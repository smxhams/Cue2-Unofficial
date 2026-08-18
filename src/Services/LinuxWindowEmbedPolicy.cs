// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot;

namespace Cue2.Services;

/// <summary>
/// Linux-only window policy: embed <see cref="Popup"/> / OptionButton dropdowns inside their
/// parent window, while keeping real app windows as native OS windows.
/// </summary>
/// <remarks>
/// Cue2 keeps <c>embed_subwindows=false</c> so video outputs and Settings/About/Log can be
/// separate desktop windows. On Linux (especially Wayland) native popup windows cannot be
/// placed under the widget that opened them. Enabling embed on the parent viewport and
/// <see cref="Window.ForceNative"/> on non-<see cref="Popup"/> windows fixes OptionButton
/// lists without changing Windows/macOS or turning house screens into embedded views.
/// <para>
/// <see cref="ApplyToAppWindow"/> must run in the <see cref="Window"/> constructor — before
/// the node enters the tree — because Godot decides native vs embedded during enter-tree
/// when the window is already visible.
/// </para>
/// </remarks>
public static class LinuxWindowEmbedPolicy
{
    /// <summary>True when the process is running on Linux.</summary>
    public static bool IsLinux => OS.GetName() == "Linux";

    /// <summary>
    /// Embeds popups in <paramref name="root"/> on Linux. No-op on other platforms.
    /// </summary>
    /// <param name="root">Typically <c>GetTree().Root</c>.</param>
    public static void EnablePopupEmbedding(Window root)
    {
        if (!IsLinux || root == null || !GodotObject.IsInstanceValid(root))
            return;

        root.GuiEmbedSubwindows = true;
        ApplyToAppWindow(root);
        GD.Print("LinuxWindowEmbedPolicy:EnablePopupEmbedding - Embedding popups on Linux; app Windows stay native.");
    }

    /// <summary>
    /// Marks a non-popup <see cref="Window"/> as a native OS window whose own popups embed.
    /// </summary>
    /// <param name="window">App window (Settings, video output, dialog, …). Ignored if a <see cref="Popup"/>.</param>
    public static void ApplyToAppWindow(Window window)
    {
        if (!IsLinux || window == null || !GodotObject.IsInstanceValid(window))
            return;
        if (window is Popup)
            return;

        window.ForceNative = true;
        window.GuiEmbedSubwindows = true;
    }
}
