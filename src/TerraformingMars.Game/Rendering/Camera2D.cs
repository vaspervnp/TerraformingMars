using Microsoft.Xna.Framework;

namespace TerraformingMars.Game.Rendering;

/// <summary>
/// Απλή 2D κάμερα: κρατά ένα world-point στο κέντρο της οθόνης + zoom.
/// Παρέχει view matrix για το rendering και screen↔world για mouse picking.
/// </summary>
public sealed class Camera2D
{
    public Vector2 Position { get; set; }       // world point στο κέντρο της οθόνης
    public float Zoom { get; set; } = 1f;
    public float MinZoom { get; set; } = 0.12f;
    public float MaxZoom { get; set; } = 6f;

    private int _viewportWidth = 1;
    private int _viewportHeight = 1;

    public void SetViewport(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    public Matrix GetViewMatrix() =>
        Matrix.CreateTranslation(new Vector3(-Position, 0f)) *
        Matrix.CreateScale(Zoom, Zoom, 1f) *
        Matrix.CreateTranslation(new Vector3(_viewportWidth * 0.5f, _viewportHeight * 0.5f, 0f));

    public Vector2 ScreenToWorld(Vector2 screen) =>
        Vector2.Transform(screen, Matrix.Invert(GetViewMatrix()));

    /// <summary>Pan με βάση μετατόπιση ποντικιού σε pixels (διαιρείται με zoom → world units).</summary>
    public void Pan(Vector2 screenDelta) => Position -= screenDelta / Zoom;

    /// <summary>Zoom γύρω από ένα σημείο της οθόνης (μένει ακίνητο κάτω από τον κέρσορα).</summary>
    public void ZoomAt(Vector2 screenAnchor, float factor)
    {
        Vector2 before = ScreenToWorld(screenAnchor);
        Zoom = MathHelper.Clamp(Zoom * factor, MinZoom, MaxZoom);
        Vector2 after = ScreenToWorld(screenAnchor);
        Position += before - after;
    }
}
