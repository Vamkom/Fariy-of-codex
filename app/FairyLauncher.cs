using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
static class FairyLauncher {
    [STAThread] static void Main() {
        bool created;
        using(var gate=new Mutex(true,"Local\\FairyLauncher-Codex-v1",out created)) {
            if(!created)return;
            try {
                string root=AppDomain.CurrentDomain.BaseDirectory;
                string python=Path.GetFullPath(Path.Combine(root,"..",".venv","Scripts","pythonw.exe"));
                var process=Process.Start(new ProcessStartInfo {FileName=python,Arguments="-X utf8 \""+Path.Combine(root,"launch.py")+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true});
                process.WaitForExit();if(process.ExitCode!=0)throw new Exception("See app/launcher-error.log for details.");
            } catch(Exception error) {MessageBox.Show("Fairy 启动失败："+error.Message,"Fairy");}
        }
    }
}
