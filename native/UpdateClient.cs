using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuota {
  public static class AppVersion {
    public static readonly string Current=Read();
    static string Read(){try{var data=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"version.json")));string value;if(data.TryGetValue("version",out value)&&Regex.IsMatch(value,"^[0-9]+\\.[0-9]+\\.[0-9]+(?:\\.[0-9]+)?$"))return value;}catch{}return "2.1.0";}
  }
  public class UpdateConfiguration {
    public string Repository="",AssetName="CodexQuota-win-x64.zip";
    public bool CheckOnStartup=true;
    public int CheckIntervalHours=24;
    public bool IsConfigured {get{return Regex.IsMatch(Repository??"","^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$");}}
    public static UpdateConfiguration Load(string directory) {
      var c=new UpdateConfiguration();
      string file=Path.Combine(directory,"update-source.json");if(!File.Exists(file))return c;
      var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));object v;
      if(data.TryGetValue("repository",out v))c.Repository=Convert.ToString(v).Trim();
      if(data.TryGetValue("checkOnStartup",out v)&&v is bool)c.CheckOnStartup=(bool)v;
      if(data.TryGetValue("checkIntervalHours",out v)){int n;if(int.TryParse(Convert.ToString(v),out n))c.CheckIntervalHours=Math.Max(1,Math.Min(168,n));}
      if(data.TryGetValue("assetName",out v)){string name=Convert.ToString(v);if(!Regex.IsMatch(name,"^[A-Za-z0-9_.-]+\\.zip$"))throw new InvalidDataException("Invalid update asset name.");c.AssetName=name;}
      if(c.Repository.Length>0&&!c.IsConfigured)throw new InvalidDataException("Invalid update repository.");
      return c;
    }
  }
  public class UpdateRelease {public string Version,Notes,PackageUrl,Sha256,HtmlUrl,ChecksumUrl;}
  public class UpdateProgress {public int Percent;public string Phase;}
  public class PreparedUpdate {public string PlanPath,HelperPath;}

  public sealed class UpdateClient : IDisposable {
    readonly string root;
    readonly UpdateConfiguration configuration;
    readonly HttpClient client;
    const long MaximumArchive=100L*1024*1024;
    public UpdateClient(string directory,UpdateConfiguration source):this(directory,source,new HttpClientHandler {AllowAutoRedirect=true}){}
    public UpdateClient(string directory,UpdateConfiguration source,HttpMessageHandler handler) {
      root=Path.GetFullPath(directory);configuration=source;
      ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
      client=new HttpClient(handler);client.Timeout=TimeSpan.FromMinutes(5);
      client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaUpdater/2.1.0");
      client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    static object Get(object source,string key){var d=source as Dictionary<string,object>;object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
    static string Str(object value){return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);}
    static bool SafeAssetUrl(string value,string repository) {
      Uri uri;
      return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)&&String.IsNullOrEmpty(uri.UserInfo)&&uri.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.OrdinalIgnoreCase);
    }
    public static UpdateRelease ParseRelease(string json,string repository,string assetName,string current) {
      object data=new JavaScriptSerializer {MaxJsonLength=2*1024*1024}.DeserializeObject(json);
      if(object.Equals(Get(data,"draft"),true)||object.Equals(Get(data,"prerelease"),true))return null;
      string tag=Str(Get(data,"tag_name"));if(!UpdateCore.IsNewer(tag,current))return null;
      var release=new UpdateRelease {Version=tag.TrimStart('v','V'),Notes=Str(Get(data,"body")),HtmlUrl="https://github.com/"+repository+"/releases"};
      if(release.Notes.Length>16000)release.Notes=release.Notes.Substring(0,16000);
      var assets=Get(data,"assets") as IEnumerable;
      if(assets!=null)foreach(var asset in assets) {
        string name=Str(Get(asset,"name")),url=Str(Get(asset,"browser_download_url"));
        if(name!=assetName&&name!=assetName+".sha256")continue;
        if(!SafeAssetUrl(url,repository))throw new InvalidDataException("Release asset is outside the configured repository.");
        if(name==assetName){if(release.PackageUrl!=null)throw new InvalidDataException("Duplicate package asset.");release.PackageUrl=url;string digest=Str(Get(asset,"digest"));if(Regex.IsMatch(digest,"^sha256:[a-fA-F0-9]{64}$"))release.Sha256=digest.Substring(7).ToLowerInvariant();}
        else release.ChecksumUrl=url;
      }
      if(String.IsNullOrEmpty(release.PackageUrl))throw new InvalidDataException("The release has no Windows update package.");
      if(release.Sha256==null&&release.ChecksumUrl==null)throw new InvalidDataException("The release has no SHA-256 checksum.");
      return release;
    }
    public static string ParseChecksum(string content,string assetName) {
      foreach(string raw in content.TrimStart('\uFEFF').Split('\n')) {
        var match=Regex.Match(raw.Trim(),"^([a-fA-F0-9]{64})(?:\\s+\\*?(.+))?$");
        if(match.Success&&(!match.Groups[2].Success||match.Groups[2].Value==assetName))return match.Groups[1].Value.ToLowerInvariant();
      }
      throw new InvalidDataException("Invalid package checksum.");
    }
    static void CheckRedirect(HttpResponseMessage response,bool api) {
      Uri final=response.RequestMessage.RequestUri;
      bool host=api?final.Host=="api.github.com":final.Host=="github.com"||final.Host=="release-assets.githubusercontent.com"||final.Host=="objects.githubusercontent.com";
      if(final.Scheme!="https"||!host||!String.IsNullOrEmpty(final.UserInfo))throw new InvalidDataException("Unexpected update redirect.");
    }
    async Task<string> ReadText(string url,int maximum,bool api,CancellationToken token) {
      using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token)) {
        response.EnsureSuccessStatusCode();CheckRedirect(response,api);
        if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("Update metadata is too large.");
        using(var input=await response.Content.ReadAsStreamAsync())using(var output=new MemoryStream()) {
          var buffer=new byte[8192];int n;
          while((n=await input.ReadAsync(buffer,0,buffer.Length,token))>0){if(output.Length+n>maximum)throw new InvalidDataException("Update metadata is too large.");output.Write(buffer,0,n);}
          return Encoding.UTF8.GetString(output.ToArray());
        }
      }
    }
    public async Task<UpdateRelease> CheckAsync(CancellationToken token) {
      if(!configuration.IsConfigured)return null;
      string json=await ReadText("https://api.github.com/repos/"+configuration.Repository+"/releases/latest",2*1024*1024,true,token);
      var release=ParseRelease(json,configuration.Repository,configuration.AssetName,AppVersion.Current);
      if(release!=null&&release.Sha256==null)release.Sha256=ParseChecksum(await ReadText(release.ChecksumUrl,8192,false,token),configuration.AssetName);
      return release;
    }
    public async Task<PreparedUpdate> PrepareAsync(UpdateRelease release,IProgress<UpdateProgress> progress,CancellationToken token) {
      if(!SafeAssetUrl(release.PackageUrl,configuration.Repository)||!Regex.IsMatch(release.Sha256??"","^[a-fA-F0-9]{64}$"))throw new InvalidDataException("Unverified update source.");
      string staging=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuota","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
      string archive=Path.Combine(staging,"package.zip");
      using(var response=await client.GetAsync(release.PackageUrl,HttpCompletionOption.ResponseHeadersRead,token)) {
        response.EnsureSuccessStatusCode();CheckRedirect(response,false);
        long length=response.Content.Headers.ContentLength??0;if(length>MaximumArchive)throw new InvalidDataException("Update package is too large.");
        using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
          var buffer=new byte[81920];long total=0;int read;
          while((read=await input.ReadAsync(buffer,0,buffer.Length,token))>0) {
            total+=read;if(total>MaximumArchive)throw new InvalidDataException("Update package is too large.");
            await output.WriteAsync(buffer,0,read,token);
            if(progress!=null)progress.Report(new UpdateProgress {Phase="download",Percent=length>0?(int)Math.Min(75,total*75/length):0});
          }
        }
      }
      token.ThrowIfCancellationRequested();
      if(!String.Equals(UpdateCore.HashFile(archive),release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Package SHA-256 verification failed.");
      if(progress!=null)progress.Report(new UpdateProgress {Phase="verify",Percent=80});
      string package=Path.Combine(staging,"package");
      await Task.Run(delegate{UpdateCore.StagePackage(archive,package,release.Version);},token);
      token.ThrowIfCancellationRequested();
      string helper=Path.Combine(staging,"CodexQuota.Updater.exe");File.Copy(Path.Combine(root,"CodexQuota.Updater.exe"),helper,false);
      int servicePid=0;
      try {
        using(var local=new HttpClient(new HttpClientHandler {UseProxy=false})) {local.Timeout=TimeSpan.FromSeconds(2);string health=await local.GetStringAsync("http://127.0.0.1:43761/health");object d=new JavaScriptSerializer().DeserializeObject(health);if(Str(Get(d,"app"))=="codex-usage-panel")int.TryParse(Str(Get(d,"pid")),out servicePid);}
      }catch{}
      if(servicePid==0) {
        using(var probe=new TcpClient()) {
          try {
            var connection=probe.ConnectAsync("127.0.0.1",43761);
            if(await Task.WhenAny(connection,Task.Delay(2000,token))!=connection)throw new IOException("Cannot confirm the data service is stopped.");
            await connection;
            throw new IOException("The data service could not be identified safely.");
          }catch(SocketException error){if(error.SocketErrorCode!=SocketError.ConnectionRefused)throw;}
        }
      }
      using(var process=Process.GetCurrentProcess()) {
        var plan=new UpdatePlan {InstallRoot=root,PackageDirectory=package,BackupDirectory=Path.Combine(staging,"backup"),FromVersion=AppVersion.Current,ToVersion=release.Version,ParentPid=process.Id,ParentStartTicks=process.StartTime.ToUniversalTime().Ticks,ServicePid=servicePid};
        string planPath=Path.Combine(staging,"plan.json");File.WriteAllText(planPath,new JavaScriptSerializer().Serialize(plan));
        if(progress!=null)progress.Report(new UpdateProgress {Phase="ready",Percent=100});
        return new PreparedUpdate {PlanPath=planPath,HelperPath=helper};
      }
    }
    public void Dispose(){client.Dispose();}
  }
}
