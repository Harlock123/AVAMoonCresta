using AVAMoonCresta.Engine;

namespace AVAMoonCresta.Tests;

/// <summary>How the computer pilot gets on, wave by wave (set CRESTA_PROBE to run it, for tuning; CRESTA_PROBE=lazy for one that just sits and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("CRESTA_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "cresta-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetReserve(1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3); g.Reserve.Sort();
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase; string docks = "";
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 14 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Fire: t % 20 < 10) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if ((g.Phase == Phase.Dying && last != Phase.Dying) || (g.Message?.EndsWith("LOST") == true && g.MessageTime > 1.48)) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
                if (g.Phase == Phase.Dock && g.Dock is { Done: true } dk && g.PhaseTime < 0.02) docks += g.Message is { } m ? m[0] : '?';
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   stages {string.Join("", g.Rocket.Stages)} reserve {string.Join("", g.Reserve)} docks {docks}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
