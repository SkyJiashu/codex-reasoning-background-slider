using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using CrystalDragon;

class StartupPreparationTests {
    [STAThread] static int Main(string[] args) {
        try {
            var timer=Stopwatch.StartNew();
            var controller=new SkinController(Path.GetFullPath(args[0]));
            controller.BeginPreparation();
            if(controller.ProcessId!=0)throw new Exception("preparation created a native client");
            if(!controller.Preparation.Wait(15000))throw new Exception("preparation deadline");
            controller.BeginPreparation();
            if(controller.ProcessId!=0||controller.Ready||controller.ShellReady)throw new Exception("read-only preparation changed a client state");
            string qa=Path.Combine(Path.GetFullPath(args[0]),"qa");Directory.CreateDirectory(qa);
            File.WriteAllText(Path.Combine(qa,"preparation-only.json"),new JavaScriptSerializer().Serialize(new { ready=true, nativeClientCreated=false, timing=controller.TimingSnapshot }));
            string cacheRoot=Path.Combine(qa,"visual-cache-test"),cachePath=Path.Combine(cacheRoot,"appearance-stage.txt");
            Directory.CreateDirectory(cacheRoot);if(File.Exists(cachePath))File.Delete(cachePath);
            var cached=new SkinController(cacheRoot);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var read=typeof(SkinController).GetMethod("ReadStartupStage",flags);
            var remember=typeof(SkinController).GetMethod("RememberStage",flags);
            if((int)read.Invoke(cached,null)!=4)throw new Exception("first startup mascot missing");
            remember.Invoke(cached,new object[]{new Dictionary<string,object>{{"status","unknown"},{"stage",4}}});
            if(File.Exists(cachePath))throw new Exception("provisional state persisted as native effort");
            remember.Invoke(cached,new object[]{new Dictionary<string,object>{{"status","confirmed"},{"stage",2}}});
            if((int)read.Invoke(cached,null)!=2||new FileInfo(cachePath).Length!=1)throw new Exception("confirmed visual cache must be one bounded digit");
            foreach(var state in new[]{new Dictionary<string,object>{{"status","pending"},{"stage",4}},new Dictionary<string,object>{{"status","confirmed"},{"stage",5}}})remember.Invoke(cached,new object[]{state});
            if((int)read.Invoke(cached,null)!=2)throw new Exception("invalid/pending effort replaced confirmed stage");
            File.WriteAllText(cachePath,"9");if((int)read.Invoke(cached,null)!=4)throw new Exception("invalid stage accepted");
            File.WriteAllText(cachePath,"invalid");if((int)read.Invoke(cached,null)!=4)throw new Exception("non-cache data accepted");
            File.Delete(cachePath);
            Console.WriteLine("Preparation: identity and local artwork tasks completed before any native creation ("+timer.ElapsedMilliseconds+"ms); no renderer or current client connected.");
            return 0;
        } catch(Exception error) {Console.WriteLine("Preparation check failed: "+error.GetBaseException().GetType().Name);return 1;}
    }
}
