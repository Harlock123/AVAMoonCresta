using AVAMoonCresta.Engine;

namespace AVAMoonCresta.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamooncresta-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with nothing in it, nothing coming, and nothing swooping or bombing unless a test makes it.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.SetToCome(0);
        return g;
    }

    /// <summary>A real game in play, with plenty of stages in reserve.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetReserve(1, 1, 2, 2, 3, 3, 3);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    /// <summary>An alien straight above a gun, a little way up.</summary>
    private static Alien Above(GameEngine g, AlienKind kind, double up = 260, bool small = false)
    {
        var a = g.PutAlien(kind, g.Rocket.X, Field.Base - g.Rocket.Height - up, small);
        a.Vx = a.Vy = 0;
        return a;
    }

    // ───────────────────────────── the rocket ─────────────────────────────

    [Fact]
    public void The_rocket_slides_along_the_bottom_of_the_column()
    {
        var g = Game();
        g.Run(200, new Controls(Dx: 1));
        Assert.Equal(Field.Right - Stage.Width(1) / 2, g.Rocket.X);
        g.SetStages(1, 2, 3);
        g.Run(200, new Controls(Dx: -1));
        Assert.Equal(Field.Left + Stage.Width(3) / 2, g.Rocket.X);
    }

    [Theory]
    [InlineData(new[] { 1 }, 1)]
    [InlineData(new[] { 1, 2 }, 3)]
    [InlineData(new[] { 1, 2, 3 }, 5)]
    [InlineData(new[] { 2, 3 }, 4)]
    public void Each_stage_fires_its_own_volley_one_at_a_time(int[] stages, int shots)
    {
        var g = Game();
        g.SetStages(stages);
        g.Tick(FireButton);
        Assert.Equal(shots, g.Shots.Count);
        g.Run(5, FireButton);
        Assert.Equal(shots, g.Shots.Count);
        g.Idle(60);
        Assert.Empty(g.Shots);
    }

    [Fact]
    public void A_stage_hit_is_lost_and_the_rest_fly_on()
    {
        var g = Game();
        g.SetStages(1, 2, 3);
        var (top, bottom) = g.Rocket.Span(1);
        g.PutBomb(g.Rocket.X + 16, (top + bottom) / 2);
        g.Tick(default);
        Assert.Equal([1, 3], g.Rocket.Stages);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Rocket.Safe > 0);
        Assert.Equal("BOMBED", g.Cause);
    }

    [Fact]
    public void The_last_stage_lost_the_next_flies_from_the_reserve_and_with_none_left_the_game_ends()
    {
        var g = Game();
        g.SetReserve(3);
        Above(g, AlienKind.Goggler, up: 0);
        g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal([3], g.Rocket.Stages);
        Assert.Empty(g.Reserve);
        g.Idle((int)(GameEngine.SafeSeconds * 60) + 5);
        Above(g, AlienKind.Goggler, up: 0);
        g.Tick(default);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    [Fact]
    public void A_Goggler_is_worth_more_swooping()
    {
        var g = Game();
        Above(g, AlienKind.Goggler);
        g.Tick(FireButton);
        g.Idle(30);
        Assert.Equal(30, g.Score);
        var s = Above(g, AlienKind.Goggler, up: 200);
        s.Swooping = true;
        s.Vy = 0;
        g.Tick(FireButton);
        g.Idle(20);
        Assert.True(s.Dead);
        Assert.Equal(30 + 60, g.Score);
    }

    [Fact]
    public void Low_down_a_Goggler_swoops_at_the_rocket()
    {
        var g = Game();
        g.Calm = false;
        g.SetToCome(0);
        var a = g.PutAlien(AlienKind.Goggler, Field.Left + 60, Field.Base - 320);
        bool swooped = false;
        for (int t = 0; t < 300 && !a.Dead; t++) { g.Tick(default); swooped |= a.Swooping; }
        Assert.True(swooped);
    }

    [Fact]
    public void A_Splitter_splits_in_two_and_its_halves_are_worth_more()
    {
        var g = Game(3);
        Above(g, AlienKind.Splitter);
        g.Tick(FireButton);
        g.Idle(20);
        Assert.Equal(2, g.Aliens.Count(a => a.Small));
        Assert.Equal(50, g.Score);
        var half = g.Aliens.First(a => a.Small);
        g.Aliens.RemoveAll(a => a != half);
        half.X = g.Rocket.X;
        half.Y = 400;
        half.Vx = half.Vy = 0;
        g.Tick(FireButton);
        g.Idle(25);
        Assert.Equal(150, g.Score);
    }

    [Fact]
    public void A_Zipper_keeps_changing_direction()
    {
        var g = Game(5);
        var z = g.PutAlien(AlienKind.Zipper, Field.CentreX, 200);
        var headings = new HashSet<int>();
        for (int t = 0; t < 120; t++)
        {
            g.Tick(new Controls(Dx: t % 60 < 30 ? 1 : -1));
            headings.Add((int)Math.Round(Math.Atan2(z.Vy, z.Vx) * 4));
            if (z.Dead) break;
        }
        Assert.True(headings.Count >= 3);
    }

    [Fact]
    public void Meteors_shrug_off_shots_and_go_on_by()
    {
        var g = Game(7);
        var m = Above(g, AlienKind.Meteor, up: 300);
        g.Tick(FireButton);
        g.Idle(20);
        Assert.False(m.Dead);
        Assert.Equal(0, g.Score);
        m.X = Field.Left + 40;
        m.Vy = 300;
        for (int t = 0; t < 200 && !m.Dead; t++) g.Tick(default);
        Assert.True(m.Dead);
        Assert.DoesNotContain(m, g.Aliens);
    }

    [Fact]
    public void Off_the_bottom_they_come_round_from_the_top()
    {
        var g = Game();
        var a = g.PutAlien(AlienKind.Goggler, Field.Left + 40, Field.Bottom + 25);
        a.Vy = 300;
        a.Vx = 0;
        g.Tick(default);
        Assert.True(a.Y < Field.Top + 10);
    }

    [Fact]
    public void Bombs_fall_from_the_fifth_wave_on()
    {
        foreach (var (wave, bombs) in new[] { (1, false), (5, true) })
        {
            var g = Real(wave);
            bool seen = false;
            for (int t = 0; t < 60 * 25; t++) { g.Tick(new Controls(Dx: t % 120 < 60 ? 1 : -1)); seen |= g.Bombs.Count > 0; }
            Assert.Equal(bombs, seen);
        }
    }

    // ───────────────────────────── waves and docking ─────────────────────────────

    [Theory]
    [InlineData(1, AlienKind.Goggler)]
    [InlineData(3, AlienKind.Splitter)]
    [InlineData(6, AlienKind.Zipper)]
    [InlineData(7, AlienKind.Meteor)]
    [InlineData(8, AlienKind.Goggler)]
    public void The_waves_go_round_in_sevens(int wave, AlienKind kind) => Assert.Equal(kind, GameEngine.KindOf(wave));

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void A_docking_follows_the_second_Gogglers_and_the_second_Splitters(int wave, bool docks)
    {
        var g = Game(wave);
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(docks ? Phase.Dock : Phase.Start, g.Phase);
        // With nothing in reserve, no docking.
        var h = Game(wave);
        h.SetReserve();
        h.Calm = false;
        h.Tick(default);
        Assert.Equal(Phase.Start, h.Phase);
    }

    private static Docking DockAt(GameEngine g, double offset, double vy)
    {
        g.GoDock();
        var d = g.Dock!;
        d.X = d.StageX + offset;
        d.Y = Field.Base - Stage.Height(d.Stage) - 0.2;
        d.Vy = vy;
        return d;
    }

    [Fact]
    public void Docking_the_fall_and_the_thrust()
    {
        var g = Game(2);
        g.GoDock();
        var d = g.Dock!;
        d.Vy = 0;
        g.Idle(60);
        Assert.InRange(d.Vy, 60, 80);
        g.Run(60, FireButton);
        Assert.True(d.Vy < 0);
    }

    [Fact]
    public void A_perfect_docking_joins_the_stage_for_a_big_bonus()
    {
        var g = Game(2);
        DockAt(g, 1, 30);
        g.Tick(default);
        Assert.Equal([1, 2], g.Rocket.Stages);
        Assert.Equal([3], g.Reserve);
        Assert.Equal(GameEngine.PerfectDock, g.Score);
        g.Idle((int)(GameEngine.DockedSeconds * 60) + 5);
        Assert.Equal(3, g.Wave);
    }

    [Fact]
    public void A_good_docking_still_joins()
    {
        var g = Game(2);
        DockAt(g, 6, 60);
        g.Tick(default);
        Assert.Equal([1, 2], g.Rocket.Stages);
        Assert.InRange(g.Score, GameEngine.GoodDock, GameEngine.PerfectDock - 1);
    }

    [Theory]
    [InlineData(0, 120)]
    [InlineData(20, 30)]
    public void A_bad_docking_wrecks_the_waiting_stage(double offset, double vy)
    {
        var g = Game(2);
        DockAt(g, offset, vy);
        g.Tick(default);
        Assert.Equal([1], g.Rocket.Stages);
        Assert.Equal([3], g.Reserve);
        Assert.Equal("DOCKING FAILED", g.Message);
    }

    [Fact]
    public void Out_of_time_the_stage_waits_for_the_next_docking()
    {
        var g = Game(2);
        g.GoDock();
        g.Run((int)(Docking.Limit * 60) + 5, FireButton);
        Assert.Equal("OUT OF TIME", g.Message);
        Assert.Equal([2, 3], g.Reserve);
    }

    [Fact]
    public void Surviving_the_meteor_storm_pays_a_bonus()
    {
        var g = Game(7);
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(GameEngine.StormBonus, g.Score);
        Assert.Equal(8, g.Wave);
    }

    [Fact]
    public void A_spare_stage_at_30000_of_whichever_kind_is_missing()
    {
        var g = Game();
        g.SetReserve(3);
        g.SetScore(29990);
        Above(g, AlienKind.Goggler);
        g.Tick(FireButton);
        g.Idle(30);
        Assert.Equal([2, 3], g.Reserve);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_clears_the_first_waves_and_docks()
    {
        var g = Real();
        for (int i = 0; i < 60 * 180 && g.Wave < 5 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 5, $"reached wave {g.Wave}");
        Assert.True(g.Rocket.Stages.Count >= 2, "docked");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Rocket.X:0.00} {string.Join("", g.Rocket.Stages)} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Run(30, new Controls(Dx: 1));
        double x = g.Rocket.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Rocket.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
