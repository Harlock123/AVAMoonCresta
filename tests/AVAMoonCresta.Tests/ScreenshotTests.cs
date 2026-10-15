using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMoonCresta.Engine;
using AVAMoonCresta.Views;
using Controls = AVAMoonCresta.Engine.Controls;

namespace AVAMoonCresta.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set CRESTA_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamooncresta-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("CRESTA_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMoonCresta-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }


    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 64250, 12);
        g.HighScores.Insert("AVA", 41800, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave played by the computer until it's busy, with plenty of stages to spare.</summary>
    private static void Wave(int wave, string name, Func<GameEngine, bool> busy, int seed = 2, int[]? stages = null)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetStages(stages ?? [1]);
        g.SetReserve(1, 2, 3, 1, 2, 3);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.WaveTime > 3 && g.Rocket.Alive && g.Rocket.Safe <= 0 && busy(g), 90));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void Gogglers() => Wave(1, "gogglers", g => g.Aliens.Count >= 6 && g.Shots.Count > 0);

    [AvaloniaFact]
    public void Splitters() => Wave(3, "splitters", g => g.Aliens.Count(a => a.Small) >= 2 && g.Aliens.Count(a => !a.Small) >= 3, stages: [1, 2]);

    [AvaloniaFact]
    public void Zippers() => Wave(5, "zippers", g => g.Kind == AlienKind.Zipper && g.Aliens.Count >= 5 && g.Bombs.Count > 0 && g.Shots.Count > 0, stages: [1, 2, 3]);

    [AvaloniaFact]
    public void Meteors() => Wave(7, "meteors", g => g.Kind == AlienKind.Meteor && g.Aliens.Count(a => a.Y > 150) >= 4, stages: [1, 2, 3]);

    [AvaloniaFact]
    public void Docking()
    {
        var (w, c, g) = Open(3);
        g.Go(2);
        g.GoDock();
        Assert.True(PlayUntil(g, c, g => g.Dock is { Done: false } d && d.Y > 420 && d.Thrusting, 30));
        Shot(w, c, "docking");
        Assert.True(PlayUntil(g, c, g => g.Dock is { Done: true } && g.PhaseTime > 0.3, 30));
        Shot(w, c, "docked");
    }

    [AvaloniaFact]
    public void A_stage_lost()
    {
        var (w, c, g) = Open(5);
        g.Go(2);
        g.SetStages(1, 2);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 4 && g.Rocket.Alive && g.Rocket.Safe <= 0 && g.Aliens.Count >= 4));
        g.PutBomb(g.Rocket.X - 16, Field.Base - 40);
        g.Idle(8);
        Assert.Single(g.Rocket.Stages);
        c.Advance(0.2);
        Shot(w, c, "stage-lost");
    }
}
