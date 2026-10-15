namespace AVAMoonCresta.Engine;

public sealed partial class GameEngine
{
    public const double RocketSpeed = 330, ShotSpeed = 900, BombSpeed = 300;
    public const double StartSeconds = 2.0, DyingSeconds = 2.2, SafeSeconds = 1.6, DockedSeconds = 1.8;
    public const int ExtraStageAt = 30000, ExtraStageEvery = 50000, PerfectDock = 3000, GoodDock = 1000, StormBonus = 2000;
    /// <summary>The waves go round in sevens: Gogglers, Splitters and Zippers twice each, then a meteor storm.</summary>
    public const int Cycle = 7;

    public static AlienKind KindOf(int wave) => ((wave - 1) % Cycle) switch
    {
        0 or 1 => AlienKind.Goggler,
        2 or 3 => AlienKind.Splitter,
        4 or 5 => AlienKind.Zipper,
        _ => AlienKind.Meteor,
    };

    public static string NameOf(AlienKind k) => k switch
    {
        AlienKind.Goggler => "GOGGLERS",
        AlienKind.Splitter => "SPLITTERS",
        AlienKind.Zipper => "ZIPPERS",
        _ => "METEOR STORM",
    };

    public static int Points(AlienKind k, bool small = false, bool swooping = false) => k switch
    {
        AlienKind.Goggler => swooping ? 60 : 30,
        AlienKind.Splitter => small ? 100 : 50,
        AlienKind.Zipper => 80,
        _ => 0,
    };

    public static uint Colour(AlienKind k) => k switch
    {
        AlienKind.Goggler => 0xFF6AFF8A,
        AlienKind.Splitter => 0xFFFF6AC8,
        AlienKind.Zipper => 0xFF6AE0FF,
        _ => 0xFFC8A070,
    };

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    /// <summary>The rocket's stages flying, and those waiting in reserve: the game lasts as long as there are any.</summary>
    public int Lives => Rocket.Stages.Count + Reserve.Count;
    public AlienKind Kind => KindOf(Wave);
    public int Round => (Wave - 1) / Cycle + 1;
    public Rocket Rocket { get; } = new();
    /// <summary>The stages waiting to be docked (or to fly, once the rocket is lost).</summary>
    public List<int> Reserve { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Bomb> Bombs { get; } = [];
    public List<Alien> Aliens { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public Docking? Dock { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>How many more the wave has still to send.</summary>
    public int ToCome { get; private set; }
    /// <summary>For tests: nothing new comes, swoops or drops bombs by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _spawnIn, _thrustSound;

    public double Pace => Math.Min(1.8, 1 + 0.15 * (Round - 1) + 0.03 * ((Wave - 1) % Cycle));
    public int Remaining => ToCome + Aliens.Count(a => !a.Dead);
    /// <summary>Whether a docking comes after this wave: after the second Gogglers and the second Splitters.</summary>
    public bool DockAfter => (Wave - 1) % Cycle is 1 or 3;
    private int MaxAliens => Kind == AlienKind.Meteor ? 5 + Round : 7 + Round;
    private bool Bombing => Round > 1 || (Wave - 1) % Cycle >= 4;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Wave = 1;
        _nextExtra = ExtraStageAt;
        Rocket.Stages.Clear();
        Rocket.Stages.Add(1);
        Reserve.Clear();
        Reserve.AddRange([2, 3]);
        Rocket.X = Field.CentreX;
        Rocket.Alive = true;
        Dock = null;
        Clear();
        Wreckage.Clear();
        SetPhase(Phase.Start);
    }

    private void Clear()
    {
        Shots.Clear();
        Bombs.Clear();
        Aliens.Clear();
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        WaveTime = 0;
        Clear();
        Dock = null;
        ToCome = Kind switch
        {
            AlienKind.Goggler => 16,
            AlienKind.Splitter => 10,
            AlienKind.Zipper => 16,
            _ => 20,
        } + 2 * (Round - 1);
        _spawnIn = 0.3;
        Rocket.Alive = true;
        Rocket.Safe = 0;
        SetPhase(Phase.Start);
        Say(NameOf(Kind), $"WAVE {wave}", StartSeconds + 0.4);
        Play(Sfx.WaveStart);
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                FlyRocket(c, dt, canFire: false);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                WaveTime += dt;
                FlyRocket(c, dt, canFire: true);
                MoveShots(dt);
                Spawn(dt);
                MoveAliens(dt);
                MoveBombs(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveAliens(dt);
                MoveBombs(dt);
                if (PhaseTime > DyingSeconds) NextRocket();
                break;
            case Phase.Dock:
                FlyDock(c, dt);
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── the rocket ─────────────────────────────

    private void FlyRocket(Controls c, double dt, bool canFire)
    {
        var r = Rocket;
        if (!r.Alive) return;
        r.Safe = Math.Max(0, r.Safe - dt);
        double half = r.Stages.Max(Stage.Width) / 2;
        r.X = Math.Clamp(r.X + Math.Sign(c.Dx) * RocketSpeed * dt, Field.Left + half, Field.Right - half);
        if (!canFire || !c.Fire) return;
        // Each stage fires its own volley, once its last one is gone.
        bool fired = false;
        for (int i = 0; i < r.Stages.Count; i++)
        {
            int s = r.Stages[i];
            if (Shots.Any(x => x.Stage == s)) continue;
            var (top, _) = r.Span(i);
            foreach (double g in Stage.Guns(s)) Shots.Add(new Shot { X = r.X + g, Y = top, Stage = s });
            fired = true;
        }
        if (fired) Play(Sfx.Fire);
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.Y -= ShotSpeed * dt;
            if (s.Y < Field.Top) { s.Dead = true; continue; }
            foreach (var a in Aliens)
            {
                if (a.Dead || Math.Abs(a.X - s.X) > a.Radius + 3 || Math.Abs(a.Y - s.Y) > a.Radius + 10) continue;
                s.Dead = true;
                Hit(a);
                break;
            }
        }
        Shots.RemoveAll(s => s.Dead);
        Aliens.RemoveAll(a => a.Dead);
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    private void Spawn(double dt)
    {
        if (Calm || ToCome <= 0) return;
        _spawnIn -= dt;
        if (_spawnIn > 0 || Aliens.Count >= MaxAliens) return;
        _spawnIn = Math.Max(0.25, (Kind == AlienKind.Meteor ? 0.75 : 0.7) / Pace) * (0.6 + 0.8 * _rng.NextDouble());
        ToCome--;
        double x = Field.Left + 40 + _rng.NextDouble() * (Field.Right - Field.Left - 80);
        Put(Kind, x, Field.Top - 20);
    }

    private Alien Put(AlienKind kind, double x, double y, bool small = false)
    {
        var a = new Alien { Kind = kind, X = x, Y = y, Small = small, Phase = x, T = _rng.NextDouble() * 6, DropIn = 1 + _rng.NextDouble() * 3 };
        switch (kind)
        {
            case AlienKind.Goggler:
                a.Vx = (_rng.Next(2) * 2 - 1) * (90 + 30 * _rng.NextDouble());
                a.Vy = 40;
                break;
            case AlienKind.Meteor:
                a.Vy = 130 + _rng.NextDouble() * 120;
                a.Vx = (_rng.NextDouble() - 0.5) * 50;
                a.Turn = (_rng.NextDouble() - 0.5) * 3;
                break;
            case AlienKind.Zipper:
                a.Turn = 0;
                break;
        }
        Aliens.Add(a);
        return a;
    }

    private void MoveAliens(double dt)
    {
        var r = Rocket;
        double rocketTop = Field.Base - r.Height;
        foreach (var a in Aliens.ToList())
        {
            if (a.Dead) continue;
            a.T += dt;
            a.Hurt = Math.Max(0, a.Hurt - dt);
            switch (a.Kind)
            {
                case AlienKind.Goggler:
                    if (!a.Swooping)
                    {
                        // Drifting and bobbing, bouncing off the sides; low down, it may swoop.
                        a.X += a.Vx * Pace * dt;
                        a.Y += (a.Vy * Pace + Math.Cos(a.T * 3) * 60) * dt;
                        if (!Calm && r.Alive && a.Y > Field.Base - 330 && _rng.NextDouble() < 0.012 * Pace)
                        {
                            a.Swooping = true;
                            a.Vy = 60;
                            Play(Sfx.Swoop);
                        }
                    }
                    else
                    {
                        a.Vy = Math.Min(300 * Pace, a.Vy + 500 * dt);
                        a.Vx += Math.Clamp((r.X - a.X) * 3 - a.Vx, -600 * dt, 600 * dt);
                        a.X += a.Vx * dt;
                        a.Y += a.Vy * dt;
                    }
                    Bounce(a);
                    Wrap(a);
                    break;
                case AlienKind.Splitter when !a.Small:
                    // A great bird gliding down in wide sweeps.
                    a.Y += 45 * Pace * dt;
                    a.X = Math.Clamp(a.Phase + Math.Sin(a.T * 1.3) * 120, Field.Left + 24, Field.Right - 24);
                    a.Vx = Math.Cos(a.T * 1.3) * 1.3 * 120;
                    a.Vy = 45 * Pace;
                    Wrap(a);
                    break;
                case AlienKind.Splitter:
                    // A half: flung apart, then diving at the rocket.
                    a.Vy = Math.Min(280 * Pace, a.Vy + 400 * dt);
                    a.Vx += Math.Clamp((r.X - a.X) * 2.5 - a.Vx, -450 * dt, 450 * dt);
                    a.X += a.Vx * dt;
                    a.Y += a.Vy * dt;
                    Bounce(a);
                    Wrap(a);
                    break;
                case AlienKind.Zipper:
                {
                    // Straight dashes, a new direction every moment, mostly downwards and towards the rocket.
                    a.Turn -= dt;
                    if (a.Turn <= 0)
                    {
                        a.Turn = 0.3 + _rng.NextDouble() * 0.45;
                        double toward = Math.Atan2(rocketTop - a.Y, r.X - a.X);
                        double ang = _rng.NextDouble() < 0.5 ? toward + (_rng.NextDouble() - 0.5) * 1.4 : Math.PI / 2 + (_rng.NextDouble() - 0.5) * 2.6;
                        double speed = (220 + 40 * _rng.NextDouble()) * Pace;
                        a.Vx = Math.Cos(ang) * speed;
                        a.Vy = Math.Sin(ang) * speed;
                        if (a.Y < Field.Top + 40) a.Vy = Math.Abs(a.Vy);
                    }
                    a.X += a.Vx * dt;
                    a.Y += a.Vy * dt;
                    Bounce(a);
                    Wrap(a);
                    break;
                }
                case AlienKind.Meteor:
                    a.X += a.Vx * dt;
                    a.Y += a.Vy * Pace * dt;
                    a.Phase += a.Turn * dt;
                    Bounce(a);
                    // A meteor goes on by; it doesn't come round again.
                    if (a.Y > Field.Bottom + 30) a.Dead = true;
                    break;
            }
            if (!Calm && Bombing && a.Kind is AlienKind.Goggler or AlienKind.Zipper && r.Alive && Phase == Phase.Play && a.Y < rocketTop - 120)
            {
                a.DropIn -= dt;
                if (a.DropIn <= 0 && Math.Abs(a.X - r.X) < 160 && Bombs.Count < 3 + Round)
                {
                    a.DropIn = Math.Max(1.2, 3.2 - 0.3 * Round) * (0.7 + 0.6 * _rng.NextDouble());
                    double t = (Field.Base - a.Y) / BombSpeed;
                    double vx = Math.Clamp((r.X - a.X) / t, -90, 90);
                    Bombs.Add(new Bomb { X = a.X, Y = a.Y + 10, Vx = vx, Vy = BombSpeed });
                    Play(Sfx.Bomb);
                }
            }
        }
        Aliens.RemoveAll(a => a.Dead);
    }

    private static void Bounce(Alien a)
    {
        if (a.X < Field.Left + a.Radius) { a.X = Field.Left + a.Radius; a.Vx = Math.Abs(a.Vx); }
        if (a.X > Field.Right - a.Radius) { a.X = Field.Right - a.Radius; a.Vx = -Math.Abs(a.Vx); }
    }

    /// <summary>Off the bottom, round to the top again.</summary>
    private static void Wrap(Alien a)
    {
        if (a.Y < Field.Bottom + 30) return;
        a.Y = Field.Top - 20;
        if (a.Kind == AlienKind.Goggler)
        {
            a.Swooping = false;
            a.Vy = 40;
            a.Vx = Math.Sign(a.Vx == 0 ? 1 : a.Vx) * 100;
        }
        if (a.Kind == AlienKind.Splitter && a.Small) a.Vy = 60;
    }

    private void MoveBombs(double dt)
    {
        foreach (var b in Bombs)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            if (b.Y > Field.Bottom) b.Dead = true;
        }
        Bombs.RemoveAll(b => b.Dead);
    }

    /// <summary>A shot finds its mark: a meteor shrugs it off, a great bird splits, anything else is destroyed.</summary>
    private void Hit(Alien a)
    {
        if (a.Kind == AlienKind.Meteor)
        {
            a.Hurt = 0.1;
            Play(Sfx.MeteorHit);
            return;
        }
        a.Dead = true;
        int points = Points(a.Kind, a.Small, a.Swooping);
        AddScore(points);
        Shatter(a.X, a.Y, a.Kind == AlienKind.Meteor ? 18 : 10, Colour(a.Kind));
        if (points >= 100) Effects.Add(new Effect { Kind = EffectKind.Score, X = a.X, Y = a.Y - 18, Text = points.ToString(), Colour = points >= 150 ? 0xFFFFE04A : 0xFFFFFFFF });
        if (a.Kind == AlienKind.Splitter && !a.Small)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                var half = Put(AlienKind.Splitter, a.X + s * 12, a.Y, small: true);
                half.Vx = s * 240;
                half.Vy = -80;
            }
            Play(Sfx.Split);
            return;
        }
        Play(a.Kind == AlienKind.Meteor ? Sfx.Meteor : Sfx.Explode);
    }

    private void Shatter(double x, double y, int n, uint colour)
    {
        for (int i = 0; i < n; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2, v = 50 + _rng.NextDouble() * 180;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(ang) * v, Vy = Math.Sin(ang) * v, Life = 0.5 + _rng.NextDouble() * 0.6, Colour = i % 3 == 0 ? 0xFFFFFFFF : colour });
        }
    }

    // ───────────────────────────── hits on the rocket ─────────────────────────────

    /// <summary>Which of the rocket's stages something at a place is touching, if any.</summary>
    public int StageAt(double x, double y, double radius)
    {
        var r = Rocket;
        for (int i = 0; i < r.Stages.Count; i++)
        {
            var (top, bottom) = r.Span(i);
            double half = Stage.Width(r.Stages[i]) / 2;
            if (Math.Abs(x - r.X) < half + radius * 0.8 && y + radius * 0.8 > top && y - radius * 0.8 < bottom) return i;
        }
        return -1;
    }

    private void Hits()
    {
        var r = Rocket;
        if (!r.Alive || r.Safe > 0) return;
        foreach (var b in Bombs)
        {
            int i = StageAt(b.X, b.Y, 4);
            if (i < 0) continue;
            b.Dead = true;
            LoseStage(i, "BOMBED");
            return;
        }
        foreach (var a in Aliens)
        {
            if (a.Dead) continue;
            int i = StageAt(a.X, a.Y, a.Radius);
            if (i < 0) continue;
            if (a.Kind != AlienKind.Meteor) { a.Dead = true; Shatter(a.X, a.Y, 8, Colour(a.Kind)); }
            Aliens.RemoveAll(x => x.Dead);
            LoseStage(i, $"HIT BY {(a.Kind == AlienKind.Meteor ? "A METEOR" : a.Kind == AlienKind.Goggler ? "A GOGGLER" : a.Kind == AlienKind.Zipper ? "A ZIPPER" : "A SPLITTER")}");
            return;
        }
    }

    /// <summary>One stage of the rocket destroyed: the rest fly on; if it was the last, the rocket's gone.</summary>
    private void LoseStage(int index, string cause)
    {
        var r = Rocket;
        Cause = cause;
        var (top, bottom) = r.Span(index);
        Shatter(r.X, (top + bottom) / 2, 22, 0xFFFFE070);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = r.X, Y = (top + bottom) / 2, Life = 0.9, Size = 1.6, Colour = 0xFFFFE070 });
        int lost = r.Stages[index];
        r.Stages.RemoveAt(index);
        if (r.Stages.Count > 0)
        {
            r.Safe = SafeSeconds;
            Say($"{Stage.Name(lost)} LOST", null, 1.5);
            Play(Sfx.StageLost);
            return;
        }
        r.Alive = false;
        Bombs.Clear();
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The next rocket from the reserve, if there's one left.</summary>
    private void NextRocket()
    {
        if (Reserve.Count == 0) { GameOver(); return; }
        var r = Rocket;
        r.Stages.Clear();
        r.Stages.Add(Reserve[0]);
        Reserve.RemoveAt(0);
        r.Alive = true;
        r.Safe = SafeSeconds;
        r.X = Field.CentreX;
        SetPhase(Phase.Play);
        Say($"{Stage.Name(r.Stages[0])} FLIES", null, 1.5);
        if (State == GameState.Playing) Music(true);
    }

    /// <summary>The wave's over once it has sent everything and none is left.</summary>
    private void Progress()
    {
        if (Calm || Remaining > 0) return;
        Play(Sfx.WaveClear);
        if (Kind == AlienKind.Meteor)
        {
            AddScore(StormBonus * Round);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.CentreX, Y = 380, Text = $"STORM SURVIVED +{StormBonus * Round}", Life = 2.2, Colour = 0xFFFFE04A });
        }
        Shots.Clear();
        Bombs.Clear();
        if (DockAfter && Reserve.Count > 0) StartDock();
        else BeginWave(Wave + 1);
    }

    // ───────────────────────────── docking ─────────────────────────────

    private void StartDock()
    {
        Music(false);
        int stage = Reserve[0];
        Dock = new Docking
        {
            Stage = stage,
            StageX = Field.Left + 90 + _rng.NextDouble() * (Field.Right - Field.Left - 180),
            X = Field.CentreX + (_rng.NextDouble() - 0.5) * 300,
            Y = Field.Top + 140,
            Vy = 30,
        };
        SetPhase(Phase.Dock);
        Say("DOCKING", $"BRING THE ROCKET DOWN ONTO {Stage.Name(stage)}", 2.5);
        Play(Sfx.DockStart);
    }

    /// <summary>Thrust against the fall, line up over the waiting stage, and come down onto it gently.</summary>
    private void FlyDock(Controls c, double dt)
    {
        var d = Dock!;
        if (d.Done)
        {
            if (PhaseTime > DockedSeconds)
            {
                Rocket.X = d.X;
                BeginWave(Wave + 1);
            }
            return;
        }
        d.Time += dt;
        d.Thrusting = c.Fire;
        d.Vy += Docking.Gravity * dt;
        if (d.Thrusting)
        {
            d.Vy -= Docking.Thrust * dt;
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        }
        double half = Rocket.Stages.Max(Stage.Width) / 2;
        d.X = Math.Clamp(d.X + Math.Sign(c.Dx) * Docking.Across * dt, Field.Left + half, Field.Right - half);
        d.Y += d.Vy * dt;
        if (d.Y < Field.Top + 60 + Rocket.Height) { d.Y = Field.Top + 60 + Rocket.Height; d.Vy = Math.Max(0, d.Vy); }
        double meet = Field.Base - Stage.Height(d.Stage);
        if (d.Y >= meet)
        {
            d.Y = meet;
            double off = Math.Abs(d.X - d.StageX);
            if (off <= Docking.GoodAcross && d.Vy <= Docking.GoodDown)
            {
                bool perfect = off <= 2.5 && d.Vy <= 40;
                int bonus = perfect ? PerfectDock : GoodDock + (int)Math.Max(0, Docking.Limit - d.Time) * 40;
                AddScore(bonus);
                Rocket.Stages.Add(d.Stage);
                Rocket.Stages.Sort();
                Reserve.Remove(d.Stage);
                Say(perfect ? "PERFECT DOCKING" : "DOCKED", $"BONUS {bonus}", DockedSeconds);
                Play(Sfx.Docked);
            }
            else
            {
                // Too hard, or not lined up: the waiting stage is wrecked.
                Reserve.Remove(d.Stage);
                Shatter(d.StageX, Field.Base - 14, 26, 0xFFFFE070);
                Say("DOCKING FAILED", $"{Stage.Name(d.Stage)} WRECKED", DockedSeconds);
                Play(Sfx.DockCrash);
            }
            d.Done = true;
            d.Vy = 0;
            PhaseTime = 0;
            return;
        }
        if (d.Time > Docking.Limit)
        {
            Say("OUT OF TIME", $"{Stage.Name(d.Stage)} WAITS", DockedSeconds);
            d.Done = true;
            PhaseTime = 0;
        }
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraStageEvery;
            // A spare stage, of whichever kind the rocket's missing.
            int missing = Enumerable.Range(1, 3).FirstOrDefault(s => !Rocket.Stages.Contains(s) && !Reserve.Contains(s));
            if (missing > 0)
            {
                Reserve.Add(missing);
                Reserve.Sort();
                Play(Sfx.ExtraStage);
            }
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Alien PutAlien(AlienKind kind, double x, double y, bool small = false) => Put(kind, x, y, small);
    internal void PutBomb(double x, double y) => Bombs.Add(new Bomb { X = x, Y = y, Vy = BombSpeed });
    internal void SetToCome(int n) => ToCome = n;
    internal void SetStages(params int[] stages) { Rocket.Stages.Clear(); Rocket.Stages.AddRange(stages); }
    internal void SetReserve(params int[] stages) { Reserve.Clear(); Reserve.AddRange(stages); }
    internal void SetScore(int n) => Score = n;
    internal void GoDock() => StartDock();
}
