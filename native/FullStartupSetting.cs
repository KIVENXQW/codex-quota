using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CodexQuota {
  internal static class FullStartupSetting {
    internal static bool Invoke(string directory,string action) {
      if(action!="Status" && action!="Enable" && action!="Disable")throw new ArgumentOutOfRangeException("action");
      string script=Path.Combine(directory,"Start-Native.ps1");
      var start=new ProcessStartInfo {
        FileName=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),
        Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -NoLaunch -ReportState -Startup "+action,
        WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
        RedirectStandardOutput=true,RedirectStandardError=true
      };
      using(var process=Process.Start(start)) {
        Task<string> output=process.StandardOutput.ReadToEndAsync();
        Task<string> error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10000)) {
          process.Kill();
          throw new TimeoutException("Timed out reading Windows login startup.");
        }
        Task.WaitAll(output,error);
        if(process.ExitCode==0) {
          string value=output.Result.Trim();
          if(value=="FULL_STARTUP=1")return true;
          if(value=="FULL_STARTUP=0")return false;
        }
        throw new InvalidOperationException("Windows login startup status is unavailable.");
      }
    }
  }
}
