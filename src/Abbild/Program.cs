using Abbild;
using Abbild.Core;

string? snapshots = null;
string? dataDir = null;
bool windowed = false;
bool autoplay = false;
string? only = null;
double autoSeconds = 240;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--snapshots" when i + 1 < args.Length:
            snapshots = args[++i];
            break;
        case "--data" when i + 1 < args.Length:
            dataDir = args[++i];
            break;
        case "--only" when i + 1 < args.Length:
            only = args[++i];
            break;
        case "--windowed":
            windowed = true;
            break;
        case "--autoplay":
            autoplay = true;
            if (i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec))
            {
                autoSeconds = sec;
                i++;
            }
            break;
    }
}

try
{
    using var game = new AbbildGame(new LaunchOptions(snapshots, windowed, dataDir, autoplay, autoSeconds, only));
    game.Run();
}
catch (Exception ex)
{
    // 落ちたときは、原因を記録に残す（問い合わせの手がかりにする）
    try
    {
        string dir = dataDir ?? SaveStore.DefaultDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}\n{ex}");
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
    throw;
}
