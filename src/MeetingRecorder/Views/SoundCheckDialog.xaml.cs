using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using MeetingRecorder.Abstractions;
using MeetingRecorder.Domain;

namespace MeetingRecorder.Views;

/// <summary>Owns a short audio check and releases the devices before returning to the recorder.</summary>
internal partial class SoundCheckDialog : Window
{
    private readonly ISoundCheck _check;
    private readonly EndpointId? _microphone;
    private readonly EndpointId? _output;
    private CancellationTokenSource? _operation;
    private byte[]? _sample;
    private bool _closing;

    internal SoundCheckDialog(ISoundCheck check, Endpoint? microphone, Endpoint? output)
    {
        InitializeComponent();
        _check = check;
        _microphone = microphone?.Id;
        _output = output?.Id;
        MicrophonePanel.Visibility = microphone is null ? Visibility.Collapsed : Visibility.Visible;
        OutputPanel.Visibility = output is null ? Visibility.Collapsed : Visibility.Visible;
        MicrophoneName.Text = $"Microphone · {microphone?.Name}";
        OutputName.Text = $"Computer audio · {output?.Name}";
        Instructions.Text = (output is null ? "" : "A short tone will play on the selected output. ")
            + (microphone is null ? "The check captures five seconds of computer audio." : "Speak for five seconds after starting the check.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operation is not null)
        {
            e.Cancel = true;
            _closing = true;
            _operation.Cancel();
        }
        base.OnClosing(e);
    }

    private async void OnCheckAsync(object sender, RoutedEventArgs e) => await RunAsync(listen: false);

    private async void OnListenAsync(object sender, RoutedEventArgs e) => await RunAsync(listen: true);

    private async Task RunAsync(bool listen)
    {
        if (_operation is not null)
        {
            return;
        }

        using var operation = new CancellationTokenSource();
        _operation = operation;
        CaptureButton.IsEnabled = ListenButton.IsEnabled = false;
        try
        {
            if (listen && _sample is not null)
            {
                Status.Text = "Playing the sample…";
                await _check.PlayAsync(_output, _sample, operation.Token);
                Status.Text = "Playback finished.";
            }
            else
            {
                _sample = null;
                Status.Text = "Checking sound…";
                UpdatePeaks(default);
                var progress = new Progress<Levels>(levels =>
                {
                    if (_operation == operation)
                    {
                        UpdatePeaks(levels);
                    }
                });
                _sample = await _check.CaptureAsync(_microphone, _output, progress, operation.Token);
                Status.Text = "Check complete. Listen to the sample to verify the sound.";
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // Closing waits for capture or playback to release the device.
        }
        catch (RecordingException ex)
        {
            Status.Text = ex.Message;
        }
        finally
        {
            _operation = null;
            if (_closing)
            {
                Close();
            }
            else
            {
                CaptureButton.Content = "Check again";
                CaptureButton.IsEnabled = true;
                ListenButton.IsEnabled = _sample is not null;
            }
        }
    }

    private void UpdatePeaks(Levels levels)
    {
        ShowPeak(MicrophonePeak, MicrophoneSignal, levels.Microphone.Value);
        ShowPeak(OutputPeak, OutputSignal, levels.Computer.Value);
    }

    private static void ShowPeak(ProgressBar meter, TextBlock signal, float peak)
    {
        meter.Value = peak > 0 ? Math.Clamp(1 + (20 * Math.Log10(peak) / 60), 0, 1) : 0;
        signal.Text = peak >= 0.001f ? "Signal detected" : "Silent";
    }
}
