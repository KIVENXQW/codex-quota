using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexQuota {
  internal static class UpdateHelper {
    [DllImport("shell32.dll",SetLastError=true)]static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)]string commandLine,out int count);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);

    [STAThread]static int Main(string[] args) {
      UpdatePlan plan=null;string root=null,stage="读取更新计划";bool restartAllowed=false,applied=false;var log=new List<string>();
      try {
        if(args.Length!=1 || !Path.IsPathRooted(args[0]))throw new InvalidDataException("请由额度工具启动更新器。");
        UpdateCore.EnsureNoReparsePath(args[0]);
        if(new FileInfo(args[0]).Length>1024*1024)throw new InvalidDataException("更新计划过大。");
        plan=new JavaScriptSerializer {MaxJsonLength=1024*1024}.Deserialize<UpdatePlan>(File.ReadAllText(args[0]));
        if(plan==null)throw new InvalidDataException("更新计划无效。");
        root=UpdateCore.AbsoluteDirectory(plan.InstallRoot);
        if(!Directory.Exists(root) || !File.Exists(UpdateCore.Inside(root,"CodexQuota.exe")))throw new InvalidDataException("原程序不存在。");
        stage="等待原程序退出";WaitForParent(plan,root);restartAllowed=true;log.Add("原程序身份已确认并已退出。");
        string runningHelper=Process.GetCurrentProcess().MainModule.FileName;
        if(String.Equals(Path.GetFullPath(runningHelper),UpdateCore.Inside(root,"CodexQuota.Updater.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新器必须从独立暂存副本运行。");
        stage="验证更新包";UpdateCore.ValidatePlan(plan);log.Add("版本、白名单及文件 SHA256 已验证。");
        stage="停止本机额度服务";StopService(plan,root);log.Add("指定服务已停止或原本未运行。");
        stage="替换运行文件";UpdateCore.ApplyFiles(plan);applied=true;log.Add("运行文件已替换，原文件保留在备份目录。");
        stage="启动新版程序";Restart(root);log.Add("已启动安装目录中的程序。");
        SaveResult(root,plan,true,null,log);return 0;
      }catch(Exception error) {
        bool restartRollbackFailed=false;
        if(applied && plan!=null)try{UpdateCore.RestoreAppliedFiles(plan);applied=false;log.Add("新版未能启动，已从备份恢复旧版。");}catch{restartRollbackFailed=true;log.Add("启动失败后的自动恢复未完成，请保留备份目录。");}
        string message=stage+"失败。"+(restartRollbackFailed?"自动恢复未完全成功，请保留备份目录并手动恢复。":"原程序文件已保留；已执行的文件替换会按事务回滚。")+Environment.NewLine+SafeMessage(error.Message);
        log.Add(stage+"失败。");
        if(restartAllowed && root!=null)try{Restart(root);log.Add("已尝试重新启动安装目录中的程序。");}catch{log.Add("重新启动失败，请手动打开额度工具。");}
        if(root!=null && plan!=null)SaveResult(root,plan,false,message,log);
        MessageBox.Show(message,"Codex 额度工具更新失败",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
      }
    }

    static string SafeMessage(string value) {
      if(String.IsNullOrWhiteSpace(value))return "请保留备份并稍后重试。";
      value=value.Replace("\r"," ").Replace("\n"," ");return value.Length>500?value.Substring(0,500):value;
    }
    static void WaitForParent(UpdatePlan plan,string root) {
      if(plan.ParentPid<=0 || plan.ParentStartTicks<=0 || plan.ParentPid==Process.GetCurrentProcess().Id)throw new InvalidDataException("原程序进程信息无效。");
      Process parent;try{parent=Process.GetProcessById(plan.ParentPid);}catch(ArgumentException){return;}
      using(parent) {
        try {
          if(parent.HasExited)return;
          if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStartTicks)throw new InvalidDataException("原程序 PID 已被其他进程使用，已取消更新。");
          string expected=UpdateCore.Inside(root,"CodexQuota.exe");
          if(!String.Equals(Path.GetFullPath(parent.MainModule.FileName),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("原程序进程路径不匹配，已取消更新。");
          if(!parent.WaitForExit(30000))throw new IOException("原程序仍在运行，请退出后重试。");
        }catch(InvalidOperationException){if(!parent.HasExited)throw;}
      }
    }
    static string[] Arguments(string commandLine) {
      if(String.IsNullOrEmpty(commandLine))return new string[0];int count;IntPtr pointer=CommandLineToArgvW(commandLine,out count);
      if(pointer==IntPtr.Zero)throw new InvalidDataException("无法验证服务启动参数。");
      try{var values=new string[count];for(int i=0;i<count;i++)values[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,i*IntPtr.Size));return values;}finally{LocalFree(pointer);}
    }
    static bool ExpectedService(string executable,string commandLine,string root) {
      if(String.IsNullOrEmpty(executable) || !String.Equals(Path.GetFileName(executable),"node.exe",StringComparison.OrdinalIgnoreCase))return false;
      string[] args=Arguments(commandLine);
      // Start-Panel launches node.exe with this absolute script as its entry point.
      // A matching path merely passed to another script must not authorize a kill.
      if(args.Length<2 || !Path.IsPathRooted(args[1]))return false;
      return String.Equals(Path.GetFullPath(args[1]),UpdateCore.Inside(root,"server.mjs"),StringComparison.OrdinalIgnoreCase);
    }
    static void StopService(UpdatePlan plan,string root) {
      if(plan.ServicePid==0)return;
      if(plan.ServicePid<0 || plan.ServicePid==Process.GetCurrentProcess().Id || plan.ServicePid==plan.ParentPid)throw new InvalidDataException("服务进程信息无效。");
      Process service;try{service=Process.GetProcessById(plan.ServicePid);}catch(ArgumentException){return;}
      using(service) {
        if(service.HasExited)return;long started=service.StartTime.ToUniversalTime().Ticks;
        string executable=null,commandLine=null;
        using(var query=new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE ProcessId = "+plan.ServicePid.ToString(CultureInfo.InvariantCulture)))
        using(var rows=query.Get())foreach(ManagementObject row in rows)using(row){executable=row["ExecutablePath"] as string;commandLine=row["CommandLine"] as string;}
        if(!ExpectedService(executable,commandLine,root))throw new InvalidDataException("指定 PID 不是该安装目录的 Node 服务，未终止任何无关进程。");
        Process confirmed;try{confirmed=Process.GetProcessById(plan.ServicePid);}catch(ArgumentException){return;}
        using(confirmed){if(confirmed.StartTime.ToUniversalTime().Ticks!=started)throw new InvalidDataException("服务 PID 已变化，已取消终止操作。");confirmed.Kill();if(!confirmed.WaitForExit(10000))throw new IOException("服务进程未能及时退出。");}
      }
    }
    static void Restart(string root) {
      string executable=UpdateCore.Inside(root,"CodexQuota.exe");
      var info=new ProcessStartInfo(executable){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
      using(var process=Process.Start(info)){if(process==null)throw new IOException("无法启动额度工具。");if(process.WaitForExit(2000)&&process.ExitCode!=0)throw new IOException("额度工具启动后立即异常退出。");}
    }
    static void SaveResult(string root,UpdatePlan plan,bool success,string error,List<string> log) {
      try {
        string directory=UpdateCore.Inside(root,".data");Directory.CreateDirectory(directory);UpdateCore.EnsureNoReparsePath(directory);
        string target=UpdateCore.Inside(root,".data/update-result.json");
        string json=new JavaScriptSerializer().Serialize(new {success=success,fromVersion=plan.FromVersion,toVersion=plan.ToVersion,completedAt=DateTime.UtcNow.ToString("o"),error=error,log=log.ToArray()});
        File.WriteAllText(target,json);
      }catch{}
    }
  }
}
