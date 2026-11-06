using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PTVgame {
 public static class GteRouting {
  public const string RunnerPlaceId="71880688656931";
  public static bool SignalOnly(Game game) { return game!=null&&game.category=="Roblox"&&game.placeId==RunnerPlaceId; }
 }

 // HTTPS carries raw LIVE events to exactly one paired PTVRunner room.
 // The game credential is separate from the local engine/OBS credential.
 public static class GteRunnerTunnel {
  const string CloudflaredHash="76b2805253d0d6ffacb092a7acfbee99e7af6917835c0c5410e23f3ef5052da2";
  static Process process; static Task<string> starting; static int generation;
  public static string PairCode="";
  public static bool Running { get { try { return process!=null&&!process.HasExited&&PairCode!=""; } catch { return false; } } }
  public static Task<string> Start() {
   if(Running)return Task.FromResult(PairCode);
   if(starting==null||starting.IsCompleted)starting=StartCore(generation);
   return starting;
  }
  static async Task<string> StartCore(int version) {
   var state=await GteEngine.Get("runner/state");
   if(!GteData.Bool(state,"enabled",false))throw new Exception("Select PTVRunner first");
   string token=GteData.Str(state,"pairingToken","");
   if(!Regex.IsMatch(token,"^[a-f0-9]{64}$"))throw new Exception("Invalid game pairing credential");
   string executable=Path.Combine(GteData.AssetsDir,"cloudflared.exe");
   if(!File.Exists(executable))throw new Exception("Missing assets/cloudflared.exe. Extract the complete Launcher package.");
   string hash=await Task.Run(()=>{using(var sha=SHA256.Create())using(var f=File.OpenRead(executable))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();});
   if(hash!=CloudflaredHash)throw new Exception("cloudflared checksum mismatch");
   if(version!=generation)throw new OperationCanceledException();
   string url="",lastError="";int registered=0;
   var psi=new ProcessStartInfo(executable,"tunnel --no-autoupdate --protocol http2 --url http://127.0.0.1:"+GteEngine.Port+" --http-host-header 127.0.0.1:"+GteEngine.Port) {
    WorkingDirectory=GteData.AssetsDir,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true
   };
   var child=new Process { StartInfo=psi }; process=child;
   DataReceivedEventHandler read=(s,e)=>{if(e.Data==null)return;var m=Regex.Match(e.Data,@"https://([a-z0-9-]+)\.trycloudflare\.com");if(m.Success)Interlocked.Exchange(ref url,m.Groups[1].Value);if(e.Data.Contains("Registered tunnel connection"))Interlocked.Exchange(ref registered,1);if(e.Data.Contains(" ERR "))Interlocked.Exchange(ref lastError,e.Data);};
   child.OutputDataReceived+=read;child.ErrorDataReceived+=read;
   try {
    child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
    for(int n=0;n<180;n++) {
     if(version!=generation)throw new OperationCanceledException();
     if(child.HasExited)throw new Exception("PTVRunner connection stopped. Try creating a new pairing code.");
     var host=Interlocked.CompareExchange(ref url,null,null);
     if(!string.IsNullOrEmpty(host)&&Volatile.Read(ref registered)==1) { PairCode="GTE1|"+host+"|"+token;return PairCode; }
     await Task.Delay(250);
    }
    throw new Exception("Could not create the PTVRunner connection. Check your internet connection and try again. "+Interlocked.CompareExchange(ref lastError,null,null));
   } catch { try { if(!child.HasExited)child.Kill(); }catch{} if(version==generation)PairCode="";throw; }
  }
  public static void Stop() {
   Interlocked.Increment(ref generation);PairCode="";
   var child=process;process=null;
   try { if(child!=null&&!child.HasExited)child.Kill(); } catch {}
  }
 }
}
