using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrystalDragon {
    // Startup plays an offline-rendered film. No DataScene or particle drawing is
    // instantiated by the live launcher; the posters cover decoder start/end.
    internal sealed class FilmSurface : Grid {
        readonly MediaElement player;
        readonly Image poster;
        readonly BitmapSource endFrame;
        readonly Stopwatch elapsed = new Stopwatch();
        internal bool Opened { get; private set; }
        internal bool Completed { get; private set; }
        internal bool Failed { get; private set; }
        internal string FailureClass { get; private set; }
        internal double PositionSeconds { get { return Completed ? 5.4 : player.Position.TotalSeconds; } }
        internal int Phase { get { double time=PositionSeconds*DataScene.PlaybackRate;return time<1.6?1:time<3?2:time<4.5?3:4; } }
        static BitmapSource LoadImage(string path) {
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
            image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();return image;
        }
        internal FilmSurface(string assets) {
            var firstFrame=LoadImage(Path.Combine(assets,"startup-poster.png"));
            ClipToBounds=true;Background=new SolidColorBrush(Color.FromRgb(16,14,25));
            RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
            endFrame=LoadImage(Path.Combine(assets,"startup-end.png"));
            player=new MediaElement {LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,
                Stretch=Stretch.Uniform,IsMuted=true,Volume=0,IsHitTestVisible=false};
            poster=new Image {Source=firstFrame,Stretch=Stretch.Uniform,IsHitTestVisible=false};
            Children.Add(player);Children.Add(poster);
            player.MediaOpened+=delegate {Opened=true;};
            player.MediaEnded+=delegate {Finish();};
            player.MediaFailed+=delegate(object sender,ExceptionRoutedEventArgs e) {
                Failed=true;FailureClass=e.ErrorException.GetType().Name;
                poster.Visibility=Visibility.Visible;
            };
            player.Source=new Uri(Path.GetFullPath(Path.Combine(assets,"startup.mp4")));
        }
        internal void Start() {elapsed.Start();player.Play();}
        internal void Tick() {
            if(Completed)return;
            if(Failed) {if(elapsed.Elapsed.TotalSeconds>=5.4)Finish();return;}
            if(Opened && player.HasVideo && !player.IsBuffering && player.Position.TotalSeconds>=.04)
                poster.Visibility=Visibility.Collapsed;
        }
        void Finish() {
            if(Completed)return;
            Completed=true;poster.Source=endFrame;poster.Visibility=Visibility.Visible;
            player.Pause();
        }
        internal void Pause() {player.Pause();}
        internal void Close() {player.Close();}
    }
}
