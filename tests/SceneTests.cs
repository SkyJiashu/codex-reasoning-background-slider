using System;
using System.IO;
using System.Windows;
using CrystalDragon;
class SceneTests {
    [STAThread] static int Main(string[] args) {
        if (DataScene.Ease(-1) != 0 || DataScene.Ease(2) != 1 || DataScene.Ease(0.5) != 0.5) return 1;
        if (Math.Abs(DataScene.Duration/DataScene.PlaybackRate - 5.3913043478)>0.0001) return 3;
        double last = 0;
        for (int i = 0; i <= 100; i++) { double n = DataScene.Ease(i / 100.0); if (n < last || n < 0 || n > 1) return 2; last = n; }
        if(args.Length==2) {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Directory.CreateDirectory(args[1]);
            var scene=new DataScene(args[0]);
            double[] frames={1.0,2.3,3.2,5.2};
            for(int i=0;i<frames.Length;i++)scene.RenderPng(frames[i],Path.Combine(args[1],"scene-"+i+".png"));
            string repeated=Path.Combine(args[1],"scene-repeat.png");scene.RenderPng(frames[1],repeated);
            byte[] expected=File.ReadAllBytes(Path.Combine(args[1],"scene-1.png")),actual=File.ReadAllBytes(repeated);
            if(expected.Length!=actual.Length)return 4;
            for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])return 4;
            app.Shutdown();
            Console.WriteLine("Scene render: four off-screen phases and repeat-frame stability passed.");
        }
        Console.WriteLine("Scene timing: endpoints, bounds and 101 monotonic samples passed.");
        return 0;
    }
}
