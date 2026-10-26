using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class ExportFairyIcon {
    [STAThread] static void Main() {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        var canvas=new Canvas {Width=238,Height=238};
        FairyEmblem.Build(canvas,Path.Combine(root,"assets/fairy-emblem.json"));
        canvas.Measure(new Size(238,238));canvas.Arrange(new Rect(0,0,238,238));canvas.UpdateLayout();
        var bitmap=new RenderTargetBitmap(238,238,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        string file=Path.Combine(root,"assets/fairy-icon.png");using(var stream=File.Create(file))png.Save(stream);
        using(var source=new System.Drawing.Bitmap(file))using(var small=new System.Drawing.Bitmap(source,128,128)) {
            IntPtr handle=small.GetHicon();
            try {using(var icon=System.Drawing.Icon.FromHandle(handle))using(var output=File.Create(Path.Combine(root,"assets/fairy.ico")))icon.Save(output);}
            finally {DestroyIcon(handle);}
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
