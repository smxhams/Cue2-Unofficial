// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot.Collections;

namespace Cue2.Domain.Cues;

public interface ICueComponent
{
    string Type { get; }
    Dictionary GetData();
    void LoadFromData(Dictionary data);
}