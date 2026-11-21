# SoundSeal

<img src="src/MeetingRecorder/Assets/SoundSeal.png" width="120" height="120" alt="The SoundSeal seal wearing dark sunglasses and turquoise headphones">

SoundSeal records your microphone and the audio you hear in an online meeting into a single WAV file on your computer. It runs offline on Windows 11 x64. Click **Start recording** when you're ready. After the meeting, you can send the saved file to a transcription service of your choice.

## Screenshots

| Choose audio sources | Record a meeting |
| --- | --- |
| [![SoundSeal ready to record, with selectors for the microphone, playback device, and recording folder](docs/screenshots/ready.png)](docs/screenshots/ready.png) | [![SoundSeal recording, with the elapsed time and active signal meters for both audio sources](docs/screenshots/recording.png)](docs/screenshots/recording.png) |

[View the saved recording and its **Copy link** button](docs/screenshots/saved.png)

## Build and run

Install the **.NET 10 SDK for x64** and run these commands in PowerShell from the repository root. The first restore requires access to NuGet or a local cache containing the required packages.

```powershell
dotnet restore .\src\MeetingRecorder.slnx
dotnet build .\src\MeetingRecorder.slnx -c Release --no-restore
dotnet test .\src\MeetingRecorder.slnx -c Release --no-build
dotnet run --project .\src\MeetingRecorder\MeetingRecorder.csproj -c Release --no-build
```

After building, you can launch `src\MeetingRecorder\bin\Release\net10.0-windows\SoundSeal.exe` directly. To run the build on another computer, copy the entire output directory and install the **.NET 10 Desktop Runtime for x64** there.

## Record a meeting

1. Under **Microphone**, choose your input device. Under **Computer audio**, choose the headphones or speakers you use to hear the other participants. SoundSeal initially selects the Windows defaults and keeps your selected devices for the entire recording, including pauses.
2. Check **Recording folder**. The default is `%LOCALAPPDATA%\MeetingRecorder\Recordings`. Enter a path or click **Browse…** to choose a folder. SoundSeal creates the folder when recording starts if it doesn't already exist.
3. Click **Start recording**. The seal's glasses glow while recording is active, and a red dot appears beside **Recording**. Speak into the microphone, then check the other meter when someone on the call speaks. Each meter shows **Signal detected** or **Silent**. **Elapsed time** keeps counting during silence.
4. Click **Pause** to leave a break out of the file. Both meters show **Paused** and the timer stops. Click **Resume** to continue recording to the same file.
5. Click **Finish recording**. When the status changes to **Saved**, the window shows the duration and full file path. Click **Copy link** beside the path to copy a `file://` link to the WAV, or **Open folder** to find it in File Explorer. Anyone using the link needs access to the same file location. If you close the window during a recording, choose **Finish and exit** to save it or **Return to recorder** to keep the session open.

SoundSeal saves one mono track as 16-bit PCM audio at 16,000 Hz. One hour of active recording uses about 115.2 MB. Filenames include the start time, for example `Meeting-2026-11-21_14-35-08.wav`. If a name is already in use, SoundSeal adds a numeric suffix.

## Troubleshooting

| What you see or hear | What to check |
| --- | --- |
| **Microphone** stays **Silent** while you speak | Check the selected input, the microphone's hardware mute switch, and the Windows input level. In Windows microphone privacy settings, allow desktop apps to access the microphone. |
| **Computer audio** stays **Silent** while someone else speaks | Check that the calling app uses the playback device selected in SoundSeal. WASAPI loopback captures the shared mix on that device, including browser audio and notifications. Protected content and apps using exclusive mode may be unavailable for capture. |
| A device is missing or busy | Connect the device or close the app using it in exclusive mode, then click **Refresh devices**. |
| You hear the other participant twice in the recording | Sound from the speakers can reach the microphone. A headset reduces this echo. |
| **Error** appears after a device disconnects or the recorder is overloaded | Read the message below **Recording folder**. If SoundSeal saved part of the recording, the message shows the WAV file path. Check the devices before clicking **Start recording** again. |
| A disk error occurs or SoundSeal cannot finish writing the file | Check free disk space and write access to the folder. If SoundSeal kept an unfinished file, the window shows its `.wav.partial` path. It may contain audio data with an incomplete WAV header. |

## Testing

Unit tests exercise individual components. Integration tests exercise capture queues, the file system, dependency injection, and the WPF window. To run either category from the repository root, use the corresponding command.

```powershell
dotnet test .\src\MeetingRecorder.slnx -c Release --filter Category=Unit
dotnet test .\src\MeetingRecorder.slnx -c Release --filter Category=Integration
```

For line and branch coverage, run `./scripts/Test-Coverage.ps1` in PowerShell 7. The script runs the full suite in Release, merges Coverlet results from all four test projects, and reports coverage for each application module. It saves the raw JSON reports in a separate directory under `TestResults`.

The four test projects cover recorder states, timing, the WPF window, mixing synthetic audio sources, and the contents of saved WAV files. The clock-drift tests simulate 44.1 kHz and 48 kHz devices with 100 ppm of relative drift over an hour. Once the dependencies are restored, the tests can run offline without a sound card.

To check real hardware, record both sides of a call, try a different playback device, pause and resume, then listen to the result. In a separate short recording, disconnect the selected headset and play the saved segment. For an hour-long call, check the start and end of the audio and compare the WAV duration with the recorder's timer. You can also repeat a short recording offline.

## Solution structure

The solution follows the conventions of DayScope at commit `c9d9845`. Both projects share baseline `.editorconfig` and `Directory.Build.props` settings, use the .NET Generic Host for dependency injection, and have a test project for each layer.

| Project | Responsibilities |
| --- | --- |
| `MeetingRecorder.Domain` | Defines the recording format, time intervals, recorder states, and results. It has no dependencies on other solution projects. |
| `MeetingRecorder.Application` | Coordinates recorder state transitions and defines contracts in `Abstractions`. It depends only on the domain layer. |
| `MeetingRecorder.Infrastructure` | NAudio adapters in `Wasapi` read packets and release the native buffers. The worker queue in `Audio` processes copies, aligns the tracks, and sends PCM samples to the writer in `Files`. |
| `MeetingRecorder` | `Views` contains WPF windows and event handlers. `ViewModels` manages recording and folder navigation. `Platform` handles file system and File Explorer access. `Themes` defines colors and styles. |
