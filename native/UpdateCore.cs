using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace CodexQuota {
  public sealed class UpdateManifest { public string Version; public List<UpdateFile> Files; }
  public sealed class UpdateFile { public string Path; public string Sha256; }
  public sealed class UpdatePlan { public string InstallRoot,PackageDirectory,BackupDirectory,FromVersion,ToVersion; public int ParentPid; public long ParentStartTicks; public int ServicePid; }
  public static class UpdateCore {
    const long MaxZipBytes=100L*1024*1024,MaxExtractedBytes=300L*1024*1024;
    const int MaxManifestBytes=1024*1024;
    static readonly string[] Required={"CodexQuota.exe","CodexQuota.Updater.exe","server.mjs","codex-account.mjs","usage-model.mjs","activity-model.mjs","activity-collector.mjs","activity-tracker.mjs","Start-Panel.ps1","version.json"};
    static readonly HashSet<string> Allowed=new HashSet<string>(new[]{"CodexQuota.exe","CodexQuota.Updater.exe","Start-Panel.ps1","Start-Native.ps1","server.mjs","codex-account.mjs","usage-model.mjs","activity-model.mjs","activity-collector.mjs","activity-tracker.mjs","version.json","package.json","public/index.html","public/app.js","public/style.css","update-source.json"},StringComparer.OrdinalIgnoreCase);

    static Version ReadVersion(string value) {
      if(value!=null && value.StartsWith("v",StringComparison.OrdinalIgnoreCase))value=value.Substring(1);
      Version parsed;
      if(value==null || !Regex.IsMatch(value,@"^\d+\.\d+\.\d+(?:\.\d+)?$") || !Version.TryParse(value,out parsed))throw new InvalidDataException("更新版本号无效。");
      return new Version(parsed.Major,parsed.Minor,Math.Max(0,parsed.Build),Math.Max(0,parsed.Revision));
    }
    public static bool IsNewer(string candidate,string current) {try{return ReadVersion(candidate)>ReadVersion(current);}catch{return false;}}
    public static bool IsAllowedPath(string path) {
      return !String.IsNullOrEmpty(path) && path.IndexOf('\\')<0 && path.IndexOf(':')<0 && !System.IO.Path.IsPathRooted(path) && Allowed.Contains(path);
    }
    public static string HashFile(string path) {
      using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    public static UpdateManifest ParseManifest(string json) {
      if(json==null || json.Length>MaxManifestBytes)throw new InvalidDataException("更新清单缺失或过大。");
      var root=new JavaScriptSerializer {MaxJsonLength=MaxManifestBytes}.DeserializeObject(json) as Dictionary<string,object>;
      object value;
      if(root==null || !root.TryGetValue("version",out value) || !(value is string))throw new InvalidDataException("更新清单缺少版本号。");
      var manifest=new UpdateManifest {Version=(string)value,Files=new List<UpdateFile>()};ReadVersion(manifest.Version);
      if(!root.TryGetValue("files",out value) || !(value is object[]))throw new InvalidDataException("更新清单缺少文件列表。");
      var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(object row in (object[])value) {
        var fields=row as Dictionary<string,object>;object pathValue,hashValue;
        if(fields==null || !fields.TryGetValue("path",out pathValue) || !fields.TryGetValue("sha256",out hashValue) || !(pathValue is string) || !(hashValue is string))throw new InvalidDataException("更新清单文件记录无效。");
        string path=(string)pathValue,hash=(string)hashValue;
        if(!IsAllowedPath(path) || !seen.Add(path))throw new InvalidDataException("更新清单含不允许或重复的文件路径。");
        if(!Regex.IsMatch(hash,@"\A[0-9a-fA-F]{64}\z"))throw new InvalidDataException("更新清单 SHA256 无效。");
        manifest.Files.Add(new UpdateFile {Path=path,Sha256=hash.ToLowerInvariant()});
      }
      foreach(string required in Required)if(!seen.Contains(required))throw new InvalidDataException("更新包缺少必需文件："+required);
      return manifest;
    }

    internal static string AbsoluteDirectory(string path) {
      if(String.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))throw new InvalidDataException("更新目录必须是绝对路径。");
      string absolute=System.IO.Path.GetFullPath(path);
      if(String.Equals(absolute.TrimEnd('\\','/'),System.IO.Path.GetPathRoot(absolute).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("不能使用磁盘根目录作为更新目录。");
      string full=absolute.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
      EnsureNoReparsePath(full);return full;
    }
    internal static void EnsureNoReparsePath(string path) {
      string full=System.IO.Path.GetFullPath(path),part=System.IO.Path.GetPathRoot(full);
      CheckReparse(part);
      foreach(string segment in full.Substring(part.Length).Split(new[]{System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries)){part=System.IO.Path.Combine(part,segment);CheckReparse(part);}
    }
    static void CheckReparse(string path) {
      try{if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新路径不能经过符号链接或重解析目录。");}
      catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
    }
    internal static string Inside(string root,string relative) {
      string target=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative.Replace('/',System.IO.Path.DirectorySeparatorChar)));
      if(!target.StartsWith(root+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新路径越出目标目录。");
      EnsureNoReparsePath(target);return target;
    }
    static bool EqualPath(string left,string right){return String.Equals(left,right,StringComparison.OrdinalIgnoreCase);}
    static string ReadSmallFile(string path) {if(new FileInfo(path).Length>MaxManifestBytes)throw new InvalidDataException("更新元数据文件过大。");return File.ReadAllText(path);}
    static void CheckVersionFile(string root,string expectedVersion) {
      var data=new JavaScriptSerializer().DeserializeObject(ReadSmallFile(Inside(root,"version.json"))) as Dictionary<string,object>;object version;
      if(data==null || !data.TryGetValue("version",out version) || !(version is string) || ReadVersion((string)version)!=ReadVersion(expectedVersion))throw new InvalidDataException("version.json 与更新清单版本不一致。");
    }
    static void VerifyFiles(string root,UpdateManifest manifest) {
      foreach(var file in manifest.Files){string path=Inside(root,file.Path);if(!File.Exists(path) || !String.Equals(HashFile(path),file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新文件校验失败："+file.Path);}
      CheckVersionFile(root,manifest.Version);
    }

    public static UpdateManifest StagePackage(string zipPath,string extractionRoot,string expectedVersion) {
      ReadVersion(expectedVersion);
      if(!System.IO.Path.IsPathRooted(zipPath))throw new InvalidDataException("更新包必须使用绝对路径。");
      EnsureNoReparsePath(zipPath);
      if(new FileInfo(zipPath).Length>MaxZipBytes)throw new InvalidDataException("更新压缩包超过 100 MB 限制。");
      string root=AbsoluteDirectory(extractionRoot);
      if(Directory.Exists(root) || File.Exists(root))throw new InvalidDataException("暂存目录必须是尚未创建的唯一目录。");
      using(var archive=ZipFile.OpenRead(zipPath)) {
        var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);long expanded=0;
        foreach(var entry in archive.Entries) {
          string name=entry.FullName;
          if(((entry.ExternalAttributes>>16)&0xF000)==0xA000 || (entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新包不能包含符号链接。");
          if(name.IndexOf('\\')>=0 || name.IndexOf(':')>=0 || System.IO.Path.IsPathRooted(name))throw new InvalidDataException("更新包路径无效。");
          if(name.EndsWith("/",StringComparison.Ordinal)){if(!String.Equals(name,"public/",StringComparison.OrdinalIgnoreCase) || entry.Length!=0)throw new InvalidDataException("更新包含未知目录。");if(entries.ContainsKey(name))throw new InvalidDataException("更新包含重复目录。");entries.Add(name,entry);continue;}
          if(!String.Equals(name,"update-manifest.json",StringComparison.OrdinalIgnoreCase) && !IsAllowedPath(name))throw new InvalidDataException("更新包含未授权文件。");
          if(entries.ContainsKey(name))throw new InvalidDataException("更新包含重复文件。");
          entries.Add(name,entry);expanded=checked(expanded+entry.Length);
          if(expanded>MaxExtractedBytes)throw new InvalidDataException("更新包解压后超过 300 MB 限制。");
        }
        ZipArchiveEntry manifestEntry;
        if(!entries.TryGetValue("update-manifest.json",out manifestEntry) || manifestEntry.Length>MaxManifestBytes)throw new InvalidDataException("更新包没有有效清单。");
        string json;
        using(var reader=new StreamReader(manifestEntry.Open())){var text=new StringBuilder();var chars=new char[4096];int count;while((count=reader.Read(chars,0,chars.Length))>0){if(text.Length+count>MaxManifestBytes)throw new InvalidDataException("更新清单解压后过大。");text.Append(chars,0,count);}json=text.ToString();}
        UpdateManifest manifest=ParseManifest(json);
        if(ReadVersion(manifest.Version)!=ReadVersion(expectedVersion))throw new InvalidDataException("更新包版本与所选版本不一致。");
        var declared=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var file in manifest.Files){declared.Add(file.Path);if(!entries.ContainsKey(file.Path))throw new InvalidDataException("更新包缺少清单文件："+file.Path);}
        foreach(string name in entries.Keys)if(!name.EndsWith("/",StringComparison.Ordinal) && !String.Equals(name,"update-manifest.json",StringComparison.OrdinalIgnoreCase) && !declared.Contains(name))throw new InvalidDataException("更新包包含未列入清单的文件。");
        Directory.CreateDirectory(root);long extracted=0;
        foreach(var pair in entries) {
          if(pair.Key.EndsWith("/",StringComparison.Ordinal))continue;
          string target=Inside(root,pair.Key),parent=System.IO.Path.GetDirectoryName(target);Directory.CreateDirectory(parent);EnsureNoReparsePath(parent);
          using(var input=pair.Value.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
            var buffer=new byte[65536];int read;long written=0;
            while((read=input.Read(buffer,0,buffer.Length))>0){written+=read;extracted+=read;if(written>pair.Value.Length || extracted>MaxExtractedBytes)throw new InvalidDataException("解压数据超出声明大小。");output.Write(buffer,0,read);}
            if(written!=pair.Value.Length)throw new InvalidDataException("更新包文件长度不匹配。");
          }
        }
        VerifyFiles(root,manifest);return manifest;
      }
    }

    internal static UpdateManifest ValidatePlan(UpdatePlan plan) {
      if(plan==null)throw new InvalidDataException("更新计划为空。");
      plan.InstallRoot=AbsoluteDirectory(plan.InstallRoot);plan.PackageDirectory=AbsoluteDirectory(plan.PackageDirectory);plan.BackupDirectory=AbsoluteDirectory(plan.BackupDirectory);
      if(!Directory.Exists(plan.InstallRoot) || !Directory.Exists(plan.PackageDirectory))throw new InvalidDataException("安装或暂存目录不存在。");
      if(EqualPath(plan.InstallRoot,plan.PackageDirectory) || EqualPath(plan.BackupDirectory,plan.InstallRoot) || EqualPath(plan.BackupDirectory,plan.PackageDirectory))throw new InvalidDataException("更新目录不能相同。");
      if(Directory.Exists(plan.BackupDirectory) || File.Exists(plan.BackupDirectory))throw new InvalidDataException("备份目录必须是新的唯一目录。");
      if(!IsNewer(plan.ToVersion,plan.FromVersion))throw new InvalidDataException("目标版本必须比当前版本新。");
      UpdateManifest manifest=ParseManifest(ReadSmallFile(Inside(plan.PackageDirectory,"update-manifest.json")));
      if(ReadVersion(manifest.Version)!=ReadVersion(plan.ToVersion))throw new InvalidDataException("更新计划与清单版本不一致。");
      VerifyFiles(plan.PackageDirectory,manifest);CheckVersionFile(plan.InstallRoot,plan.FromVersion);
      foreach(var file in manifest.Files)if(!String.Equals(file.Path,"update-source.json",StringComparison.OrdinalIgnoreCase))Inside(plan.InstallRoot,file.Path);
      return manifest;
    }

    sealed class SavedFile {public string Relative,OriginalHash;public bool Existed;}
    static void AtomicCopy(string source,string destination,string expectedHash) {
      EnsureNoReparsePath(destination);string directory=System.IO.Path.GetDirectoryName(destination);Directory.CreateDirectory(directory);EnsureNoReparsePath(directory);
      string temp=System.IO.Path.Combine(directory,".codex-update-"+Guid.NewGuid().ToString("N")+".tmp");
      try {
        File.Copy(source,temp,false);
        if(!String.Equals(HashFile(temp),expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("复制后的更新文件校验失败。");
        EnsureNoReparsePath(destination);
        if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);
      }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void ApplyFiles(UpdatePlan plan) {
      UpdateManifest manifest=ValidatePlan(plan);Directory.CreateDirectory(plan.BackupDirectory);EnsureNoReparsePath(plan.BackupDirectory);
      var backups=new Dictionary<string,SavedFile>(StringComparer.OrdinalIgnoreCase);var changed=new List<SavedFile>();
      // Complete every backup before replacing the first installation file.
      foreach(var file in manifest.Files) {
        if(String.Equals(file.Path,"update-source.json",StringComparison.OrdinalIgnoreCase))continue;
        string target=Inside(plan.InstallRoot,file.Path);var saved=new SavedFile {Relative=file.Path,Existed=File.Exists(target)};
        if(Directory.Exists(target))throw new IOException("运行文件路径被目录占用："+file.Path);
        if(saved.Existed){saved.OriginalHash=HashFile(target);string backup=Inside(plan.BackupDirectory,file.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));File.Copy(target,backup,false);if(HashFile(backup)!=saved.OriginalHash)throw new IOException("备份校验失败："+file.Path);}
        backups.Add(file.Path,saved);
      }
      var receiptFiles=new List<object>();foreach(var saved in backups.Values)receiptFiles.Add(new {path=saved.Relative,existed=saved.Existed,sha256=saved.OriginalHash});
      File.WriteAllText(Inside(plan.BackupDirectory,".rollback.json"),new JavaScriptSerializer().Serialize(new {fromVersion=plan.FromVersion,toVersion=plan.ToVersion,files=receiptFiles}));
      try {
        foreach(var file in manifest.Files) {
          if(String.Equals(file.Path,"update-source.json",StringComparison.OrdinalIgnoreCase))continue;
          string target=Inside(plan.InstallRoot,file.Path);
          AtomicCopy(Inside(plan.PackageDirectory,file.Path),target,file.Sha256);changed.Add(backups[file.Path]);
          if(!String.Equals(HashFile(target),file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("安装后校验失败："+file.Path);
        }
      }catch(Exception failure) {
        bool rollbackFailed=false;
        for(int i=changed.Count-1;i>=0;i--)try {
          var saved=changed[i];string target=Inside(plan.InstallRoot,saved.Relative);
          if(saved.Existed){AtomicCopy(Inside(plan.BackupDirectory,saved.Relative),target,saved.OriginalHash);if(HashFile(target)!=saved.OriginalHash)throw new IOException("回滚校验失败。");}
          else if(File.Exists(target))File.Delete(target);
        }catch{rollbackFailed=true;}
        if(rollbackFailed)throw new IOException("更新失败，部分文件未能自动恢复；原文件仍保留在备份目录。",failure);
        throw new IOException("更新失败，已恢复原版本文件。",failure);
      }
    }
    // Called only after ApplyFiles succeeded but the replacement EXE could not start.
    // The receipt distinguishes absent old files from missing/corrupt backups.
    internal static void RestoreAppliedFiles(UpdatePlan plan) {
      string install=AbsoluteDirectory(plan.InstallRoot),backup=AbsoluteDirectory(plan.BackupDirectory);
      var receipt=new JavaScriptSerializer().DeserializeObject(ReadSmallFile(Inside(backup,".rollback.json"))) as Dictionary<string,object>;object from,to,rows;
      if(receipt==null || !receipt.TryGetValue("fromVersion",out from) || !receipt.TryGetValue("toVersion",out to) || !receipt.TryGetValue("files",out rows) || !(rows is object[]) || !String.Equals(from as string,plan.FromVersion,StringComparison.Ordinal) || !String.Equals(to as string,plan.ToVersion,StringComparison.Ordinal))throw new InvalidDataException("回滚记录与更新计划不一致。");
      var files=new List<SavedFile>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(object row in (object[])rows) {
        var fields=row as Dictionary<string,object>;object path,existed,hash;
        if(fields==null || !fields.TryGetValue("path",out path) || !fields.TryGetValue("existed",out existed) || !fields.TryGetValue("sha256",out hash) || !(path is string) || !(existed is bool) || !IsAllowedPath((string)path) || String.Equals((string)path,"update-source.json",StringComparison.OrdinalIgnoreCase) || !seen.Add((string)path))throw new InvalidDataException("回滚文件记录无效。");
        if((bool)existed && (!(hash is string) || !Regex.IsMatch((string)hash,@"\A[0-9a-fA-F]{64}\z")))throw new InvalidDataException("回滚文件 SHA256 无效。");
        files.Add(new SavedFile {Relative=(string)path,Existed=(bool)existed,OriginalHash=hash as string});
      }
      foreach(string required in Required)if(!seen.Contains(required))throw new InvalidDataException("回滚记录缺少必需文件。");
      bool failed=false;
      for(int i=files.Count-1;i>=0;i--)try{var saved=files[i];string target=Inside(install,saved.Relative);if(saved.Existed){AtomicCopy(Inside(backup,saved.Relative),target,saved.OriginalHash);if(!String.Equals(HashFile(target),saved.OriginalHash,StringComparison.OrdinalIgnoreCase))throw new IOException("回滚校验失败。");}else if(File.Exists(target))File.Delete(target);}catch{failed=true;}
      if(failed)throw new IOException("部分文件无法恢复；请保留备份目录并手动恢复。");
    }
  }
}
