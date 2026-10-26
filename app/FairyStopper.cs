using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
static class FairyStopper {
    [STAThread] static void Main() {
        try {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string python=Path.GetFullPath(Path.Combine(root,"..",".venv","Scripts","pythonw.exe"));
            Process.Start(new ProcessStartInfo {FileName=python,Arguments="-X utf8 \""+Path.Combine(root,"stop_fairy.py")+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true});
        } catch(Exception error) {MessageBox.Show(error.Message,"Fairy");}
    }
}
