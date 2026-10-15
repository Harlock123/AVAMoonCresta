namespace AVAMoonCresta.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It slides out from under anything about to hit
/// the rocket; otherwise it lines up whichever gun is nearest with the best target, leading it, and fires.
/// Docking, it lines up over the waiting stage first, then lets the rocket down slower the nearer it gets.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        if (g.Phase == Phase.Dock) return Docking(g);
        var r = g.Rocket;
        if (!r.Alive) return default;
        double half = r.Stages.Max(Stage.Width) / 2, top = Field.Base - r.Height;

        // Anything about to come down on the rocket.
        double danger = double.NaN, soonest = 99;
        void Threat(double x, double y, double vx, double vy, double radius)
        {
            for (double t = 0; t <= 0.7; t += 0.05)
            {
                double px = x + vx * t, py = y + vy * t;
                if (py + radius > top - 6 && py - radius < Field.Base && Math.Abs(px - r.X) < half + radius + 8)
                {
                    if (t < soonest) { soonest = t; danger = px; }
                    return;
                }
            }
        }
        if (r.Safe <= 0.3)
        {
            foreach (var b in g.Bombs) Threat(b.X, b.Y, b.Vx, b.Vy, 4);
            foreach (var a in g.Aliens) Threat(a.X, a.Y, a.Vx, a.Vy * (a.Kind == AlienKind.Meteor ? g.Pace : 1), a.Radius);
        }
        // Something coming straight down onto a gun, with time to shoot it: stand and fire.
        var gunsNow = new List<double>();
        for (int i = 0; i < r.Stages.Count; i++) foreach (double gun in Stage.Guns(r.Stages[i])) gunsNow.Add(r.X + gun);
        bool shootable = g.Aliens.Any(a => a.Kind != AlienKind.Meteor && a.Y < top - 50 && a.Y > Field.Top + 10 &&
                                           gunsNow.Any(x => Math.Abs(a.X - x) < a.Radius) && (top - a.Y) / (GameEngine.ShotSpeed + Math.Max(0, a.Vy)) < soonest - 0.1);
        if (!double.IsNaN(danger) && shootable && !g.Bombs.Any(b => Math.Abs(b.X - r.X) < half + 10 && b.Y > top - 150))
        {
            Doing = "stand";
            return new Controls(0, 0, Fire: _tick % 2 == 0);
        }
        if (!double.IsNaN(danger))
        {
            Doing = "dodge";
            int away = danger > r.X ? -1 : 1;
            if (r.X - half < Field.Left + 40) away = 1;
            if (r.X + half > Field.Right - 40) away = -1;
            return new Controls(away, 0, Fire: _tick % 2 == 0);
        }

        // The best target: low and near; the gun nearest it lined up, leading it.
        double bestCost = double.MaxValue, want = r.X;
        var guns = new List<double>();
        for (int i = 0; i < r.Stages.Count; i++) foreach (double gun in Stage.Guns(r.Stages[i])) guns.Add(gun);
        foreach (var a in g.Aliens)
        {
            if (a.Y < Field.Top + 10 || a.Kind == AlienKind.Meteor) continue;
            double t = Math.Max(0, (top - a.Y) / GameEngine.ShotSpeed);
            // Something homing in will be nearer the rocket by then; anything else goes on as it is.
            double ax = a.Small || a.Swooping ? r.X + (a.X - r.X) * Math.Exp(-2.5 * t) : a.X + a.Vx * t;
            foreach (double gun in guns)
            {
                double need = Math.Clamp(ax - gun, Field.Left + half, Field.Right - half);
                double cost = Math.Abs(need - r.X) + (top - a.Y) * 0.3 - (a.Swooping || a.Small ? 150 : 0);
                if (cost < bestCost) { bestCost = cost; want = need; }
            }
        }
        if (bestCost == double.MaxValue)
        {
            Doing = "wait";
            return new Controls(Math.Abs(Field.CentreX - r.X) > 10 ? Math.Sign(Field.CentreX - r.X) : 0, 0, false);
        }
        Doing = "attack";
        int dx = Math.Abs(want - r.X) > 4 ? Math.Sign(want - r.X) : 0;
        return new Controls(dx, 0, Fire: Math.Abs(want - r.X) < 10);
    }

    private Controls Docking(GameEngine g)
    {
        var d = g.Dock;
        if (d == null || d.Done) return default;
        Doing = "dock";
        double off = d.StageX - d.X;
        int dx = Math.Abs(off) > 1.5 ? Math.Sign(off) : 0;
        double meet = Field.Base - Stage.Height(d.Stage);
        double left = meet - d.Y;
        double wantVy = Math.Clamp(left * 0.45, 22, 140);
        // Not down onto it until it's lined up.
        if (Math.Abs(off) > 3 && left < 120) wantVy = 0;
        return new Controls(dx, 0, Fire: d.Vy > wantVy);
    }
}
