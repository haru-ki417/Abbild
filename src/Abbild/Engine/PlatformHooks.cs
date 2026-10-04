namespace Abbild.Engine;

/// <summary>
/// ブラウザー版などが、ゲームの流れを変えずに足すための入口。Windows 版ではどれも使わない（null のまま）。
/// </summary>
public static class PlatformHooks
{
    /// <summary>毎フレーム、入力を読む前に呼ぶ（画面のボタンでの操作を足す）。</summary>
    public static Action<Input>? BeforeInput { get; set; }

    /// <summary>全画面の切りかえを、その環境のやり方で行う（ブラウザーでは Fullscreen API）。</summary>
    public static Action<bool>? SetFullscreen { get; set; }

    /// <summary>全画面かどうか（SetFullscreen を使うとき）。</summary>
    public static Func<bool>? IsFullscreen { get; set; }

    /// <summary>「おわる」が使えるか（ブラウザーではページを閉じて終わるので使わない）。</summary>
    public static bool CanExit { get; set; } = true;

    /// <summary>タイトル画面の下に出す操作の案内（null なら Windows 版の文）。</summary>
    public static string? TitleNote { get; set; }
}
