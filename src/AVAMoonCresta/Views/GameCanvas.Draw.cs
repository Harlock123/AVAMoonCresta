using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMoonCresta.Engine;
using Effect = AVAMoonCresta.Engine.Effect;

namespace AVAMoonCresta.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMoonCresta/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private const uint Hull = 0xFFF0F2FA, RocketRed = 0xFFE83A4A, RocketBlue = 0xFF3A7AE8, Gold = 0xFFFFD84A, Moon = 0xFFE8E0B0;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        if (InGame)
        {
            using (dc.PushClip(new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top)))
            {
                if (g.Phase == Phase.Dock && g.Dock is { } d) DrawDocking(dc, d);
                else
                {
                    foreach (var a in g.Aliens) DrawAlien(dc, a.Kind, a.X, a.Y, a.T, a.Small, a.Hurt > 0, a.Phase, 1);
                    foreach (var b in g.Bombs)
                    {
                        dc.FillRectangle(Brush(Alpha(Red, 0.35)), new Rect(b.X - 4, b.Y - 9, 8, 18));
                        dc.FillRectangle(Brush((int)(_clock * 20) % 2 == 0 ? 0xFFFFE0E0 : Red), new Rect(b.X - 1.5, b.Y - 7, 3, 14));
                    }
                    foreach (var s in g.Shots)
                    {
                        dc.FillRectangle(Brush(Alpha(Yellow, 0.3)), new Rect(s.X - 3, s.Y - 2, 6, 18));
                        dc.FillRectangle(Brush(0xFFFFFFE0), new Rect(s.X - 1.5, s.Y, 3, 13));
                    }
                    var r = g.Rocket;
                    if (r.Alive && (r.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawRocket(dc, r.Stages, r.X, Field.Base, 1, flame: true);
                }
                foreach (var d2 in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d2.Age / d2.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d2.Colour, a)), new Rect(d2.X - 2, d2.Y - 2, 4, 4));
                }
            }
            DrawPanels(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>Space with its stars falling past, and the moon, a great crescent, hanging over the battle.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF060812), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame) dc.FillRectangle(Brush(0xFF000004), new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top));
        double left = InGame ? Field.Left : 0, width = InGame ? Field.Right - Field.Left : Field.Width;
        // The crescent: a pale disc with a darker one bitten out of it.
        var mc = InGame ? new Point(left + width * 0.78, 180) : new Point(860, 300);
        dc.DrawEllipse(Brush(Alpha(Moon, 0.14)), null, mc, 74, 74);
        dc.DrawEllipse(Brush(Alpha(Moon, 0.55)), null, mc, 60, 60);
        dc.DrawEllipse(Brush(InGame ? 0xFF000004 : 0xFF060812), null, new Point(mc.X + 24, mc.Y - 12), 54, 54);
        uint[] colours = [0xFFFFFFFF, 0xFFFFE0A0, 0xFFA0C8FF, 0xFFFFA0D0];
        for (int k = 0; k < 110; k++)
        {
            double x = left + Hash(k, 1, 7) % (uint)width;
            double y = (Hash(k, 3, 7) % 768 + _clock * (30 + k % 3 * 25)) % 768;
            if (InGame && y < Field.Top) continue;
            dc.FillRectangle(Brush(Alpha(colours[k % colours.Length], 0.5 + 0.3 * (k % 3))), new Rect(x, y, 2, 2));
        }
        if (InGame)
        {
            dc.DrawLine(Pen(0xFF2A3050, 2), new Point(Field.Left, Field.Top), new Point(Field.Left, Field.Bottom));
            dc.DrawLine(Pen(0xFF2A3050, 2), new Point(Field.Right, Field.Top), new Point(Field.Right, Field.Bottom));
        }
    }

    // ───────────────────────────── the rocket ─────────────────────────────

    /// <summary>The rocket's stages stacked up from <paramref name="baseY"/>, the smallest on top.</summary>
    private void DrawRocket(DrawingContext dc, IReadOnlyList<int> stages, double x, double baseY, double k, bool flame)
    {
        double bottom = baseY;
        for (int i = stages.Count - 1; i >= 0; i--)
        {
            DrawStage(dc, stages[i], x, bottom, k, flame && i == stages.Count - 1);
            bottom -= Stage.Height(stages[i]) * k;
        }
    }

    /// <summary>
    /// One stage, its bottom at <paramref name="bottom"/>: the first a slim nose with a red tip; the second a
    /// broader body with two guns; the third the widest, with great fins, two guns and the main engine.
    /// </summary>
    private void DrawStage(DrawingContext dc, int stage, double x, double bottom, double k, bool flame)
    {
        double h = Stage.Height(stage) * k, w = Stage.Width(stage) * k / 2, top = bottom - h;
        Point P(double px, double py) => new(x + px * k, top + py * k);
        if (flame)
        {
            double f = (8 + 5 * Math.Sin(_clock * 38)) * k;
            Poly(dc, Fire, null, 0, new Point(x - 6 * k, bottom), new Point(x, bottom + f), new Point(x + 6 * k, bottom));
        }
        switch (stage)
        {
            case 1:
                Poly(dc, Hull, 0xFF6A7088, 1, P(0, 0), P(7, 12), P(7, 30), P(-7, 30), P(-7, 12));
                Poly(dc, RocketRed, null, 0, P(0, 0), P(4, 7), P(-4, 7));
                Poly(dc, RocketRed, null, 0, P(-7, 20), P(-13, 34), P(-7, 34));
                Poly(dc, RocketRed, null, 0, P(7, 20), P(13, 34), P(7, 34));
                dc.DrawEllipse(Brush(RocketBlue), null, P(0, 16), 3 * k, 4 * k);
                break;
            case 2:
                Poly(dc, Hull, 0xFF6A7088, 1, P(-10, 0), P(10, 0), P(14, 26), P(-14, 26));
                foreach (int s in new[] { -1, 1 })
                {
                    Poly(dc, RocketBlue, null, 0, P(s * 14, 8), P(s * 21, 14), P(s * 21, 26), P(s * 14, 26));
                    dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(x + (s * 16 - 1.5) * k, top - 4 * k, 3 * k, 12 * k));
                }
                dc.FillRectangle(Brush(RocketRed), new Rect(x - 12 * k, top + 12 * k, 24 * k, 4 * k));
                break;
            default:
                Poly(dc, Hull, 0xFF6A7088, 1, P(-13, 0), P(13, 0), P(16, 28), P(-16, 28));
                foreach (int s in new[] { -1, 1 })
                {
                    Poly(dc, RocketRed, null, 0, P(s * 16, 4), P(s * 28, 20), P(s * 28, 28), P(s * 16, 28));
                    dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(x + (s * 23 - 1.5) * k, top - 2 * k, 3 * k, 14 * k));
                }
                dc.FillRectangle(Brush(RocketBlue), new Rect(x - 13 * k, top + 6 * k, 26 * k, 5 * k));
                dc.FillRectangle(Brush(RocketBlue), new Rect(x - 14 * k, top + 16 * k, 28 * k, 3 * k));
                break;
        }
    }

    /// <summary>The docking: the waiting stage below, the rocket coming down onto it, and the guide between.</summary>
    private void DrawDocking(DrawingContext dc, Docking d)
    {
        var g = Engine;
        // The landing ground and the waiting stage.
        dc.FillRectangle(Brush(0xFF1A1A2A), new Rect(Field.Left, Field.Base, Field.Right - Field.Left, Field.Bottom - Field.Base));
        dc.DrawLine(Pen(0xFF4A4A6A, 2), new Point(Field.Left, Field.Base), new Point(Field.Right, Field.Base));
        bool joined = d.Done && g.Rocket.Stages.Contains(d.Stage);
        if (!joined && (!d.Done || g.Reserve.Contains(d.Stage))) DrawStage(dc, d.Stage, d.StageX, Field.Base, 1, false);
        // A guide line up from the waiting stage, green when the rocket is lined up.
        bool lined = Math.Abs(d.X - d.StageX) <= Docking.GoodAcross;
        uint guide = lined ? 0xFF6AFF8A : 0xFF6A6A8A;
        for (double y = Field.Top + 20; y < Field.Base - 40; y += 16)
            dc.DrawLine(Pen(Alpha(guide, 0.5), 1.5), new Point(d.StageX, y), new Point(d.StageX, y + 8));
        var stages = joined ? g.Rocket.Stages : g.Rocket.Stages.ToList();
        if (joined) DrawRocket(dc, stages, d.X, Field.Base, 1, false);
        else DrawRocket(dc, stages, d.X, d.Y, 1, false);
        if (d.Thrusting && !d.Done)
        {
            double f = 14 + 8 * Math.Sin(_clock * 40);
            Poly(dc, Fire, null, 0, new Point(d.X - 8, d.Y), new Point(d.X, d.Y + f), new Point(d.X + 8, d.Y));
        }
        if (!d.Done)
        {
            bool slow = d.Vy <= Docking.GoodDown;
            Text(dc, $"DOWN {d.Vy,4:0}", new Point(Field.Left + 20, Field.Top + 30), 26, slow ? 0xFF6AFF8A : Red);
            Text(dc, $"ACROSS {Math.Abs(d.X - d.StageX),4:0}", new Point(Field.Left + 20, Field.Top + 58), 26, lined ? 0xFF6AFF8A : Red);
            Text(dc, $"TIME {Math.Max(0, Docking.Limit - d.Time),3:0}", new Point(Field.Right - 20, Field.Top + 30), 26, Yellow, right: true);
        }
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    private void DrawAlien(DrawingContext dc, AlienKind kind, double x, double y, double t, bool small, bool hurt, double spin, double k)
    {
        Point P(double px, double py) => new(x + px * k, y + py * k);
        uint col = GameEngine.Colour(kind);
        switch (kind)
        {
            case AlienKind.Goggler:
            {
                // A round green body with two great eyes that follow the rocket, and little feet paddling.
                double look = Math.Atan2(Field.Base - y, Engine.Rocket.X - x);
                dc.DrawEllipse(Brush(col), Pen(0xFF1A5A2A, 1.2), P(0, 2), 14 * k, 11 * k);
                foreach (int s in new[] { -1, 1 })
                {
                    dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(0xFF1A5A2A, 1), P(s * 6, -4), 6.5 * k, 7 * k);
                    dc.DrawEllipse(Brush(0xFF101030), null, P(s * 6 + Math.Cos(look) * 2.5, -4 + Math.Sin(look) * 3), 2.6 * k, 2.6 * k);
                    double paddle = Math.Sin(t * 10 + s) * 3;
                    dc.DrawLine(Pen(col, 2.5 * k), P(s * 7, 11), P(s * 10 + paddle, 16));
                }
                break;
            }
            case AlienKind.Splitter when !small:
            {
                // A great pink bird, wings beating.
                double flap = Math.Sin(t * 8) * 8;
                foreach (int s in new[] { -1, 1 })
                    Poly(dc, Alpha(col, 0.95), 0xFF6A1A4A, 1, P(s * 4, -2), P(s * 24, -8 - flap), P(s * 22, 4 - flap / 2), P(s * 6, 8));
                Poly(dc, 0xFFFFC0E0, 0xFF6A1A4A, 1, P(0, -12), P(6, -2), P(5, 12), P(0, 16), P(-5, 12), P(-6, -2));
                Poly(dc, Yellow, null, 0, P(-3, -12), P(0, -18), P(3, -12));
                dc.DrawEllipse(Brush(0xFF101030), null, P(-2.5, -6), 1.8 * k, 1.8 * k);
                dc.DrawEllipse(Brush(0xFF101030), null, P(2.5, -6), 1.8 * k, 1.8 * k);
                break;
            }
            case AlienKind.Splitter:
            {
                // Half a bird: smaller, angrier, and quick.
                double flap = Math.Sin(t * 14) * 5;
                Poly(dc, 0xFFFF4A9A, 0xFF6A1A4A, 1, P(0, -9), P(12, -5 - flap), P(5, 3), P(0, 10), P(-5, 3), P(-12, -5 - flap));
                dc.DrawEllipse(Brush(Yellow), null, P(0, -2), 2.2 * k, 2.2 * k);
                break;
            }
            case AlienKind.Zipper:
            {
                // A crackling spark, its arcs flicking about.
                for (int i = 0; i < 4; i++)
                {
                    double a = t * 9 + i * Math.PI / 2 + Hash(i, (int)(t * 12), 3) % 10 * 0.05;
                    var p1 = P(Math.Cos(a) * 5, Math.Sin(a) * 5);
                    var p2 = P(Math.Cos(a + 0.4) * 11, Math.Sin(a + 0.4) * 11);
                    dc.DrawLine(Pen(Alpha(col, 0.5), 4 * k), p1, p2);
                    dc.DrawLine(Pen(0xFFE0FFFF, 1.6 * k), p1, p2);
                }
                dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(col, 2), P(0, 0), 4 * k, 4 * k);
                break;
            }
            default:
            {
                // A tumbling rock with craters.
                uint rock = hurt ? 0xFFFFFFFF : col;
                var pts = Enumerable.Range(0, 9).Select(i =>
                {
                    double a = spin + i * Math.PI * 2 / 9, rr = 24 * (0.8 + Hash(i, 5, 9) % 10 * 0.03);
                    return P(Math.Cos(a) * rr, Math.Sin(a) * rr);
                }).ToArray();
                Poly(dc, rock, 0xFF5A4030, 2, pts);
                dc.DrawEllipse(Brush(0xFF8A6A4A), null, P(Math.Cos(spin) * 8, Math.Sin(spin) * 8), 6 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFF8A6A4A), null, P(Math.Cos(spin + 2.5) * 10, Math.Sin(spin + 2.5) * 9), 4 * k, 4 * k);
                break;
            }
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFF0C0 : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"WAVE {g.Wave}", new Point(Field.Width / 2, 36), 38, White, center: true);
    }

    /// <summary>The stages waiting in reserve on the left; the wave, and what's worth what, on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = Field.Left / 2, rx = Field.Right + (Field.Width - Field.Right) / 2;
        Text(dc, "RESERVE", new Point(lx, 120), 30, Dim, center: true);
        for (int i = 0; i < Math.Min(g.Reserve.Count, 6); i++)
        {
            double y = 190 + i * 64;
            DrawStage(dc, g.Reserve[i], lx - 30, y, 1, false);
            Text(dc, Stage.Name(g.Reserve[i]).Replace("STAGE ", ""), new Point(lx + 34, y - 14), 26, White, center: true);
        }
        if (g.Reserve.Count == 0) Text(dc, "NONE LEFT", new Point(lx, 170), 24, Red, center: true);
        Text(dc, "THE ROCKET", new Point(lx, 590), 26, Dim, center: true);
        DrawRocket(dc, g.Rocket.Stages, lx, 700, 1, false);

        Text(dc, GameEngine.NameOf(g.Kind), new Point(rx, 120), 28, GameEngine.Colour(g.Kind), center: true);
        Text(dc, $"ROUND {g.Round}", new Point(rx, 150), 24, Dim, center: true);
        (AlienKind Kind, bool Small, string Points)[] rows =
        [
            (AlienKind.Goggler, false, "30 / 60"),
            (AlienKind.Splitter, false, "50"),
            (AlienKind.Splitter, true, "100"),
            (AlienKind.Zipper, false, "80"),
            (AlienKind.Meteor, false, "DODGE"),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            double y = 210 + i * 58;
            DrawAlien(dc, rows[i].Kind, rx - 50, y, _clock + i, rows[i].Small, false, _clock * 0.5, rows[i].Kind == AlienKind.Meteor ? 0.8 : 1);
            Text(dc, rows[i].Points, new Point(rx + 40, y), 24, White, center: true);
        }
        Text(dc, "DOCKING", new Point(rx, 520), 26, Gold, center: true);
        Text(dc, $"PERFECT {GameEngine.PerfectDock}", new Point(rx, 550), 22, White, center: true);
        Text(dc, g.DockAfter && g.Reserve.Count > 0 ? "AFTER THIS WAVE" : "", new Point(rx, 578), 22, Yellow, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = g.Phase == Phase.Dock ? 200 : 440;
        var (w, h) = (Math.Min(Field.Right - Field.Left, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 44 : 56, g.Phase == Phase.Dying || msg.Contains("LOST") || msg.Contains("FAIL") ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), sub.Length > 30 ? 22 : 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA MOON CRESTA", new Point(cx, 108), 104, Gold, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 174), 28, Dim, center: true);
        // The rocket, its three stages pulling apart and coming together.
        double gap = 14 * (1 + Math.Sin(_clock * 1.5));
        DrawStage(dc, 3, cx, 330, 1.4, true);
        DrawStage(dc, 2, cx, 330 - 28 * 1.4 - gap, 1.4, false);
        DrawStage(dc, 1, cx, 330 - 54 * 1.4 - gap * 2, 1.4, false);
        Text(dc, "I", new Point(cx + 60, 330 - 54 * 1.4 - gap * 2 - 24), 26, White, center: true);
        Text(dc, "II", new Point(cx + 70, 330 - 28 * 1.4 - gap - 18), 26, White, center: true);
        Text(dc, "III", new Point(cx + 80, 330 - 20), 26, White, center: true);
        Text(dc, "DOCK THE STAGES FOR MORE GUNS - EACH ONE YOU LOSE IS GONE", new Point(cx, 378), 24, White, center: true);
        (AlienKind Kind, bool Small, string Name, string Pts)[] roster =
        [
            (AlienKind.Goggler, false, "GOGGLER", "30-60"),
            (AlienKind.Splitter, false, "SPLITTER", "50"),
            (AlienKind.Splitter, true, "ITS HALVES", "100"),
            (AlienKind.Zipper, false, "ZIPPER", "80"),
            (AlienKind.Meteor, false, "METEOR", "DODGE IT"),
        ];
        for (int i = 0; i < roster.Length; i++)
        {
            double x = cx - 340 + i * 170;
            DrawAlien(dc, roster[i].Kind, x, 450, _clock + i, roster[i].Small, false, _clock * 0.6, 1.4);
            Text(dc, roster[i].Name, new Point(x, 498), 24, GameEngine.Colour(roster[i].Kind), center: true);
            Text(dc, roster[i].Pts, new Point(x, 524), 22, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: FLY    SPACE: FIRE (AND THRUST WHEN DOCKING)    P: PAUSE    M: SOUND", new Point(cx, 660), 24, Dim, center: true);
        Text(dc, $"A SPARE STAGE AT {GameEngine.ExtraStageAt} AND EVERY {GameEngine.ExtraStageEvery}", new Point(cx, 694), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "MOON PILOTS", new Point(cx, 130), 72, Gold, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
