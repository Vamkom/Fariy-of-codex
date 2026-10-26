using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

public class FairyCompanion : Window {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly HttpClient client = new HttpClient(new HttpClientHandler { UseProxy=false });
    readonly Canvas canvas = new Canvas();
    readonly Grid boot = new Grid();
    readonly OpeningPlayer video;
    readonly Image backdrop = new Image();
    readonly Canvas eye = new Canvas { Width=238, Height=238 };
    readonly Border caption = new Border();
    readonly TextBlock captionText = new TextBlock();
    readonly MediaPlayer voice = new MediaPlayer();
    readonly Queue<Dictionary<string,object>> pending = new Queue<Dictionary<string,object>>();
    readonly DispatcherTimer tracking = new DispatcherTimer();
    readonly DispatcherTimer polling = new DispatcherTimer();
    readonly DispatcherTimer settle = new DispatcherTimer();
    IntPtr target, own;
    string phase="waiting", api="http://127.0.0.1:18742", token="", greeting="";
    bool started, busy, speaking, muted, detached, compact, live=true;
    bool showcase=!Environment.GetCommandLineArgs().Contains("--follow");
    static System.Threading.Mutex singleton;
    readonly DispatcherTimer compactTimer=new DispatcherTimer();
    long after;
    DateTime lastError=DateTime.MinValue;

    [STAThread] public static void Main(string[] args) {
        try { bool created;singleton=new System.Threading.Mutex(true,"Local\\FairyCompanion-Codex-"+AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant().GetHashCode().ToString("X"),out created);
            if(!created)return;new Application().Run(new FairyCompanion(args.Contains("--self-test"))); }
        catch(Exception ex) { File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"companion-error.log"),DateTime.Now+" "+ex+Environment.NewLine); }
    }
    public FairyCompanion(bool test) {
        detached=test;
        var cfg=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(System.IO.Path.Combine(root,"config.json")));
        token=(string)cfg["token"]; greeting=(string)cfg["greeting"]; client.DefaultRequestHeaders.Add("X-Fairy-Token",token);
        if(File.Exists(System.IO.Path.Combine(root,"assets/fairy-icon.png")))Icon=BitmapFrame.Create(new Uri(System.IO.Path.Combine(root,"assets/fairy-icon.png")));
        Title="Fairy · Codex Companion"; Width=1200; Height=800; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=true; ShowActivated=true;
        ResizeMode=ResizeMode.NoResize; Content=canvas; client.Timeout=TimeSpan.FromSeconds(240);
        video=new OpeningPlayer(root);
        BuildVisuals();
        if(detached){Opacity=0;video.IsMuted=true;}
        SourceInitialized+=(s,e)=> { own=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(own).AddHook(Hook); };
        Loaded+=(s,e)=> { tracking.Interval=TimeSpan.FromMilliseconds(250); tracking.Tick+=(a,b)=>TrackWindow(); tracking.Start();
            polling.Interval=TimeSpan.FromSeconds(1); polling.Tick+=async(a,b)=>await PollAudio(); polling.Start();
            if(detached) { Left=50;Top=50;LayoutParts();BeginBoot(); SelfTest(); }
            else if(showcase){var work=SystemParameters.WorkArea;Left=work.Left;Top=work.Top;Width=work.Width;Height=work.Height;Topmost=true;Show();UpdateLayout();LayoutParts();BeginBoot();}
            else TrackWindow(); };
        Closed+=(s,e)=> { live=false;tracking.Stop();polling.Stop();settle.Stop();compactTimer.Stop();video.Dispose();voice.Close();client.Dispose(); };
        SizeChanged+=(s,e)=>LayoutParts();
        WriteState("starting");
    }
    void BuildVisuals() {
        string background=System.IO.Path.Combine(root,"skin/background.png");
        backdrop.Source=File.Exists(background)?(ImageSource)new BitmapImage(new Uri(background)):
            new DrawingImage(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(9,29,48)),null,new RectangleGeometry(new Rect(0,0,1600,900))));
        backdrop.Stretch=Stretch.UniformToFill; backdrop.Opacity=0;backdrop.IsHitTestVisible=false;canvas.Children.Add(backdrop);
        boot.Background=Brushes.Black;
        boot.Children.Add(video);canvas.Children.Add(boot);
        var skip=MakeButton("跳过开屏",(s,e)=>Greet()); skip.HorizontalAlignment=HorizontalAlignment.Right;
        skip.VerticalAlignment=VerticalAlignment.Bottom;skip.Margin=new Thickness(20);boot.Children.Add(skip);
        video.MediaOpened+=(s,e)=>Log("opening-ready renderer=frames duration="+video.Duration.TotalSeconds.ToString("F3"));
        video.MediaEnded+=(s,e)=> {Log("video-ended naturally");Greet();};
        video.MediaFailed+=(s,e)=> {Log("video-error "+e.ErrorException.Message);Greet();};
        FairyEmblem.Build(eye,System.IO.Path.Combine(root,"assets/fairy-emblem.json"));
        var label=new TextBlock {Text="F A I R Y",Foreground=Brushes.LightSkyBlue,FontFamily=new FontFamily("Consolas"),FontSize=13,Width=238,TextAlignment=TextAlignment.Center};
        Canvas.SetTop(label,246);eye.Children.Add(label);
        eye.Background=Brushes.Transparent;eye.Visibility=Visibility.Hidden;
        eye.MouseRightButtonUp+=(s,e)=>OpenMenu();eye.MouseLeftButtonUp+=(s,e)=>OpenMenu();
        canvas.Children.Add(eye);
        caption.Background=new SolidColorBrush(Color.FromArgb(240,8,19,33));caption.BorderBrush=new SolidColorBrush(Color.FromRgb(102,184,255));
        caption.BorderThickness=new Thickness(3,0,0,0);caption.Padding=new Thickness(15,10,15,10);caption.Width=400;caption.Visibility=Visibility.Hidden;
        captionText.Foreground=Brushes.White;captionText.FontSize=14;captionText.TextWrapping=TextWrapping.Wrap;captionText.LineHeight=25;
        caption.Child=captionText;caption.IsHitTestVisible=false;canvas.Children.Add(caption);
        voice.MediaEnded+=(s,e)=> {speaking=false;Log("voice-ended");PlayNext();};
        voice.MediaOpened+=(s,e)=>Log("voice-opened "+voice.NaturalDuration);
        voice.MediaFailed+=(s,e)=> {speaking=false;Log("voice-error "+e.ErrorException.Message);};
    }
    Button MakeButton(string text,RoutedEventHandler action) {
        var b=new Button {Content=text,Padding=new Thickness(14,8,14,8),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(17,34,52))};
        b.Click+=action;return b;
    }
    void OpenMenu() {
        var menu=new ContextMenu {Placement=System.Windows.Controls.Primitives.PlacementMode.Right,HorizontalOffset=4,VerticalOffset=0};
        var replay=new MenuItem {Header="重播开屏"};replay.Click+=(s,e)=>BeginBoot();menu.Items.Add(replay);
        var greet=new MenuItem {Header="播放问候"};greet.Click+=(s,e)=>PlayGreeting();menu.Items.Add(greet);
        var mute=new MenuItem {Header=muted?"开启语音":"暂停语音"};mute.Click+=(s,e)=> {muted=!muted;voice.Stop();speaking=false;pending.Clear();WriteState("voice-toggle");};menu.Items.Add(mute);
        var prepare=new MenuItem {Header="连接 Fairy 语音引擎"};prepare.Click+=async(s,e)=>await PrepareVoice();menu.Items.Add(prepare);
        var skin=new MenuItem {Header="切换 HDD 换肤"};skin.Click+=async(s,e)=>await ToggleSkin();menu.Items.Add(skin);
        var close=new MenuItem {Header="关闭 Fairy"};close.Click+=(s,e)=>Close();menu.Items.Add(close);
        menu.PlacementTarget=eye;menu.IsOpen=true;
    }
    async Task ToggleSkin() {
        try {
            var body=await client.GetStringAsync("http://127.0.0.1:18743/status");
            var state=json.Deserialize<Dictionary<string,object>>(body);
            bool active=state.ContainsKey("installed") && Convert.ToBoolean(state["installed"]);
            var response=await client.PostAsync("http://127.0.0.1:18743/"+(active?"restore":"enable"),new StringContent("{}"));
            Log(response.IsSuccessStatusCode?"skin-toggled":"skin-toggle-failed");
        } catch(Exception e){Log("skin-toggle "+e.Message);}
    }
    async Task PrepareVoice() {
        try { var request=new HttpRequestMessage(HttpMethod.Post,api+"/api/prepare");request.Headers.Add("X-Fairy-Token",token);
            request.Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json");
            var response=await client.SendAsync(request);var body=await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode)throw new Exception(body);
            Log("voice-engine-ready"); PlayGreeting();
        } catch(Exception e) {Log("voice-prepare "+e.Message);}
    }
    void TrackWindow() {
        if(detached||showcase)return;
        if(target==IntPtr.Zero) {
            var candidates=Process.GetProcesses().Where(process=>
                (process.ProcessName=="ChatGPT"&&process.MainWindowTitle=="ChatGPT" ||
                 process.ProcessName=="csw"&&process.MainWindowTitle=="Remote AI") && process.MainWindowHandle!=IntPtr.Zero && !IsIconic(process.MainWindowHandle))
                .OrderByDescending(process=>process.MainWindowHandle==GetForegroundWindow()).ThenByDescending(process=>process.ProcessName=="ChatGPT").ToArray();
            if(candidates.Length==0)return;
            target=candidates[0].MainWindowHandle;WriteState("codex-window-found "+candidates[0].ProcessName);
            new WindowInteropHelper(this).Owner=target; Log("attached-codex-window");
        }
        RECT r;var p=new POINT();
        if(!GetClientRect(target,out r)||!ClientToScreen(target,ref p)) {target=IntPtr.Zero;Hide();return;}
        if(IsIconic(target)) {if(IsVisible){Hide();if(phase=="boot")video.Pause();}return;}
        var dpi=GetDpiForWindow(target)/96.0;if(dpi<=0)dpi=1;
        Left=p.X/dpi;Width=Math.Max(100,compact?Math.Min(520,r.Right/dpi):r.Right/dpi);
        Height=compact?140:Math.Max(100,r.Bottom/dpi);Top=p.Y/dpi+(compact?Math.Max(0,r.Bottom/dpi-140):0);
        if(!IsVisible){Show();if(phase=="boot")video.Play();}
        if(!started)BeginBoot();
    }
    void LayoutParts() {
        backdrop.Width=ActualWidth;backdrop.Height=ActualHeight;boot.Width=ActualWidth;boot.Height=ActualHeight;
        if(phase=="dock")PlaceDock();
        else if(phase=="center") {Canvas.SetLeft(eye,(ActualWidth-238)/2);Canvas.SetTop(eye,ActualHeight*.46-119);PlaceCaption(true);}
    }
    void BeginBoot() {
        started=true;compact=false;compactTimer.Stop();settle.Stop();voice.Stop();pending.Clear();speaking=false;phase="boot";
        eye.Visibility=Visibility.Hidden;caption.Visibility=Visibility.Hidden;boot.Visibility=Visibility.Visible;
        backdrop.BeginAnimation(OpacityProperty,null);backdrop.Opacity=0;
        eye.BeginAnimation(Canvas.LeftProperty,null);eye.BeginAnimation(Canvas.TopProperty,null);
        eye.RenderTransform=new ScaleTransform(1,1);if(detached){Width=1200;Height=800;UpdateLayout();}else if(showcase){var work=SystemParameters.WorkArea;Left=work.Left;Top=work.Top;Width=work.Width;Height=work.Height;UpdateLayout();}else{TrackWindow();UpdateLayout();}video.Stop();LayoutParts();if(video.Available)video.Play();WriteState("boot");if(!video.Available)Greet();
    }
    void Greet() {
        if(phase!="boot")return;
        video.Stop();boot.Visibility=Visibility.Hidden;phase="center";
        backdrop.Opacity=1;eye.Visibility=Visibility.Visible;caption.Visibility=Visibility.Visible;captionText.Text=greeting;
        LayoutParts();PlayGreeting();WriteState("center");if(detached)CapturePhases();
        settle.Interval=TimeSpan.FromMilliseconds(1500);settle.Tick-=DockTick;settle.Tick+=DockTick;settle.Start();
    }
    void DockTick(object s,EventArgs e) {settle.Stop();Dock();}
    void Dock() {
        phase="dock";var duration=TimeSpan.FromMilliseconds(2200);
        var left=new DoubleAnimation(Canvas.GetLeft(eye),26,duration);var top=new DoubleAnimation(Canvas.GetTop(eye),Math.Max(10,ActualHeight-118),duration);
        left.EasingFunction=new CubicEase {EasingMode=EasingMode.EaseInOut};top.EasingFunction=left.EasingFunction;
        eye.BeginAnimation(Canvas.LeftProperty,left);eye.BeginAnimation(Canvas.TopProperty,top);
        var scale=eye.RenderTransform as ScaleTransform;scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,86.0/238,duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,86.0/238,duration));
        backdrop.BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(2800)));
        PlaceCaption(false);WriteState("dock");
        compactTimer.Interval=TimeSpan.FromMilliseconds(2900);compactTimer.Tick-=CompactTick;compactTimer.Tick+=CompactTick;compactTimer.Start();
    }
    void CompactTick(object sender,EventArgs e) {
        compactTimer.Stop();if(phase!="dock")return;compact=true;
        if(detached){Width=520;Height=140;}else if(showcase){var work=SystemParameters.WorkArea;Width=520;Height=140;Left=work.Left;Top=work.Bottom-140;}else TrackWindow();
        UpdateLayout();PlaceDock();WriteState("docked-compact");
    }
    void PlaceDock() {
        eye.BeginAnimation(Canvas.LeftProperty,null);eye.BeginAnimation(Canvas.TopProperty,null);
        Canvas.SetLeft(eye,26);Canvas.SetTop(eye,Math.Max(10,ActualHeight-118));PlaceCaption(false);
    }
    void PlaceCaption(bool center) {
        caption.Width=Math.Min(400,Math.Max(170,ActualWidth-(center?40:152)));
        Canvas.SetLeft(caption,center?(ActualWidth-caption.Width)/2:130);
        Canvas.SetTop(caption,center?ActualHeight*.46+160:Math.Max(10,ActualHeight-100));
        if(!center&&ActualWidth<440)caption.Visibility=Visibility.Hidden;
    }
    void PlayGreeting() {
        if(muted||detached)return;
        var path=System.IO.Path.Combine(root,"assets/greeting.wav");
        if(!File.Exists(path)){Log("greeting-text-only; audio-not-generated");return;}
        voice.Stop();voice.Open(new Uri(path));voice.Volume=1;voice.Play();speaking=true;Log("greeting-audio-start");
    }
    async Task PollAudio() {
        if(busy||!live)return;busy=true;
        try {
            var body=await client.GetStringAsync(api+"/api/audio?after="+after);
            var response=json.Deserialize<Dictionary<string,object>>(body);
            var items=response["items"] as System.Collections.IEnumerable;
            if(items!=null)foreach(Dictionary<string,object> item in items) {
                after=Math.Max(after,Convert.ToInt64(item["id"]));if(!muted)pending.Enqueue(item);
            }
            if(!speaking&&phase=="dock")PlayNext();
        } catch(Exception e) {if(DateTime.Now-lastError>TimeSpan.FromMinutes(1)){Log("audio-poll "+e.Message);lastError=DateTime.Now;}}
        finally {busy=false;}
    }
    void PlayNext() {
        if(muted||pending.Count==0||phase!="dock")return;
        var item=pending.Dequeue();var path=System.IO.Path.Combine(root,"assets",(string)item["file"]);
        if(!File.Exists(path))return;
        voice.Open(new Uri(path));voice.Play();speaking=true;Log("reply-audio "+item["id"]);
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
        if(msg!=0x84||phase=="boot")return IntPtr.Zero;
        long bits=lp.ToInt64();var screen=new Point((short)(bits&65535),(short)((bits>>16)&65535));
        var point=PointFromScreen(screen);double x=Canvas.GetLeft(eye),y=Canvas.GetTop(eye);
        double size=phase=="dock"?86:238;
        if(point.X<x||point.X>x+size||point.Y<y||point.Y>y+size+15){handled=true;return new IntPtr(-1);}
        return IntPtr.Zero;
    }
    async void SelfTest() {
        await Task.Delay(2000);SaveVisual("native-video.png",boot);
        await Task.Delay(3000);SaveVisual("native-video-later.png",boot);
        if(Environment.GetCommandLineArgs().Contains("--replay-test")) {
            await Task.Delay(15000);BeginBoot();
            await Task.Delay(2000);SaveVisual("native-replay.png",boot);
            await Task.Delay(3000);SaveVisual("native-replay-later.png",boot);
            await Task.Delay(17000);
        } else await Task.Delay(19000);
        WriteState("self-test-finished");Close();
    }
    async void CapturePhases() {await Task.Delay(350);SaveVisual("native-center.png",canvas);await Task.Delay(4500);SaveVisual("native-dock.png",canvas);WriteState("phase-captures-finished");}
    void SaveVisual(string name,FrameworkElement visual) {
        try {
            var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(System.IO.Path.Combine(root,name)))encoder.Save(file);
        } catch(Exception e) {Log("capture-error "+e.Message);}
    }
    void WriteState(string message) {
        try {File.WriteAllText(System.IO.Path.Combine(root,"companion-state.json"),json.Serialize(new {phase=phase,message=message,attached=target!=IntPtr.Zero,muted=muted,width=Width,height=Height,backgroundOpacity=backdrop.Opacity,visible=IsVisible,left=Left,top=Top,targetMinimized=target!=IntPtr.Zero&&IsIconic(target)}));}
        catch(IOException) { }
        Log(message);
    }
    void Log(string message) {try {File.AppendAllText(System.IO.Path.Combine(root,"companion.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch(IOException) {}}
}
