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
using System.Windows.Input;
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
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint size,time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
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
    readonly DispatcherTimer captionTimer=new DispatcherTimer();
    DateTime captionIdleDeadline=DateTime.MaxValue;
    DateTime captionActivityDeadline=DateTime.MaxValue;
    DateTime lastUserActivity=DateTime.MinValue;
    uint lastInputStamp;
    int captionRevision;
    bool captionFading;
    readonly TextBlock voiceStatus = new TextBlock {FontSize=11,Foreground=Brushes.LightSkyBlue,Margin=new Thickness(0,4,0,0)};
    WindowPreferences preferences;
    Point dragStart;
    Point dragWindowStart;
    bool dragging,dragged,preparing,audioInitialized;
    string audioStream="",connectionStatus="语音连接中…";
    DateTime nextStatus=DateTime.MinValue;
    DateTime nextReconnect=DateTime.MinValue;
    int pollFailures;
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
        preferences=WindowPreferences.Load(root);
        var cfg=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(System.IO.Path.Combine(root,"config.json")));
        token=(string)cfg["token"]; greeting=(string)cfg["greeting"]; client.DefaultRequestHeaders.Add("X-Fairy-Token",token);
        if(File.Exists(System.IO.Path.Combine(root,"assets/fairy-icon.png")))Icon=BitmapFrame.Create(new Uri(System.IO.Path.Combine(root,"assets/fairy-icon.png")));
        Title="Fairy · Codex Companion"; Width=1200; Height=800; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=true; ShowActivated=true;
        ResizeMode=ResizeMode.NoResize; Content=canvas; client.Timeout=TimeSpan.FromSeconds(240);
        video=new OpeningPlayer(root);
        BuildVisuals();
        RememberInputStamp();
        captionTimer.Interval=TimeSpan.FromMilliseconds(200);
        captionTimer.Tick+=(s,e)=>CaptionTick();captionTimer.Start();
        if(detached){Opacity=0;video.IsMuted=true;}
        SourceInitialized+=(s,e)=> { own=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(own).AddHook(Hook);ApplyTopmost(); };
        Loaded+=(s,e)=> { tracking.Interval=TimeSpan.FromMilliseconds(250); tracking.Tick+=(a,b)=>TrackWindow(); tracking.Start();
            polling.Interval=TimeSpan.FromSeconds(1); polling.Tick+=async(a,b)=>await PollAudio(); polling.Start();
            if(detached) { Left=50;Top=50;LayoutParts();BeginBoot();if(Environment.GetCommandLineArgs().Contains("--caption-test"))CaptionSelfTest();else SelfTest(); }
            else if(showcase){var work=SystemParameters.WorkArea;Left=work.Left;Top=work.Top;Width=work.Width;Height=work.Height;ApplyTopmost();Show();UpdateLayout();LayoutParts();BeginBoot();}
            else TrackWindow(); };
        Closed+=(s,e)=> { live=false;tracking.Stop();polling.Stop();settle.Stop();compactTimer.Stop();captionTimer.Stop();video.Dispose();voice.Close();client.Dispose(); };
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
        eye.ToolTip="按住左键拖动 · 右键打开设置";
        eye.MouseRightButtonUp+=(s,e)=>OpenMenu();
        eye.MouseLeftButtonDown+=(s,e)=> {if(phase=="dock"&&compact){dragStart=PointToScreen(e.GetPosition(this));dragWindowStart=new Point(Left,Top);dragging=true;dragged=false;eye.CaptureMouse();e.Handled=true;}};
        eye.MouseMove+=(s,e)=> {
            if(!dragging||e.LeftButton!=MouseButtonState.Pressed)return;
            Point current=PointToScreen(e.GetPosition(this));
            if((current-dragStart).Length<5)return;
            dragged=true;
            ScheduleCaptionActivity();
            if(!showcase){showcase=true;target=IntPtr.Zero;new WindowInteropHelper(this).Owner=IntPtr.Zero;}
            var source=PresentationSource.FromVisual(this);
            Vector delta=current-dragStart;
            if(source!=null)delta=source.CompositionTarget.TransformFromDevice.Transform(delta);
            Left=dragWindowStart.X+delta.X;Top=dragWindowStart.Y+delta.Y;
            e.Handled=true;
        };
        eye.MouseLeftButtonUp+=(s,e)=> {dragging=false;eye.ReleaseMouseCapture();if(dragged){ClampPosition();SavePosition();ApplyTopmost();WriteState("window-dragged");}else OpenMenu();dragged=false;e.Handled=true;};
        canvas.Children.Add(eye);
        caption.Background=new SolidColorBrush(Color.FromArgb(240,8,19,33));caption.BorderBrush=new SolidColorBrush(Color.FromRgb(102,184,255));
        caption.BorderThickness=new Thickness(3,0,0,0);caption.Padding=new Thickness(15,10,15,10);caption.Width=400;caption.Visibility=Visibility.Hidden;
        captionText.Foreground=Brushes.White;captionText.FontSize=14;captionText.TextWrapping=TextWrapping.Wrap;captionText.LineHeight=25;
        var speechPanel=new StackPanel();captionText.Margin=new Thickness(0,0,22,0);speechPanel.Children.Add(captionText);speechPanel.Children.Add(voiceStatus);
        var captionContent=new Grid();captionContent.Children.Add(speechPanel);
        var hideText=new Button {Content="×",ToolTip="隐藏文字气泡",Width=22,Height=22,Padding=new Thickness(0),
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Background=Brushes.Transparent,
            Foreground=Brushes.LightSkyBlue,BorderThickness=new Thickness(0)};
        hideText.Click+=(s,e)=>HideCaption("manual",true);captionContent.Children.Add(hideText);
        caption.Child=captionContent;canvas.Children.Add(caption);
        voice.MediaEnded+=(s,e)=> {speaking=false;voice.Close();Log("voice-ended");PlayNext();if(!speaking)ArmCaptionIdle();};
        voice.MediaOpened+=(s,e)=>Log("voice-opened "+voice.NaturalDuration);
        voice.MediaFailed+=(s,e)=> {speaking=false;voice.Close();voiceStatus.Text="播放失败；正在处理下一段";ArmCaptionIdle();Log("voice-error "+e.ErrorException.Message);};
    }
    Button MakeButton(string text,RoutedEventHandler action) {
        var b=new Button {Content=text,Padding=new Thickness(14,8,14,8),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(17,34,52))};
        b.Click+=action;return b;
    }
    void OpenMenu() {
        ScheduleCaptionActivity();
        var menu=new ContextMenu {Placement=System.Windows.Controls.Primitives.PlacementMode.Right,HorizontalOffset=4,VerticalOffset=0};
        var keepText=new MenuItem {Header="始终显示气泡",IsCheckable=true,IsChecked=preferences.alwaysShowCaption};
        keepText.Click+=(s,e)=>{preferences.alwaysShowCaption=keepText.IsChecked;preferences.Save(root);ShowCaption(true);WriteState("caption-always-show-toggle");};menu.Items.Add(keepText);
        var showText=new MenuItem {Header=preferences.alwaysShowCaption?"显示文字气泡":"显示文字气泡（10 秒）"};showText.Click+=(s,e)=>ShowCaption(true);menu.Items.Add(showText);
        var hideText=new MenuItem {Header="隐藏文字气泡"};hideText.Click+=(s,e)=>HideCaption("manual",true);menu.Items.Add(hideText);
        var pin=new MenuItem {Header="始终置顶",IsCheckable=true,IsChecked=preferences.alwaysOnTop};pin.Click+=(s,e)=>{preferences.alwaysOnTop=pin.IsChecked;preferences.Save(root);ApplyTopmost();WriteState("topmost-toggle");};menu.Items.Add(pin);
        var reset=new MenuItem {Header="回到左下角"};reset.Click+=(s,e)=>{preferences.positioned=false;preferences.Save(root);if(compact){var work=SystemParameters.WorkArea;Left=work.Left;Top=work.Bottom-Height;ApplyTopmost();WriteState("position-reset");}};menu.Items.Add(reset);
        var state=new MenuItem {Header=muted?"语音已暂停":connectionStatus,IsEnabled=false};menu.Items.Add(state);
        menu.Items.Add(new Separator());
        var replay=new MenuItem {Header="重播开屏"};replay.Click+=(s,e)=>BeginBoot();menu.Items.Add(replay);
        var greet=new MenuItem {Header="播放问候"};greet.Click+=(s,e)=>PlayGreeting();menu.Items.Add(greet);
        var mute=new MenuItem {Header=muted?"开启语音":"暂停语音"};mute.Click+=(s,e)=> {muted=!muted;voice.Close();speaking=false;pending.Clear();voiceStatus.Text=muted?"语音已暂停":connectionStatus;ArmCaptionIdle();WriteState("voice-toggle");};menu.Items.Add(mute);
        var prepare=new MenuItem {Header="连接 Fairy 语音引擎"};prepare.Click+=async(s,e)=>await PrepareVoice();menu.Items.Add(prepare);
        var skin=new MenuItem {Header="切换 HDD 换肤"};skin.Click+=async(s,e)=>await ToggleSkin();menu.Items.Add(skin);
        var close=new MenuItem {Header="关闭 Fairy"};close.Click+=(s,e)=>Close();menu.Items.Add(close);
        menu.PlacementTarget=eye;menu.IsOpen=true;
    }
    void RememberInputStamp() {
        var info=new LASTINPUTINFO{size=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO))};
        if(GetLastInputInfo(ref info))lastInputStamp=info.time;
    }
    void ShowCaption(bool manual=false) {
        if(phase=="boot")return;
        // While the user is actively operating, do not let the next audio chunk
        // immediately bring the bubble back. Audio itself continues normally.
        if(!manual&&!preferences.alwaysShowCaption&&DateTime.UtcNow-lastUserActivity<TimeSpan.FromSeconds(2))return;
        captionRevision++;captionFading=false;
        caption.BeginAnimation(OpacityProperty,null);caption.Opacity=1;
        caption.Visibility=Visibility.Visible;PlaceCaption(phase=="center");
        captionIdleDeadline=DateTime.UtcNow.AddSeconds(10);captionActivityDeadline=DateTime.MaxValue;RememberInputStamp();
        WriteState(manual?"caption-shown-manually":"caption-shown");
    }
    void ArmCaptionIdle() {captionIdleDeadline=DateTime.UtcNow.AddSeconds(10);}
    void ScheduleCaptionActivity() {
        lastUserActivity=DateTime.UtcNow;
        // Start one grace period on the first input. Continuing to type or move
        // the pointer must not keep pushing the deadline into the future.
        if(!preferences.alwaysShowCaption&&captionActivityDeadline==DateTime.MaxValue&&caption.Visibility==Visibility.Visible)
            captionActivityDeadline=DateTime.UtcNow.AddSeconds(2);
    }
    void HideCaption(string reason,bool quick=false) {
        if(caption.Visibility!=Visibility.Visible||captionFading)return;
        captionFading=true;int revision=++captionRevision;
        var fade=new DoubleAnimation(caption.Opacity,0,TimeSpan.FromMilliseconds(quick?200:650));
        fade.Completed+=(s,e)=> {
            if(revision!=captionRevision)return;
            caption.Visibility=Visibility.Hidden;captionFading=false;
            caption.BeginAnimation(OpacityProperty,null);caption.Opacity=0;
            WriteState("caption-hidden "+reason);
        };
        caption.BeginAnimation(OpacityProperty,fade);
    }
    void CaptionTick() {
        if(phase!="dock")return;
        if(!detached) {
            var info=new LASTINPUTINFO{size=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO))};
            if(GetLastInputInfo(ref info)&&info.time!=lastInputStamp) {
                lastInputStamp=info.time;ScheduleCaptionActivity();
            }
        }
        if(preferences.alwaysShowCaption)return;
        if(DateTime.UtcNow>=captionActivityDeadline)HideCaption("user-activity-2s");
        else if(!speaking&&DateTime.UtcNow>=captionIdleDeadline&&captionActivityDeadline==DateTime.MaxValue)HideCaption("idle-10s");
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
    async Task PrepareVoice(bool greet=true) {
        if(preparing)return;preparing=true;voiceStatus.Text="正在连接语音服务…";
        try {
            voice.Close();speaking=false;
            string python=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"..",".venv","Scripts","pythonw.exe"));
            if(!File.Exists(python))python=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,"..","runtime","venv","Scripts","pythonw.exe"));
            if(File.Exists(python)) {
                var process=Process.Start(new ProcessStartInfo{FileName=python,Arguments="-X utf8 \""+System.IO.Path.Combine(root,"launch.py")+"\" --services-only",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true});
                await Task.Run(()=>process.WaitForExit(240000));
            }
            var request=new HttpRequestMessage(HttpMethod.Post,api+"/api/prepare");
            request.Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json");
            var response=await client.SendAsync(request);var body=await response.Content.ReadAsStringAsync();
            if(response.StatusCode==System.Net.HttpStatusCode.Conflict){voiceStatus.Text="正在合成，请稍候；无需重复连接";return;}
            if(!response.IsSuccessStatusCode)throw new Exception(body);
            voiceStatus.Text="语音已连接";Log("voice-engine-ready");if(greet)PlayGreeting();
        } catch(Exception e) {voiceStatus.Text="语音未连接，请检查本地引擎";Log("voice-prepare "+e.Message);}
        finally {preparing=false;}
    }
    void ApplyTopmost() {
        Topmost=preferences.alwaysOnTop;
        if(own!=IntPtr.Zero)SetWindowPos(own,new IntPtr(preferences.alwaysOnTop?-1:-2),0,0,0,0,0x0013);
    }
    void ClampPosition() {
        double minX=SystemParameters.VirtualScreenLeft,minY=SystemParameters.VirtualScreenTop;
        Left=Math.Max(minX-20,Math.Min(Left,minX+SystemParameters.VirtualScreenWidth-120));
        Top=Math.Max(minY,Math.Min(Top,minY+SystemParameters.VirtualScreenHeight-Height));
    }
    void SavePosition() {
        if(detached)return;
        preferences.positioned=true;preferences.left=Left;preferences.top=Top;preferences.Save(root);
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
        eye.Visibility=Visibility.Hidden;captionRevision++;captionFading=false;captionActivityDeadline=DateTime.MaxValue;caption.BeginAnimation(OpacityProperty,null);caption.Visibility=Visibility.Hidden;boot.Visibility=Visibility.Visible;
        backdrop.BeginAnimation(OpacityProperty,null);backdrop.Opacity=0;
        eye.BeginAnimation(Canvas.LeftProperty,null);eye.BeginAnimation(Canvas.TopProperty,null);
        eye.RenderTransform=new ScaleTransform(1,1);if(detached){Width=1200;Height=800;UpdateLayout();}else if(showcase){var work=SystemParameters.WorkArea;Left=work.Left;Top=work.Top;Width=work.Width;Height=work.Height;UpdateLayout();}else{TrackWindow();UpdateLayout();}video.Stop();LayoutParts();if(video.Available)video.Play();WriteState("boot");if(!video.Available)Greet();
    }
    void Greet() {
        if(phase!="boot")return;
        video.Stop();boot.Visibility=Visibility.Hidden;phase="center";
        backdrop.Opacity=1;eye.Visibility=Visibility.Visible;captionText.Text=greeting;ShowCaption(true);
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
        if(showcase&&!detached&&preferences.positioned){Left=preferences.left;Top=preferences.top;ClampPosition();}
        ApplyTopmost();
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
        captionText.Text=greeting;ShowCaption();
        if(muted||detached){ArmCaptionIdle();return;}
        var path=System.IO.Path.Combine(root,"assets/greeting.wav");
        try {
            var metadata=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(System.IO.Path.Combine(root,"assets/greeting.json")));
            if(metadata.ContainsKey("file")){string file=Convert.ToString(metadata["file"]);if(System.IO.Path.GetFileName(file)==file&&file.EndsWith(".wav"))path=System.IO.Path.Combine(root,"assets",file);}
        } catch(Exception){}
        if(!File.Exists(path)){ArmCaptionIdle();Log("greeting-text-only; audio-not-generated");return;}
        voice.Close();voice.Open(new Uri(path));voice.Volume=1;voice.Play();speaking=true;Log("greeting-audio-start");
    }
    async Task PollAudio() {
        if(busy||!live)return;busy=true;
        try {
            var body=await client.GetStringAsync(api+"/api/audio?after="+after);
            var response=json.Deserialize<Dictionary<string,object>>(body);
            pollFailures=0;
            string stream=response.ContainsKey("streamId")?Convert.ToString(response["streamId"]):"legacy";
            if(!audioInitialized){audioInitialized=true;audioStream=stream;after=response.ContainsKey("latest")?Convert.ToInt64(response["latest"]):0;}
            else if(audioStream!=stream){audioStream=stream;after=0;pending.Clear();Log("audio-stream-reconnected");return;}
            var items=response["items"] as System.Collections.IEnumerable;
            if(items!=null)foreach(Dictionary<string,object> item in items) {
                long id=Convert.ToInt64(item["id"]);if(id<=after)continue;
                after=id;if(!muted)pending.Enqueue(item);
            }
            if(DateTime.Now>=nextStatus){nextStatus=DateTime.Now.AddSeconds(5);await RefreshVoiceStatus();}
            if(!speaking&&phase=="dock")PlayNext();
        } catch(Exception e) {
            connectionStatus="语音服务未连接，正在重连…";voiceStatus.Text=connectionStatus;pollFailures++;
            if(pollFailures>=3&&!preparing&&!detached&&DateTime.Now>=nextReconnect){nextReconnect=DateTime.Now.AddMinutes(1);var recovery=PrepareVoice(false);}
            if(DateTime.Now-lastError>TimeSpan.FromMinutes(1)){Log("audio-poll "+e.Message);lastError=DateTime.Now;}
        }
        finally {busy=false;}
    }
    async Task RefreshVoiceStatus() {
        var state=json.Deserialize<Dictionary<string,object>>(await client.GetStringAsync(api+"/api/status"));
        bool ready=Convert.ToBoolean(state["ready"]);
        var reader=state.ContainsKey("reader")?state["reader"] as Dictionary<string,object>:null;
        connectionStatus=!ready?"语音引擎未就绪，正在重连":reader==null?"语音已连接 · 未绑定朗读聊天":
            !Convert.ToBoolean(reader["fileExists"])?"绑定的聊天文件不存在":
            reader["error"]!=null?"朗读发生错误，请重连语音":
            Convert.ToBoolean(reader["synthesizing"])?"正在合成语音，请稍候…":
            Convert.ToInt32(reader["queued"])>0?"回复等待合成中…":"朗读就绪 · 仅当前绑定聊天";
        if(!preparing)voiceStatus.Text=muted?"语音已暂停":connectionStatus;
    }
    void PlayNext() {
        if(muted||pending.Count==0||phase!="dock")return;
        var item=pending.Dequeue();var path=System.IO.Path.Combine(root,"assets",(string)item["file"]);
        if(!File.Exists(path))return;
        ShowCaption();
        voice.Close();voice.Open(new Uri(path));voice.Play();speaking=true;Log("reply-audio "+item["id"]);
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
        if(msg!=0x84||phase=="boot")return IntPtr.Zero;
        long bits=lp.ToInt64();var screen=new Point((short)(bits&65535),(short)((bits>>16)&65535));
        var point=PointFromScreen(screen);double x=Canvas.GetLeft(eye),y=Canvas.GetTop(eye);
        double size=phase=="dock"?86:238;
        if(caption.Visibility==Visibility.Visible&&caption.Opacity>.05) {
            double closeX=Canvas.GetLeft(caption)+caption.ActualWidth-caption.Padding.Right-22;
            double closeY=Canvas.GetTop(caption)+caption.Padding.Top;
            if(point.X>=closeX&&point.X<=closeX+22&&point.Y>=closeY&&point.Y<=closeY+22)return IntPtr.Zero;
        }
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
    async void CaptionSelfTest() {
        bool originalAlwaysShow=preferences.alwaysShowCaption;
        try {
            preferences.alwaysShowCaption=false;
            Greet();await Task.Delay(5000);
            speaking=true;ShowCaption(true);
            await Task.Delay(10800);
            if(caption.Visibility!=Visibility.Visible)throw new Exception("Caption vanished during speech");
            speaking=false;ArmCaptionIdle();
            await Task.Delay(9000);
            if(caption.Visibility!=Visibility.Visible)throw new Exception("Caption hid before 10 seconds");
            await Task.Delay(2100);
            if(caption.Visibility!=Visibility.Hidden)throw new Exception("Idle caption did not fade");
            ShowCaption(true);HideCaption("manual-test",true);await Task.Delay(350);
            if(caption.Visibility!=Visibility.Hidden)throw new Exception("Manual hide failed");
            ShowCaption(true);ScheduleCaptionActivity();await Task.Delay(1400);ScheduleCaptionActivity();
            if(caption.Visibility!=Visibility.Visible||caption.Opacity<.99)throw new Exception("Activity hid before 2 second grace");
            await Task.Delay(1500);
            if(caption.Visibility!=Visibility.Hidden)throw new Exception("Continuous activity delayed hiding past first input deadline");
            preferences.alwaysShowCaption=true;ShowCaption(true);captionIdleDeadline=DateTime.UtcNow.AddSeconds(-20);ScheduleCaptionActivity();CaptionTick();await Task.Delay(1100);
            if(caption.Visibility!=Visibility.Visible||caption.Opacity<.99)throw new Exception("Pinned caption automatically hidden");
            HideCaption("manual-pinned-test",true);await Task.Delay(350);
            if(caption.Visibility!=Visibility.Hidden)throw new Exception("Manual hide failed in always-show mode");
            ShowCaption(true);HideCaption("pin-during-fade");await Task.Delay(100);ShowCaption(true);await Task.Delay(750);
            if(caption.Visibility!=Visibility.Visible||caption.Opacity<.99)throw new Exception("Pin did not cancel fade");
            preferences.alwaysShowCaption=false;
            ShowCaption(true);HideCaption("stale-fade-test");await Task.Delay(100);ShowCaption(true);await Task.Delay(750);
            if(caption.Visibility!=Visibility.Visible||caption.Opacity<.99)throw new Exception("Old fade hid new caption");
            File.WriteAllText(System.IO.Path.Combine(root,"qa-caption.json"),json.Serialize(new {passed=true,idleDelaySeconds=10,activityGraceSeconds=2,fadeMilliseconds=650,speechKeepsVisible=true,manualHide=true,activityHide=true,continuousActivityDoesNotPostpone=true,alwaysShowIgnoresActivityAndIdle=true,manualHideWhilePinned=true,staleFadeCancelled=true}));
            WriteState("caption-self-test-passed");
        }catch(Exception error){File.WriteAllText(System.IO.Path.Combine(root,"qa-caption.json"),json.Serialize(new{passed=false,error=error.Message}));}
        preferences.alwaysShowCaption=originalAlwaysShow;Close();
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
        try {File.WriteAllText(System.IO.Path.Combine(root,"companion-state.json"),json.Serialize(new {phase=phase,message=message,attached=target!=IntPtr.Zero,muted=muted,speaking=speaking,captionAlwaysShow=preferences.alwaysShowCaption,captionVisible=caption.Visibility==Visibility.Visible,captionOpacity=caption.Opacity,width=Width,height=Height,backgroundOpacity=backdrop.Opacity,visible=IsVisible,left=Left,top=Top,topmost=Topmost,nativeTopmost=own!=IntPtr.Zero&&(GetWindowLong(own,-20)&8)!=0,voiceStatus=connectionStatus,targetMinimized=target!=IntPtr.Zero&&IsIconic(target)}));}
        catch(IOException) { }
        Log(message);
    }
    void Log(string message) {try {File.AppendAllText(System.IO.Path.Combine(root,"companion.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch(IOException) {}}
}
