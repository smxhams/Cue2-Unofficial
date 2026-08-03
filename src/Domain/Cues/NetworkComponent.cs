// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot.Collections;

namespace Cue2.Domain.Cues;

public class NetworkComponent : ICueComponent
{
    public string Type => "Network";

    public Dictionary GetData()
    {
        return new Dictionary();
    }

    public void LoadFromData(Dictionary data)
    {
        
    }
}