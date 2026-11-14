using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace QuickReader;

internal enum SpeechState
{
    Idle,
    Speaking,
    Paused,
}

/// <summary>
/// Reads text aloud with the voices built into Windows (works offline).
/// Long text is cut into short pieces: the first piece starts playing almost at once,
/// and the next piece is prepared while the current one is playing.
/// All public members are used from the UI thread.
/// </summary>
internal sealed class SpeechEngine : IDisposable
{
    private readonly SemaphoreSlim _synthLock = new(1, 1);

    // Created on first use, so the app uses very little memory until you read something.
    private SpeechSynthesizer? _synth;
    private MediaPlayer? _player;

    private string? _wantedVoiceId;
    private VoiceInformation? _voice;
    private bool _voiceChosen;
    private CancellationTokenSource? _session;
    private volatile TaskCompletionSource<bool>? _playback;
    private MediaSource? _source;
    private Piece? _piece;
    private bool _pauseRequested;

    private SpeechSynthesizer Synth
    {
        get
        {
            if (_synth is null)
            {
                _synth = new SpeechSynthesizer();
                try { _synth.Options.AppendedSilence = SpeechAppendedSilence.Min; } // smoother joins between pieces
                catch { /* older Windows 10 */ }
            }
            return _synth;
        }
    }

    private MediaPlayer Player
    {
        get
        {
            if (_player is null)
            {
                _player = new MediaPlayer
                {
                    AutoPlay = false,
                    AudioCategory = MediaPlayerAudioCategory.Speech,
                };
                try { _player.CommandManager.IsEnabled = false; } // don't show up in the Windows media controls
                catch { /* not available on this Windows version */ }
                _player.MediaEnded += (_, _) => _playback?.TrySetResult(true);
                _player.MediaFailed += (_, e) => _playback?.TrySetException(new InvalidOperationException(e.ErrorMessage));
            }
            return _player;
        }
    }

    public event EventHandler? StateChanged;

    /// <summary>Raised with a message the user should see.</summary>
    public event Action<string>? Error;

    public SpeechState State { get; private set; }

    /// <summary>1.0 is normal speed. Changes apply from the next sentence.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>The voice that will be used (looked up the first time it's needed).</summary>
    public string? VoiceId => CurrentVoice?.Id;

    public static IReadOnlyList<VoiceInformation> AllVoices => SpeechSynthesizer.AllVoices;

    /// <summary>Chooses a voice. Falls back to an English voice if that one isn't installed any more.</summary>
    public void SetVoice(string? voiceId)
    {
        _wantedVoiceId = voiceId;
        _voiceChosen = false;
    }

    private VoiceInformation? CurrentVoice
    {
        get
        {
            if (_voiceChosen)
                return _voice;
            _voiceChosen = true;
            try
            {
                IReadOnlyList<VoiceInformation> all = SpeechSynthesizer.AllVoices;
                VoiceInformation? windowsDefault = SpeechSynthesizer.DefaultVoice;
                _voice = all.FirstOrDefault(v => v.Id == _wantedVoiceId)
                         ?? (windowsDefault is not null && IsEnglish(windowsDefault) ? windowsDefault : null)
                         ?? all.FirstOrDefault(IsEnglish)
                         ?? windowsDefault;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return _voice;
        }
    }

    /// <summary>Starts reading. Anything already being read is stopped first.</summary>
    public void Speak(string text) => _ = SpeakAsync(text);

    public void TogglePause()
    {
        switch (State)
        {
            case SpeechState.Speaking:
                _pauseRequested = true;
                try { _player?.Pause(); } catch (Exception ex) { Log.Error(ex); }
                SetState(SpeechState.Paused);
                break;

            case SpeechState.Paused:
                _pauseRequested = false;
                try
                {
                    if (_player?.Source is not null)
                        _player.Play();
                }
                catch (Exception ex) { Log.Error(ex); }
                SetState(SpeechState.Speaking);
                break;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? session = _session;
        _session = null;
        session?.Cancel();
        ResetPlayer();
        SetState(SpeechState.Idle);
    }

    public void Dispose()
    {
        Stop();
        _player?.Dispose();
        _synth?.Dispose();
    }

    private async Task SpeakAsync(string text)
    {
        Stop();
        List<string> pieces = TextChunker.Split(text);
        if (pieces.Count == 0)
            return;

        var session = new CancellationTokenSource();
        _session = session;
        CancellationToken token = session.Token;
        SetState(SpeechState.Speaking);

        Task<Piece>? next = SynthesizeAsync(pieces[0], token);
        try
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = await next!;
                next = null;

                // Speed or voice changed while this piece was being prepared: prepare it again.
                if (piece.Speed != Speed || piece.VoiceId != VoiceId)
                {
                    piece.Dispose();
                    piece = await SynthesizeAsync(pieces[i], token);
                }

                if (i + 1 < pieces.Count)
                    next = SynthesizeAsync(pieces[i + 1], token);

                await PlayAsync(piece, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped, or new text started.
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            if (_session == session)
                Error?.Invoke("Couldn't read the text aloud: " + ex.Message);
        }
        finally
        {
            if (next is not null)
                DisposeWhenFinished(next);

            if (_session == session)
            {
                _session = null;
                ResetPlayer();
                SetState(SpeechState.Idle);
            }
            session.Dispose();
        }
    }

    private async Task<Piece> SynthesizeAsync(string text, CancellationToken token)
    {
        await _synthLock.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            VoiceInformation? voice = CurrentVoice;
            double speed = Speed;
            SpeechSynthesizer synth = Synth;
            if (voice is not null && synth.Voice?.Id != voice.Id)
                synth.Voice = voice;
            synth.Options.SpeakingRate = speed;

            SpeechSynthesisStream stream = await synth.SynthesizeTextToStreamAsync(text);
            return new Piece(stream, speed, voice?.Id);
        }
        finally
        {
            _synthLock.Release();
        }
    }

    private async Task PlayAsync(Piece piece, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            piece.Dispose();
            token.ThrowIfCancellationRequested();
        }

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaSource source = MediaSource.CreateFromStream(piece.Stream, piece.Stream.ContentType);

        MediaSource? oldSource = _source;
        Piece? oldPiece = _piece;
        _source = source;
        _piece = piece;
        _playback = finished;
        Player.Source = source;
        oldSource?.Dispose();
        oldPiece?.Dispose();

        if (!_pauseRequested)
            Player.Play();

        using (token.Register(() => finished.TrySetCanceled()))
            await finished.Task;
    }

    private void ResetPlayer()
    {
        _pauseRequested = false;
        _playback = null;
        try
        {
            if (_player is not null)
            {
                _player.Pause();
                _player.Source = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        _source?.Dispose();
        _source = null;
        _piece?.Dispose();
        _piece = null;
    }

    private void SetState(SpeechState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsEnglish(VoiceInformation voice) =>
        voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    private static void DisposeWhenFinished(Task<Piece> task) =>
        task.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion)
                t.Result.Dispose();
            else
                _ = t.Exception; // mark as seen
        }, TaskScheduler.Default);

    /// <summary>One prepared piece of audio, and the settings it was made with.</summary>
    private sealed class Piece : IDisposable
    {
        public Piece(SpeechSynthesisStream stream, double speed, string? voiceId)
        {
            Stream = stream;
            Speed = speed;
            VoiceId = voiceId;
        }

        public SpeechSynthesisStream Stream { get; }
        public double Speed { get; }
        public string? VoiceId { get; }

        public void Dispose() => Stream.Dispose();
    }
}
