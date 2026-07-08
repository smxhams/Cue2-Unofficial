using System;
using System.Collections.Generic;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;

namespace Cue2.Shared.Audio;

/// <summary>
/// Pure float PCM mixing utilities for cue volume and routing matrices.
/// No Godot or SDL dependencies — safe to unit-test.
/// </summary>
public static class AudioMixMatrix
{
    /// <summary>
    /// Mixes interleaved source PCM into an output buffer for a single device.
    /// Applies master volume, optional CuePatch, and optional AudioOutputPatch device mapping.
    /// </summary>
    /// <param name="source">Interleaved float32 source samples (frames * inChannels).</param>
    /// <param name="frames">Number of sample-frames.</param>
    /// <param name="inChannels">Source channel count.</param>
    /// <param name="output">Destination interleaved float32 (frames * outChannels); cleared then filled.</param>
    /// <param name="outChannels">Output channel count for this stream (must match stream creation).</param>
    /// <param name="masterVolume">Runtime master volume [0,1] (includes fades).</param>
    /// <param name="componentVolume">Cue component volume [0,1].</param>
    /// <param name="routing">Optional per-cue channel matrix (source → patch buses).</param>
    /// <param name="patch">Optional global output patch.</param>
    /// <param name="deviceName">Device name for patch lookup; may be null for direct.</param>
    /// <param name="isDirectOutput">True when routing to a named direct device.</param>
    public static void Mix(
        ReadOnlySpan<float> source,
        int frames,
        int inChannels,
        Span<float> output,
        int outChannels,
        float masterVolume,
        float componentVolume,
        CuePatch routing,
        AudioOutputPatch patch,
        string deviceName,
        bool isDirectOutput)
    {
        if (frames <= 0 || inChannels <= 0 || outChannels <= 0)
            return;

        int requiredIn = frames * inChannels;
        int requiredOut = frames * outChannels;
        if (source.Length < requiredIn)
            throw new ArgumentException("Source buffer too short for frame/channel count.", nameof(source));
        if (output.Length < requiredOut)
            throw new ArgumentException("Output buffer too short for frame/channel count.", nameof(output));

        output.Slice(0, requiredOut).Clear();
        float gain = masterVolume * componentVolume;

        if (isDirectOutput)
        {
            MixDirect(source, frames, inChannels, output, outChannels, gain, routing);
            return;
        }

        if (!string.IsNullOrEmpty(deviceName) &&
            patch?.OutputDevices != null &&
            patch.OutputDevices.TryGetValue(deviceName, out var deviceOutputs) &&
            deviceOutputs != null &&
            deviceOutputs.Count > 0)
        {
            MixPatched(source, frames, inChannels, output, outChannels, gain, routing, deviceOutputs, patch.Volume);
            return;
        }

        // Fallback: 1:1 with master gain
        int ch = Math.Min(inChannels, outChannels);
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < ch; c++)
            {
                output[f * outChannels + c] = source[f * inChannels + c] * gain;
            }
        }
    }

    private static void MixDirect(
        ReadOnlySpan<float> source,
        int frames,
        int inChannels,
        Span<float> output,
        int outChannels,
        float gain,
        CuePatch routing)
    {
        if (routing != null)
        {
            int routeOut = Math.Min(outChannels, routing.OutputChannels);
            int routeIn = Math.Min(inChannels, routing.InputChannels);
            for (int f = 0; f < frames; f++)
            {
                for (int outCh = 0; outCh < routeOut; outCh++)
                {
                    float sample = 0f;
                    for (int inCh = 0; inCh < routeIn; inCh++)
                    {
                        sample += source[f * inChannels + inCh] * gain * routing.GetVolume(inCh, outCh);
                    }
                    output[f * outChannels + outCh] = sample;
                }
            }
            return;
        }

        int ch = Math.Min(inChannels, outChannels);
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < ch; c++)
            {
                output[f * outChannels + c] = source[f * inChannels + c] * gain;
            }
        }
    }

    private static void MixPatched(
        ReadOnlySpan<float> source,
        int frames,
        int inChannels,
        Span<float> output,
        int outChannels,
        float gain,
        CuePatch routing,
        List<OutputChannel> deviceOutputs,
        float patchVolume)
    {
        float totalGain = gain * patchVolume;
        int deviceOutCount = Math.Min(outChannels, deviceOutputs.Count);

        for (int f = 0; f < frames; f++)
        {
            for (int outCh = 0; outCh < deviceOutCount; outCh++)
            {
                float sample = 0f;
                var routed = deviceOutputs[outCh].RoutedChannels;
                if (routed == null) continue;

                foreach (int patchCh in routed)
                {
                    for (int inCh = 0; inCh < inChannels; inCh++)
                    {
                        float routeGain;
                        if (routing != null)
                        {
                            if (inCh < routing.InputChannels &&
                                patchCh >= 0 &&
                                patchCh < routing.OutputChannels)
                            {
                                routeGain = routing.GetVolume(inCh, patchCh);
                            }
                            else
                            {
                                routeGain = 0f;
                            }
                        }
                        else
                        {
                            // No cue matrix: identity — patch bus index maps to source channel index
                            routeGain = (inCh == patchCh) ? 1f : 0f;
                        }

                        sample += source[f * inChannels + inCh] * totalGain * routeGain;
                    }
                }

                output[f * outChannels + outCh] = sample;
            }
        }
    }

    /// <summary>
    /// Resolves the output channel count that will be written for a device stream.
    /// Must match the SDL stream source channel count.
    /// </summary>
    public static int ResolveOutputChannelCount(
        int sourceChannels,
        bool isDirectOutput,
        AudioDevice device,
        CuePatch routing,
        AudioOutputPatch patch,
        string deviceName)
    {
        if (isDirectOutput && device != null)
        {
            return device.Channels > 0 ? device.Channels : sourceChannels;
        }

        if (!string.IsNullOrEmpty(deviceName) &&
            patch?.OutputDevices != null &&
            patch.OutputDevices.TryGetValue(deviceName, out var outs) &&
            outs != null &&
            outs.Count > 0)
        {
            return outs.Count;
        }

        if (routing != null)
            return routing.OutputChannels;

        return sourceChannels;
    }
}
