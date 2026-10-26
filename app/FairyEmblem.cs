using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

public class FairyCircle {public double radius;public string fill;public string stroke;public double width;}
public class FairyDiamond {public string points;public string fill;}
public class FairyOrb {public double x;public double y;public double radius;public string fill;}
public class FairyIconSpec {
    public int version;public double canvas;public FairyCircle[] circles;public FairyCircle[] rings;
    public FairyDiamond diamond;public FairyOrb orb;
}
public static class FairyEmblem {
    static Brush Color(string value) {return (Brush)new BrushConverter().ConvertFromString(value);}
    static void Circle(Canvas canvas,FairyCircle spec,double center) {
        var element=new Ellipse {Width=spec.radius*2,Height=spec.radius*2,Fill=Color(spec.fill),
            Stroke=Color(spec.stroke),StrokeThickness=spec.width,IsHitTestVisible=false};
        Canvas.SetLeft(element,center-spec.radius);Canvas.SetTop(element,center-spec.radius);canvas.Children.Add(element);
    }
    public static void Build(Canvas canvas,string asset) {
        var spec=new JavaScriptSerializer().Deserialize<FairyIconSpec>(File.ReadAllText(asset));
        if(spec.version!=2||spec.canvas!=238)throw new InvalidDataException("Unsupported Fairy emblem");
        double center=spec.canvas/2;
        var glow=new Ellipse {Width=222,Height=222,Stroke=Color("#58b8ff"),StrokeThickness=2,Opacity=.7,
            Effect=new DropShadowEffect {Color=System.Windows.Media.Color.FromRgb(60,155,255),BlurRadius=23,ShadowDepth=0,Opacity=.8},IsHitTestVisible=false};
        Canvas.SetLeft(glow,8);Canvas.SetTop(glow,8);canvas.Children.Add(glow);
        foreach(var circle in spec.circles)Circle(canvas,circle,center);
        var points=new PointCollection();
        foreach(var pair in spec.diamond.points.Split(' ')) {
            var xy=pair.Split(',');points.Add(new Point(double.Parse(xy[0]),double.Parse(xy[1])));
        }
        canvas.Children.Add(new Polygon {Points=points,Fill=Color(spec.diamond.fill),IsHitTestVisible=false});
        foreach(var ring in spec.rings)Circle(canvas,ring,center);
        var orb=new Ellipse {Width=spec.orb.radius*2,Height=spec.orb.radius*2,Fill=Color(spec.orb.fill),IsHitTestVisible=false};
        Canvas.SetLeft(orb,spec.orb.x-spec.orb.radius);Canvas.SetTop(orb,spec.orb.y-spec.orb.radius);canvas.Children.Add(orb);
    }
}
