// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Runtime;
using Godot;

namespace Cue2.Media.Audio;

/// <summary>
/// Helpers for reclaiming large media allocations (PCM stores, frame pools).
/// .NET places large arrays on the Large Object Heap, which is not compacted by
/// default — nulling references alone often leaves process working-set high until
/// an LOH-compacting GC runs.
/// </summary>
public static class MediaMemory
{
    /// <summary>Threshold above which we request an LOH-compacting GC after release.</summary>
    public const long CompactThresholdBytes = 8L * 1024 * 1024; // 8 MiB

    private static long _pendingReclaimBytes;

    /// <summary>
    /// Note that approximately <paramref name="bytes"/> of large media memory was released.
    /// Does not collect immediately — call <see cref="ReclaimIfNeeded"/> after a batch of disposals.
    /// </summary>
    public static void NoteReleased(long bytes)
    {
        if (bytes <= 0) return;
        System.Threading.Interlocked.Add(ref _pendingReclaimBytes, bytes);
    }

    /// <summary>
    /// If enough large media has been released since the last compact, run a
    /// blocking LOH-compacting GC so process working set can drop.
    /// Safe to call from the main thread after playback Clean().
    /// </summary>
    public static void ReclaimIfNeeded(long forceIfAtLeastBytes = CompactThresholdBytes)
    {
        long pending = System.Threading.Interlocked.Read(ref _pendingReclaimBytes);
        if (pending < forceIfAtLeastBytes) return;

        System.Threading.Interlocked.Exchange(ref _pendingReclaimBytes, 0);

        GD.Print($"MediaMemory:ReclaimIfNeeded - Compacting LOH after releasing ~{pending / (1024.0 * 1024.0):F1} MiB");

        // Compact LOH once so large float[]/byte[] from PCM stores and frame rings can return.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    /// <summary>
    /// Estimate size of a float sample buffer in bytes.
    /// </summary>
    public static long FloatBufferBytes(float[] buffer) =>
        buffer == null ? 0 : (long)buffer.Length * sizeof(float);

    /// <summary>
    /// Estimate size of a byte buffer in bytes.
    /// </summary>
    public static long ByteBufferBytes(byte[] buffer) =>
        buffer?.Length ?? 0;
}
