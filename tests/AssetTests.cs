using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using CrystalDragon;

class AssetTests {
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); }
    static int Main(string[] args) {
        string root = args[0]; int canvas = 0;
        foreach (string name in new[] { "medium.png", "high.png", "extra-high.png", "ultra.png" }) {
            using (var bitmap = new Bitmap(Path.Combine(root, "assets", name))) {
                Assert(bitmap.Width == bitmap.Height && bitmap.Width >= 1000, "production square canvas");
                if (canvas == 0) canvas = bitmap.Width;
                Assert(bitmap.Width == canvas, "same display canvas for all adult stages");
                Assert(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0, "transparent corners");
                int opaque = 0, empty = 0;
                for (int y = 0; y < bitmap.Height; y += 16) for (int x = 0; x < bitmap.Width; x += 16) {
                    int alpha = bitmap.GetPixel(x, y).A; if (alpha >= 128) opaque++; if (alpha == 0) empty++;
                }
                Assert(opaque > 300 && empty > 500, "visible character and substantial true transparency");
            }
        }
        Type controller = typeof(Entry).Assembly.GetType("CrystalDragon.SkinController");
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object instance = controller.GetConstructor(flags, null, new[] { typeof(string) }, null).Invoke(new object[] { root });
        string payload = (string)controller.GetMethod("Payload", flags).Invoke(instance, new object[0]);
        Assert(payload.Length > 1000000 && payload.Length < 15000000, "payload fits the 16M transport/serializer boundary");
        Console.WriteLine("Production assets: consistent " + canvas + "px square canvases, alpha and payload budget passed (" + payload.Length + " characters). No renderer evaluated.");
        return 0;
    }
}
