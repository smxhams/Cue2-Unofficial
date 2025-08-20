using System;
using System.Collections.Generic;
using Godot.Collections;

namespace Cue2.Base.Classes;


/// <summary>
/// Represents per-cue routing matrix for audio channels to output channels with volumes.
/// </summary>
public class CuePatch
{
    public int InputChannels { get; private set; }
    public List<string> InputLabels { get; private set; }
    public int OutputChannels { get; private set; }
    public List<string> OutputLabels { get; private set; }
    public float[,] VolumeMatrix { get; private set; } // Linear volumes [input, output]

    /// <summary>
    /// Initializes with defaults: identity mapping at 1.0 where applicable.
    /// </summary>
    /// <param name="inputCh">Input channels.</param>
    /// <param name="inputLabels">Input labels.</param>
    /// <param name="outputCh">Output channels.</param>
    /// <param name="outputLabels">Output labels.</param>
    public CuePatch(int inputCh, List<string> inputLabels, int outputCh, List<string> outputLabels)
    {
        InputChannels = inputCh;
        InputLabels = inputLabels;
        OutputChannels = outputCh;
        OutputLabels = outputLabels;
        VolumeMatrix = new float[inputCh, outputCh];

        // Default 1:1 at 1.0, others 0.0
        var minCh = Math.Min(inputCh, outputCh);
        for (int i = 0; i < minCh; i++)
        {
            VolumeMatrix[i, i] = 1.0f;
        }
    }
    
    /// <summary>
    /// Derfault constructor for deserialisation.
    /// </summary>
    public CuePatch() {}

    
    /// <summary>
    /// Gets volume for specific input-output pair.
    /// </summary>
    /// <param name="inputCh"></param>
    /// <param name="outputCh"></param>
    /// <returns></returns>
    public float GetVolume(int inputCh, int outputCh)
    {
        if (inputCh < 0 || inputCh >= InputChannels || outputCh < 0 || outputCh >= OutputChannels)
        {
            throw new IndexOutOfRangeException("Invalid channel index.");
        }
        return VolumeMatrix[inputCh, outputCh];
    }

    
    /// <summary>
    /// Sets volume for specific input-output pair.
    /// </summary>
    /// <param name="inputCh"></param>
    /// <param name="outputCh"></param>
    /// <param name="linearVol"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public void SetVolume(int inputCh, int outputCh, float linearVol)
    {
        if (inputCh < 0 || inputCh >= InputChannels || outputCh < 0 || outputCh >= OutputChannels)
        {
            throw new IndexOutOfRangeException("Invalid channel index.");
        }

        if (linearVol < 0.0f || linearVol > 1.0f)
        {
            linearVol = Math.Clamp(linearVol, 0.0f, 1.0f);
        }
        VolumeMatrix[inputCh, outputCh] = linearVol;
    }

    /// <summary>
    /// Serialises to Dictionary for saving.
    /// </summary>
    public Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("InputChannels", InputChannels);
        data.Add("InputLabels", new Array<string>(InputLabels));
        data.Add("OutputChannels", OutputChannels);
        data.Add("OutputLabels", new Array<string>(OutputLabels));

        var matrixData = new Godot.Collections.Array();
        for (int i = 0; i < InputChannels; i++)
        {
            var row = new Godot.Collections.Array();
            for (int j = 0; j < OutputChannels; j++)
            {
                row.Add(VolumeMatrix[i, j]);
            }
            matrixData.Add(row);
        }
        data.Add("VolumeMatrix", matrixData);
        return data;
    }
    
    
    /// <summary>
    /// Loads from Dictionary.
    /// </summary>
    public void LoadFromData(Dictionary dataDict)
    {
        InputChannels = (int)dataDict["InputChannels"];
        InputLabels = new List<string>((Array<string>)dataDict["InputLabels"]);
        OutputChannels = (int)dataDict["OutputChannels"];
        OutputLabels = new List<string>((Array<string>)dataDict["OutputLabels"]);

        var matrixData = (Godot.Collections.Array)dataDict["VolumeMatrix"];
        VolumeMatrix = new float[InputChannels, OutputChannels];
        for (int i = 0; i < InputChannels; i++)
        {
            var row = (Godot.Collections.Array)matrixData[i];
            for (int j = 0; j < OutputChannels; j++)
            {
                VolumeMatrix[i, j] = (float)row[j];
            }
        }
    }
    
}