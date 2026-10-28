using System;
using System.IO;
using System.Web.Script.Serialization;

public class WindowPreferences {
    public bool alwaysOnTop=true;
    public bool alwaysShowCaption=false;
    public bool positioned=false;
    public double left;
    public double top;
    public static WindowPreferences Load(string root) {
        try {return new JavaScriptSerializer().Deserialize<WindowPreferences>(File.ReadAllText(Path.Combine(root,"window-settings.json")))??new WindowPreferences();}
        catch(Exception){return new WindowPreferences();}
    }
    public void Save(string root) {
        string path=Path.Combine(root,"window-settings.json");
        File.WriteAllText(path,new JavaScriptSerializer().Serialize(this));
    }
}
