using System.Numerics;

namespace Gleaner.Services;

/// <summary>
/// APCA, the Accessible Perceptual Contrast Algorithm - the contrast measure
/// behind WCAG 3, from <c>https://git.apcacontrast.com/</c>.
///
/// It answers a different question from the WCAG 2.x ratio, and answers it
/// better for this plugin's problem. The 2.x ratio is symmetric: it scores a
/// pair of colours without caring which is the text, so it cannot tell light
/// text on a dark panel from dark text on a light one. Perception is not
/// symmetric - the same luminance gap reads very differently in the two
/// directions - which is why 2.x is known to over-reward mid greys on dark
/// backgrounds and under-reward the light text those backgrounds actually need.
///
/// This matters here because the plugin themes a window eight different ways,
/// four of them dark and four light, from the same code path. A measure that is
/// wrong in one polarity gets one half of them wrong.
///
/// Two differences from the 2.x maths in <c>UiStyle</c> are easy to miss:
/// the transfer curve is a plain 2.4 power rather than the piecewise sRGB
/// function, and near-black is soft-clamped, because the darkest tones flatten
/// out perceptually rather than continuing to separate.
///
/// Pure: no Dalamud, no ImGui, no game state. Every method is a function of its
/// arguments, which is what lets the numbers be checked without a client.
/// </summary>
public static class Apca
{
    // Coefficients and exponents from the APCA-W3 reference (0.1.9 / W3
    // 0.0.98G-4g). They are not independently meaningful and should be taken as
    // one calibrated set - changing any of them individually invalidates the
    // scale rather than adjusting it.
    private const double MainTrc = 2.4;

    private const double RedCoefficient = 0.2126729;
    private const double GreenCoefficient = 0.7151522;
    private const double BlueCoefficient = 0.0721750;

    private const double NormalBackground = 0.56;
    private const double NormalText = 0.57;
    private const double ReverseBackground = 0.65;
    private const double ReverseText = 0.62;

    private const double BlackThreshold = 0.022;
    private const double BlackClamp = 1.414;

    private const double Scale = 1.14;
    private const double LowOffset = 0.027;
    private const double LowClip = 0.1;
    private const double MinimumDelta = 0.0005;

    /// <summary>
    /// Lc 90. Preferred for body text, and what a column of names wants if it
    /// can be had.
    /// </summary>
    public const float PreferredBodyText = 90f;

    /// <summary>
    /// Lc 75. The floor for body text at ordinary sizes - the level below which
    /// a column of small text stops being comfortable to read.
    /// </summary>
    public const float BodyText = 75f;

    /// <summary>
    /// Lc 60. For text that is deliberately secondary. Dimmed text has to stay
    /// readable while still reading as dimmed, and holding it to the body-text
    /// floor would leave it indistinguishable from the text it sits beside.
    /// </summary>
    public const float SecondaryText = 60f;

    /// <summary>
    /// Lc 15. The floor for something non-textual that still has to be
    /// discernible - a control's fill against the panel behind it.
    /// </summary>
    public const float NonText = 15f;

    /// <summary>
    /// Screen luminance for APCA, which is a plain 2.4 power per channel rather
    /// than the piecewise sRGB curve the 2.x maths uses. Not interchangeable
    /// with that one.
    /// </summary>
    public static float Luminance(Vector4 colour) =>
        (float)((RedCoefficient * Channel(colour.X)) +
                (GreenCoefficient * Channel(colour.Y)) +
                (BlueCoefficient * Channel(colour.Z)));

    /// <summary>
    /// Lightness contrast of <paramref name="text"/> on
    /// <paramref name="background"/>, roughly -108 to +106.
    ///
    /// The sign is the polarity, and it is the point of the algorithm: positive
    /// is dark text on a light background, negative is light text on a dark one.
    /// Use <see cref="Contrast"/> when only the strength matters.
    ///
    /// Zero means "too close to score", not "not measured": pairs under the
    /// minimum delta, and pairs the low clip rejects, both land there rather
    /// than returning a small number that would read as a near miss.
    /// </summary>
    public static float Lc(Vector4 text, Vector4 background)
    {
        var textY = SoftClampBlack(Luminance(text));
        var backgroundY = SoftClampBlack(Luminance(background));

        if (Math.Abs(backgroundY - textY) < MinimumDelta)
            return 0f;

        double sapc;
        double output;

        if (backgroundY > textY)
        {
            // Dark text on a light background.
            sapc = (Math.Pow(backgroundY, NormalBackground) - Math.Pow(textY, NormalText)) * Scale;
            output = sapc < LowClip ? 0.0 : sapc - LowOffset;
        }
        else
        {
            // Light text on a dark background, which is not the mirror image of
            // the case above - hence a second pair of exponents.
            sapc = (Math.Pow(backgroundY, ReverseBackground) - Math.Pow(textY, ReverseText)) * Scale;
            output = sapc > -LowClip ? 0.0 : sapc + LowOffset;
        }

        return (float)(output * 100.0);
    }

    /// <summary>
    /// How strongly <paramref name="text"/> reads on
    /// <paramref name="background"/>, polarity discarded, for comparing against
    /// the thresholds above.
    /// </summary>
    public static float Contrast(Vector4 text, Vector4 background) =>
        MathF.Abs(Lc(text, background));

    private static double Channel(float value) => Math.Pow(Math.Clamp(value, 0f, 1f), MainTrc);

    /// <summary>
    /// Lifts the very darkest tones, which stop separating perceptually well
    /// before they reach black. Without it the algorithm reports differences
    /// down there that the eye cannot actually find.
    /// </summary>
    private static double SoftClampBlack(double luminance) =>
        luminance > BlackThreshold
            ? luminance
            : luminance + Math.Pow(BlackThreshold - luminance, BlackClamp);
}
