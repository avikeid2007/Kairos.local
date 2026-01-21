using CommunityToolkit.Maui.Media;
using System.Diagnostics;
using System.Globalization;

namespace KaiROS.Mobile.Services;

/// <summary>
/// Service for speech-to-text and text-to-speech functionality.
/// Uses CommunityToolkit.Maui.Media and MAUI Essentials APIs.
/// </summary>
public class VoiceService
{
    private CancellationTokenSource? _listeningCts;
    private bool _isListening;
    private bool _isSpeaking;

    public bool IsListening => _isListening;
    public bool IsSpeaking => _isSpeaking;

    public event EventHandler<string>? SpeechRecognized;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ListeningStarted;
    public event EventHandler? ListeningStopped;

    /// <summary>
    /// Start listening for speech input.
    /// </summary>
    public async Task<string?> ListenAsync(CancellationToken cancellationToken = default)
    {
        if (_isListening)
            return null;

        try
        {
            _isListening = true;
            _listeningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            ListeningStarted?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "Listening...");

            // Check microphone permission
            var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Microphone>();
                if (status != PermissionStatus.Granted)
                {
                    StatusChanged?.Invoke(this, "Microphone permission denied");
                    return null;
                }
            }

            // Use SpeechToText from CommunityToolkit.Maui.Media
            var speechToText = SpeechToText.Default;

            var recognitionResult = await speechToText.ListenAsync(
                CultureInfo.CurrentCulture,
                new Progress<string>(partialText =>
                {
                    SpeechRecognized?.Invoke(this, partialText);
                }),
                _listeningCts.Token);

            if (recognitionResult.IsSuccessful)
            {
                StatusChanged?.Invoke(this, "Got it!");
                return recognitionResult.Text;
            }
            else
            {
                StatusChanged?.Invoke(this, $"Recognition failed: {recognitionResult.Exception?.Message}");
                Debug.WriteLine($"[VoiceService] STT Error: {recognitionResult.Exception}");
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "Listening cancelled");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceService] Listen error: {ex}");
            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
            return null;
        }
        finally
        {
            _isListening = false;
            _listeningCts?.Dispose();
            _listeningCts = null;
            ListeningStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stop listening for speech.
    /// </summary>
    public void StopListening()
    {
        _listeningCts?.Cancel();
    }

    /// <summary>
    /// Speak the given text aloud.
    /// </summary>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || _isSpeaking)
            return;

        try
        {
            _isSpeaking = true;
            StatusChanged?.Invoke(this, "Speaking...");

            var options = new SpeechOptions
            {
                Pitch = 1.0f,
                Volume = 1.0f
            };

            await TextToSpeech.Default.SpeakAsync(text, options, cancellationToken);

            StatusChanged?.Invoke(this, "Done speaking");
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "Speech cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceService] Speak error: {ex}");
            StatusChanged?.Invoke(this, $"Speech error: {ex.Message}");
        }
        finally
        {
            _isSpeaking = false;
        }
    }

    /// <summary>
    /// Stop speaking.
    /// </summary>
    public void StopSpeaking()
    {
        // Note: TextToSpeech doesn't have a direct stop method
        // We rely on cancellation token
    }

    /// <summary>
    /// Check if speech recognition is available.
    /// </summary>
    public async Task<bool> IsSpeechRecognitionAvailableAsync()
    {
        try
        {
            var speechToText = SpeechToText.Default;
            return await speechToText.RequestPermissions(CancellationToken.None);
        }
        catch
        {
            return false;
        }
    }
}

