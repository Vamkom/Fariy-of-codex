using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// The opening uses ordinary WPF images, avoiding the machine's video overlay path.
// Only the current JPEG is decoded; the complete sequence is never kept in RAM.
public sealed class OpeningPlayer : Image, IDisposable {
    readonly MediaPlayer audio = new MediaPlayer();
    readonly Stopwatch clock = new Stopwatch();
    readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
    readonly string directory, audioPath;
    readonly int count;
    bool ready, requested, playing, disposed;
    int frame = -1;
    public event EventHandler MediaEnded;
    public event EventHandler MediaOpened;
    public event EventHandler<OpeningError> MediaFailed;
    public bool Available { get { return count > 0 && File.Exists(audioPath); } }
    public TimeSpan Duration { get { return TimeSpan.FromSeconds(count / 30.0); } }
    public bool IsMuted { get { return audio.IsMuted; } set { audio.IsMuted = value; } }

    public OpeningPlayer(string root) {
        directory = Path.Combine(root, "assets/opening-frames");
        audioPath = Path.Combine(root, "assets/opening-audio.wav");
        count = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.jpg").Length : 0;
        if (!Available) return;
        Stretch = Stretch.Uniform;
        timer.Interval = TimeSpan.FromMilliseconds(10);
        timer.Tick += Tick;
        audio.MediaOpened += (s,e) => {
            ready = true;
            if (MediaOpened != null) MediaOpened(this, EventArgs.Empty);
            if (requested) StartPlayback();
        };
        audio.MediaEnded += (s,e) => Finish();
        audio.MediaFailed += (s,e) => {
            Stop();
            if (MediaFailed != null) MediaFailed(this, new OpeningError(e.ErrorException));
        };
        RenderFrame(0);
        audio.Open(new Uri(audioPath));
    }
    public void Play() {
        if (disposed) return;
        requested = true;
        if (ready) StartPlayback();
    }
    void StartPlayback() {
        if (playing) return;
        playing = true;
        audio.Position = clock.Elapsed;
        audio.Play();clock.Start();timer.Start();
    }
    public void Pause() {
        requested = false;playing = false;
        timer.Stop();clock.Stop();audio.Pause();
    }
    public void Stop() {
        requested = false;playing = false;
        timer.Stop();clock.Reset();audio.Stop();
        if (ready) audio.Position = TimeSpan.Zero;
        if (!disposed && Available) RenderFrame(0);
    }
    void Tick(object sender, EventArgs e) {
        if (!playing) return;
        if (clock.Elapsed >= Duration) {Finish();return;}
        try {RenderFrame(Math.Min(count - 1, (int)(clock.Elapsed.TotalSeconds * 30)));}
        catch (Exception error) {
            Stop();if (MediaFailed != null) MediaFailed(this, new OpeningError(error));
        }
    }
    void RenderFrame(int index) {
        if (frame == index) return;
        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(Path.Combine(directory, (index + 1).ToString("D5") + ".jpg"))) {
            bitmap.BeginInit();bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;bitmap.EndInit();bitmap.Freeze();
        }
        Source = bitmap;frame = index;
    }
    void Finish() {
        if (!playing) return;
        timer.Stop();clock.Stop();audio.Stop();playing = requested = false;
        if (MediaEnded != null) MediaEnded(this, EventArgs.Empty);
    }
    public void Dispose() {disposed = true;timer.Stop();audio.Close();Source = null;}
}
public sealed class OpeningError : EventArgs {
    public Exception ErrorException {get;private set;}
    public OpeningError(Exception error) {ErrorException = error;}
}
