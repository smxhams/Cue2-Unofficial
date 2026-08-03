// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;

namespace Cue2.Media.Audio;

/// <summary>
/// Fixed-capacity interleaved float PCM ring buffer.
/// Thread-safe for a single producer and single consumer via a lock.
/// Capacity is measured in float samples (channels are the caller's concern).
/// </summary>
public sealed class PcmRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _lock = new object();
    private int _readIndex;
    private int _writeIndex;
    private int _count;

    /// <summary>
    /// Creates a ring buffer with the given capacity in float samples.
    /// </summary>
    /// <param name="capacitySamples">Maximum number of float samples stored.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is less than 1.</exception>
    public PcmRingBuffer(int capacitySamples)
    {
        if (capacitySamples < 1)
            throw new ArgumentOutOfRangeException(nameof(capacitySamples), "Capacity must be at least 1.");

        _buffer = new float[capacitySamples];
        Capacity = capacitySamples;
    }

    /// <summary>
    /// Total capacity in float samples.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Number of float samples currently available to read.
    /// </summary>
    public int Available
    {
        get
        {
            lock (_lock) return _count;
        }
    }

    /// <summary>
    /// Free space in float samples available to write.
    /// </summary>
    public int Free
    {
        get
        {
            lock (_lock) return Capacity - _count;
        }
    }

    /// <summary>
    /// Writes as many samples as fit from <paramref name="src"/>. Returns samples written.
    /// </summary>
    public int Write(ReadOnlySpan<float> src)
    {
        if (src.IsEmpty) return 0;

        lock (_lock)
        {
            int toWrite = Math.Min(src.Length, Capacity - _count);
            if (toWrite == 0) return 0;

            int first = Math.Min(toWrite, Capacity - _writeIndex);
            src.Slice(0, first).CopyTo(_buffer.AsSpan(_writeIndex, first));
            int remaining = toWrite - first;
            if (remaining > 0)
            {
                src.Slice(first, remaining).CopyTo(_buffer.AsSpan(0, remaining));
            }

            _writeIndex = (_writeIndex + toWrite) % Capacity;
            _count += toWrite;
            return toWrite;
        }
    }

    /// <summary>
    /// Reads up to <paramref name="dst"/>.Length samples into <paramref name="dst"/>. Returns samples read.
    /// </summary>
    public int Read(Span<float> dst)
    {
        if (dst.IsEmpty) return 0;

        lock (_lock)
        {
            int toRead = Math.Min(dst.Length, _count);
            if (toRead == 0) return 0;

            int first = Math.Min(toRead, Capacity - _readIndex);
            _buffer.AsSpan(_readIndex, first).CopyTo(dst.Slice(0, first));
            int remaining = toRead - first;
            if (remaining > 0)
            {
                _buffer.AsSpan(0, remaining).CopyTo(dst.Slice(first, remaining));
            }

            _readIndex = (_readIndex + toRead) % Capacity;
            _count -= toRead;
            return toRead;
        }
    }

    /// <summary>
    /// Clears all buffered samples.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
        }
    }
}
