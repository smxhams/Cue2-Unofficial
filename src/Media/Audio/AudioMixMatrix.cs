using System;
using System.Collections.Generic;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;

namespace Cue2.Media.Audio;

/// <summary>
/// Pure float PCM mixing utilities for cue volume, stereo pan, and routing matrices.
/// No Godot or SDL dependencies — safe to unit-test.
/// </summary>
/// <remarks>
/// Signal flow per sample: source → component volume × master volume → stereo pan (L/R only)
/// → cue routing matrix → output patch / direct device.
/// </remarks>
public static class AudioMixMatrix
{
    /// <summary>
    /// Equal-power stereo balance gains for pan in [−1, 1].
    /// Center (0) keeps both channels at unity; full left/right fully attenuates the opposite side.
    /// </summary>
    /// <param name="pan">Pan position (−1 = full left, 0 = center, +1 = full right).</param>
    /// <param name="leftGain">Linear gain for the left (channel 0) source.</param>
    /// <param name="rightGain">Linear gain for the right (channel 1) source.</param>
    public static void GetStereoPanGains(float pan, out float leftGain, out float rightGain)
    {
        pan = Math.Clamp(pan, -1f, 1f);
        if (pan <= 0f)
        {
            leftGain = 1f;
            // cos(0)=1 at center; cos(π/2)=0 at full left
            rightGain = MathF.Cos((-pan) * (MathF.PI * 0.5f));
        }
        else
        {
            leftGain = MathF.Cos(pan * (MathF.PI * 0.5f));
            rightGain = 1f;
        }
    }

    /// <summary>
    /// Per-input-channel gain from stereo pan. Non-stereo or out-of-range channels return 1.
    /// </summary>
    public static float GetInputPanGain(float pan, int inChannels, int channelIndex)
    {
        if (inChannels != 2 || channelIndex < 0 || channelIndex > 1)
            return 1f;
        GetStereoPanGains(pan, out float left, out float right);
        return channelIndex == 0 ? left : right;
    }

    /// <summary>
    /// Mixes interleaved source PCM into an output buffer for a single device.
    /// Applies master volume, optional stereo pan (before routing), optional CuePatch,
    /// and optional AudioOutputPatch device mapping.
    /// </summary>
    /// <param name="source">Interleaved float32 source samples (frames * inChannels).</param>
    /// <param name="frames">Number of sample-frames.</param>
    /// <param name="inChannels">Source channel count.</param>
    /// <param name="output">Destination interleaved float32 (frames * outChannels); cleared then filled.</param>
    /// <param name="outChannels">Output channel count for this stream (must match stream creation).</param>
    /// <param name="masterVolume">Runtime master volume [0,1] (includes fades).</param>
    /// <param name="componentVolume">Cue component volume [0,1].</param>
    /// <param name="pan">Stereo pan [−1,1]; applied only when <paramref name="inChannels"/> is 2.</param>
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
        float pan,
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

        // Pre-routing pan gains (identity for mono / multi-channel beyond stereo, and at center).
        float panL = 1f;
        float panR = 1f;
        bool applyStereoPan = inChannels == 2 && Math.Abs(pan) > 1e-6f;
        if (applyStereoPan)
            GetStereoPanGains(pan, out panL, out panR);

        if (isDirectOutput)
        {
            MixDirect(source, frames, inChannels, output, outChannels, gain, panL, panR, applyStereoPan, routing);
            return;
        }

        if (!string.IsNullOrEmpty(deviceName) &&
            patch?.OutputDevices != null &&
            patch.OutputDevices.TryGetValue(deviceName, out var deviceOutputs) &&
            deviceOutputs != null &&
            deviceOutputs.Count > 0)
        {
            MixPatched(source, frames, inChannels, output, outChannels, gain, panL, panR, applyStereoPan,
                routing, deviceOutputs, patch.Volume);
            return;
        }

        // Fallback: 1:1 with master gain (+ stereo pan when applicable)
        int ch = Math.Min(inChannels, outChannels);
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < ch; c++)
            {
                float channelGain = gain * ChannelPanGain(c, panL, panR, applyStereoPan);
                output[f * outChannels + c] = source[f * inChannels + c] * channelGain;
            }
        }
    }

    private static float ChannelPanGain(int inCh, float panL, float panR, bool applyStereoPan)
    {
        if (!applyStereoPan) return 1f;
        return inCh == 0 ? panL : inCh == 1 ? panR : 1f;
    }

    private static void MixDirect(
        ReadOnlySpan<float> source,
        int frames,
        int inChannels,
        Span<float> output,
        int outChannels,
        float gain,
        float panL,
        float panR,
        bool applyStereoPan,
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
                        float panGain = ChannelPanGain(inCh, panL, panR, applyStereoPan);
                        sample += source[f * inChannels + inCh] * gain * panGain * routing.GetVolume(inCh, outCh);
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
                float panGain = ChannelPanGain(c, panL, panR, applyStereoPan);
                output[f * outChannels + c] = source[f * inChannels + c] * gain * panGain;
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
        float panL,
        float panR,
        bool applyStereoPan,
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

                        float panGain = ChannelPanGain(inCh, panL, panR, applyStereoPan);
                        sample += source[f * inChannels + inCh] * totalGain * panGain * routeGain;
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
