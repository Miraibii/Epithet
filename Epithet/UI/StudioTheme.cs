using System.Numerics;

namespace Epithet.Windows;

/// <summary>Single visual palette shared by every custom-drawn page.</summary>
public sealed partial class DashboardWindow
{
    // Animated alpha shared by Epithet's own draw-list primitives during page entrance.
    private static float PageVisualAlpha = 1f;
    private static readonly Vector4 Bg = new(.045f, .046f, .054f, .98f);
    private static readonly Vector4 Nav = new(.068f, .065f, .062f, .98f);
    private static readonly Vector4 Panel = new(.105f, .098f, .085f, 1f);
    private static readonly Vector4 Selected = new(.25f, .20f, .123f, 1f);
    private static readonly Vector4 Muted = new(.69f, .69f, .69f, 1f);
    private static readonly Vector4 Mint = new(.95f, .74f, .38f, 1f);
    private static readonly Vector4 Blue = new(.93f, .79f, .53f, 1f);
    private static readonly Vector4 White = new(.96f, .95f, .91f, 1f);
    private static readonly Vector4 Warning = new(1f, .55f, .51f, 1f);

    private static readonly Vector4 GoldLine = new(.33f, .28f, .20f, .85f);
    private static readonly Vector4 DeepGold = new(.16f, .13f, .095f, 1f);
    private static readonly Vector4 Surface2 = new(.16f, .14f, .11f, 1f);
    private static readonly Vector4 Green = new(.48f, .87f, .60f, 1f);

}
