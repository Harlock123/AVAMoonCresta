namespace AVAMoonCresta.Engine;

/// <summary>Left and right, and the fire button (which is also the thrust when docking).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The screen, in pixels: the battle is fought in the column between <see cref="Left"/> and <see cref="Right"/>,
/// with the rocket sliding along the bottom, its lowest stage resting on <see cref="Base"/>.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 192, Right = 832, Top = 70, Bottom = 768;
    public const double CentreX = (Left + Right) / 2;
    /// <summary>The bottom of the rocket's lowest stage.</summary>
    public const double Base = 742;
}

/// <summary>The three stages of the rocket: how big each is, and where its guns are.</summary>
public static class Stage
{
    public static double Width(int stage) => stage switch { 1 => 26, 2 => 42, _ => 56 };
    public static double Height(int stage) => stage switch { 1 => 34, 2 => 26, _ => 28 };

    /// <summary>Where each gun sits across the stage, from its middle.</summary>
    public static double[] Guns(int stage) => stage switch { 1 => [0], 2 => [-16, 16], _ => [-23, 23] };

    public static string Name(int stage) => stage switch { 1 => "STAGE I", 2 => "STAGE II", _ => "STAGE III" };
}

/// <summary>The rocket: one, two or three stages stacked, the smallest on top.</summary>
public sealed class Rocket
{
    public double X = Field.CentreX;
    public bool Alive = true;
    public double Safe;
    /// <summary>The stages, top to bottom.</summary>
    public List<int> Stages { get; } = [1];

    public double Height => Stages.Sum(Stage.Height);

    /// <summary>Where a stage is: its middle across, and its top and bottom (resting on <paramref name="baseY"/>).</summary>
    public (double Top, double Bottom) Span(int index, double baseY = Field.Base)
    {
        double bottom = baseY;
        for (int i = Stages.Count - 1; i > index; i--) bottom -= Stage.Height(Stages[i]);
        return (bottom - Stage.Height(Stages[index]), bottom);
    }
}

/// <summary>A shot from one of the rocket's guns; each stage has one volley in the air at a time.</summary>
public sealed class Shot
{
    public double X, Y;
    public int Stage;
    public bool Dead;
}

public sealed class Bomb
{
    public double X, Y, Vx, Vy;
    public bool Dead;
}

public enum AlienKind
{
    /// <summary>Goggle-eyed things that drift and bob from side to side, and swoop when they're low.</summary>
    Goggler,
    /// <summary>A great bird that splits in two when it's hit; the halves dive at the rocket.</summary>
    Splitter,
    /// <summary>Little sparks that zip about in straight dashes, changing direction all the time.</summary>
    Zipper,
    /// <summary>A rock falling through: shots don't touch it; there's nothing for it but to get out of the way.</summary>
    Meteor,
}

public sealed class Alien
{
    public AlienKind Kind;
    public double X, Y, Vx, Vy, T, Turn, Phase, DropIn = 3;
    public int Hp = 1;
    /// <summary>One half of a split bird; a Goggler swooping at the rocket.</summary>
    public bool Small, Swooping, Dead;
    public double Hurt;

    public double Radius => Kind switch
    {
        AlienKind.Meteor => 24,
        AlienKind.Splitter => Small ? 12 : 20,
        AlienKind.Zipper => 10,
        _ => 15,
    };
}

/// <summary>The docking: the rocket coming down under its own thrust onto the next stage, waiting below.</summary>
public sealed class Docking
{
    public const double Gravity = 70, Thrust = 190, Across = 140, Limit = 25, GoodDown = 75, GoodAcross = 7;
    /// <summary>The waiting stage, and where it is.</summary>
    public int Stage;
    public double StageX;
    /// <summary>The rocket: across, the bottom of its lowest stage, and how fast it's coming down.</summary>
    public double X, Y, Vy;
    public double Time;
    public bool Thrusting, Done;
}

/// <summary>A fleck of something blown up, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
