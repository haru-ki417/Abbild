using Abbild.Core;
using Abbild.Engine;
using Microsoft.JSInterop;

namespace Abbild.Web;

/// <summary>
/// ブラウザーとゲームのつなぎ:
///   ・絵とフォントは、ブラウザーの中のファイル（メモリー上）に置く。ゲームは Windows 版と同じくファイルとして読む
///   ・セーブ（冒険の書・設定）はメモリー上に書かれるので、変わったものをこの端末（localStorage）にも残す
///   ・画面のボタンの操作を、ゲームの入力に足す
/// </summary>
internal sealed class GameHost
{
    private readonly Dictionary<string, (long Length, DateTime Written)> synced = [];
    private IJSInProcessRuntime? js;
    private int frames;
    private bool started;

    public static string ContentRoot => Path.Combine(AppContext.BaseDirectory, "Content");

    public static string SaveDirectory => SaveStore.DefaultDirectory();

    /// <summary>画面のボタンで押している操作（Act の順のビット）</summary>
    public int TouchMask { get; set; }

    public static void PutContent(string path, byte[] bytes)
    {
        string full = Path.Combine(ContentRoot, path.Replace('\\', '/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public static void PutSave(string name, string text)
    {
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal)) return;
        Directory.CreateDirectory(SaveDirectory);
        File.WriteAllText(Path.Combine(SaveDirectory, name), text);
    }

    public void Attach(IJSInProcessRuntime runtime)
    {
        js = runtime;
        Remember();
        PlatformHooks.BeforeInput = input =>
        {
            int m = TouchMask;
            if (m == 0) return;
            foreach (Act a in Enum.GetValues<Act>())
            {
                if ((m & (1 << (int)a)) != 0) input.Inject(a);
            }
        };
        // 全画面はブラウザーの Fullscreen API で（起動したときの設定では切りかえない。ブラウザーはボタンを押したときだけ全画面にできる）
        PlatformHooks.SetFullscreen = on =>
        {
            if (started) js.InvokeVoid("abbildHost.fullscreen", on);
        };
        PlatformHooks.IsFullscreen = () => js.Invoke<bool>("abbildHost.isFullscreen");
        PlatformHooks.CanExit = false;
        PlatformHooks.TitleNote = "ブラウザー版：キーボード・ゲームパッド・画面のボタンで遊べます（セーブはこのブラウザーに残ります）";
    }

    public void AfterTick()
    {
        started = true;
        // 1 秒ごとに、セーブが変わっていないかを見る
        if (++frames % 60 == 0) SyncSaves();
    }

    private void Remember()
    {
        synced.Clear();
        if (!Directory.Exists(SaveDirectory)) return;
        foreach (var f in new DirectoryInfo(SaveDirectory).EnumerateFiles("*.json"))
            synced[f.Name] = (f.Length, f.LastWriteTimeUtc);
    }

    private void SyncSaves()
    {
        if (js is null) return;
        var now = new Dictionary<string, (long, DateTime)>();
        if (Directory.Exists(SaveDirectory))
        {
            foreach (var f in new DirectoryInfo(SaveDirectory).EnumerateFiles("*.json"))
            {
                var stamp = (f.Length, f.LastWriteTimeUtc);
                now[f.Name] = stamp;
                if (!synced.TryGetValue(f.Name, out var old) || old != stamp)
                    js.InvokeVoid("abbildHost.storeSave", f.Name, File.ReadAllText(f.FullName));
            }
        }
        foreach (var name in synced.Keys.Where(k => !now.ContainsKey(k)))
            js.InvokeVoid("abbildHost.removeSave", name);
        synced.Clear();
        foreach (var kv in now) synced[kv.Key] = kv.Value;
    }
}
