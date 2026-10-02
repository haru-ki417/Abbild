using Abbild.Controller;
using Abbild.Core;
using Abbild.Engine;
using Abbild.Scenes;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Abbild;

public sealed record LaunchOptions(string? SnapshotDir, bool Windowed, string? DataDir, bool AutoPlay, double AutoPlaySeconds);

public sealed class AbbildGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private RenderTarget2D? _target;
    private Services? _s;
    private readonly SceneManager _scenes = new();
    private Automation? _automation;
    private Rectangle _viewport;

    public AbbildGame(LaunchOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            HardwareModeSwitch = false,
            SynchronizeWithVerticalRetrace = true,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.AllowUserResizing = true;
        Window.Title = "Abbild ～ 古の迷宮と勇者の魂 ～";
        Content.RootDirectory = "Content";
    }

    public Services Shared => _s ?? throw new InvalidOperationException("まだ準備ができていません。");

    public SceneManager Scenes => _scenes;

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Content");
        var store = new SaveStore(_options.DataDir ?? SaveStore.DefaultDirectory());
        var settings = store.LoadSettings();
        var records = store.LoadRecords();

        byte[] font = File.ReadAllBytes(Path.Combine(root, "fonts", "DotGothic16-Regular.ttf"));
        var plain = new FontSystem(new FontSystemSettings { FontResolutionFactor = 1, KernelWidth = 0, KernelHeight = 0 });
        plain.AddFont(font);
        
        var input = new Input();
        Window.TextInput += (_, e) => input.OnTextInput(e.Character);
        var hub = new ControllerHub();
        _s = new Services
        {
            Game = this,
            Gfx = new Gfx(GraphicsDevice, plain),
            Assets = new Assets(GraphicsDevice, root),
            Audio = new Audio(root),
            Input = input,
            Controller = hub,
            Body = new BodyInput(input, hub),
            Store = store,
            Settings = settings,
            Records = records,
        };
        _s.ApplyAudioSettings();
        _target = new RenderTarget2D(GraphicsDevice, Gfx.Width, Gfx.Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        if (_options.SnapshotDir is not null || _options.AutoPlay)
        {
            _automation = new Automation(_s, _scenes, _options);
            _automation.Start();
        }
        else
        {
            SetFullscreen(settings.Fullscreen && !_options.Windowed);
            hub.Connect(settings);
            _scenes.Go(new TitleScene(_s));
        }
    }

    public void SetFullscreen(bool on)
    {
        if (on)
        {
            var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.PreferredBackBufferWidth = dm.Width;
            _graphics.PreferredBackBufferHeight = dm.Height;
            _graphics.IsFullScreen = true;
        }
        else
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = 1600;
            _graphics.PreferredBackBufferHeight = 900;
        }
        _graphics.ApplyChanges();
    }

    public bool IsFullscreen => _graphics.IsFullScreen;

    protected override void Update(GameTime gameTime)
    {
        var s = Shared;
        float dt = (float)Math.Min(0.1, gameTime.ElapsedGameTime.TotalSeconds);
        if (_automation is not null) dt = 1f / 60f;
        s.Input.SetViewport(_viewport);
        _automation?.BeforeUpdate(dt);
        s.Input.Update(dt);
        if (s.Input.ToggleFullscreen)
        {
            SetFullscreen(!IsFullscreen);
            s.Settings.Fullscreen = IsFullscreen;
            s.SaveSettings();
        }
        s.Controller.Update(dt);
        s.Audio.Update(dt);
        _scenes.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var s = Shared;
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(Color.Black);
        _scenes.Draw(s.Gfx);
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        var pp = GraphicsDevice.PresentationParameters;
        float scale = Math.Min(pp.BackBufferWidth / (float)Gfx.Width, pp.BackBufferHeight / (float)Gfx.Height);
        int w = (int)(Gfx.Width * scale), h = (int)(Gfx.Height * scale);
        _viewport = new Rectangle((pp.BackBufferWidth - w) / 2, (pp.BackBufferHeight - h) / 2, w, h);
        var shake = _scenes.Current?.Shake ?? Vector2.Zero;
        var dest = _viewport;
        dest.Offset((int)(shake.X * scale), (int)(shake.Y * scale));
        s.Gfx.Batch.Begin(samplerState: SamplerState.LinearClamp);
        s.Gfx.Batch.Draw(_target, dest, Color.White);
        s.Gfx.Batch.End();

        _automation?.AfterDraw(_target!);
        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _s?.Controller.Dispose();
        _s?.Audio.Dispose();
        base.OnExiting(sender, args);
    }
}
