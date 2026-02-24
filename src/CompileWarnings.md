# Compile Warnings Checklist

This file lists the current compile warnings after fixing CS8632. Each warning is an item to be addressed.

## C# Code Warnings

### CS0108: Hides Inherited Member
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\Connections\CueLight.cs(225,17): warning CS0108: 'CueLight.Dispose()' hides inherited member 'GodotObject.Dispose()'. Use the new keyword if hiding was intended.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\TimelineInspector.cs(584,22): warning CS0108: 'TimelineInspector.Ruler.Scale' hides inherited member 'Control.Scale'. Renamed to ZoomScale.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\Devices\VideoOutputDevice.cs(308,17): warning CS0108: 'VideoOutputDevice.Dispose()' hides inherited member 'GodotObject.Dispose()'. Use the new keyword if hiding was intended.

### CS8602: Dereference of Possibly Null Reference
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(43,9): warning CS8602: Dereference of a possibly null reference.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(57,9): warning CS8602: Dereference of a possibly null reference.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(73,13): warning CS8602: Dereference of a possibly null reference.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Base\Devices.cs(29,26): warning CS8602: Dereference of a possibly null reference.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(171,13): warning CS8602: Dereference of a possibly null reference.
- [x] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(217,13): warning CS8602: Dereference of a possibly null reference.

### CS8603: Possible Null Reference Return
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Devices.cs(48,20): warning CS8603: Possible null reference return.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Devices.cs(53,16): warning CS8603: Possible null reference return.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Devices.cs(75,16): warning CS8603: Possible null reference return.

### CS4014: Not Awaited
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\ActiveAudioPlayback.cs(217,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\ActiveAudioPlayback.cs(313,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\ActiveCue.cs(293,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\Settings.cs(180,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\AudioInspector.cs(180,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\AudioInspector.cs(736,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\AudioInspector.cs(770,13): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\VideoInspector.cs(1057,4): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Inspectors\VideoInspector.cs(1091,4): warning CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.

### CS1998: Async Method Lacks Await
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(138,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Shared\AudioDevices.cs(303,23): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Shared\CueLightManager.cs(238,23): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\ActiveCue.cs(623,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Shared\FFmpegVideoDecoder.cs(125,23): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\ActiveCue.cs(293,13): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Settings\SettingsCueLights.cs(148,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\Connections\CueLight.cs(197,23): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Base\Classes\Connections\CueLight.cs(206,23): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\UI\Scenes\Settings\SettingsCueLights.cs(246,24): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.

### CS0169: Field Never Used
- [ ] C:\MyFiles\Cue2_Home\Cue2\src\Shared\FFmpegAudioDecoder.cs(30,31): warning CS0169: The field 'FFmpegAudioDecoder._metadata' is never used

## NuGet Package Warnings
- [ ] NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json. [C:\MyFiles\Cue2_Home\Cue2\Cue2.sln]
- [ ] NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.

## Summary
- Total Warnings: 22
- C# Warnings: 20
- NuGet Warnings: 2
- Note: All CS0108 warnings fixed by using 'new' or renaming.