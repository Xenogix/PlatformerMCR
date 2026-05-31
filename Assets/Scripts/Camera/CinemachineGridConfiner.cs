using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[RequireComponent(typeof(CinemachineCamera))]
[AddComponentMenu("Cinemachine/Grid Confiner Zoom")]
public class CinemachineGridConfiner : CinemachineExtension
{
    public GridLevelLayout Grid;
    public float DesiredOrthographicSize = 10f;
    public float Margin = 0f;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || Grid == null) return;
        if (!state.Lens.Orthographic) return;

        float aspect = state.Lens.Aspect;
        if (aspect <= Epsilon) return;

        // Camera basis (includes the tilt / Dutch of the final view).
        var rot = state.GetFinalOrientation();
        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 fwd = rot * Vector3.forward;
        // If the camera looks parallel to the gameplay plane we can't project onto it.
        if (Mathf.Abs(fwd.z) < Epsilon) return;

        // Footprint of a unit (size = 1) orthographic view projected onto the z plane, measured as
        // half-extents around the view center. The footprint scales linearly with OrthographicSize.
        float hx1 = 0f, hy1 = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                // Corner offset from the camera at size 1: +-aspect along right, +-1 along up.
                Vector3 corner = sx * aspect * right + sy * up;
                // Project the corner along the view direction onto the plane (relative to the center).
                Vector3 p = corner - (corner.z / fwd.z) * fwd;
                hx1 = Mathf.Max(hx1, Mathf.Abs(p.x));
                hy1 = Mathf.Max(hy1, Mathf.Abs(p.y));
            }
        }
        if (hx1 < Epsilon || hy1 < Epsilon) return;

        float gridW = Grid.Size.x * Grid.CellSize;
        float gridH = Grid.Size.y * Grid.CellSize;

        // Largest size whose projected footprint still fits inside the grid (minus margin) on both axes.
        float fitX = (gridW * 0.5f - Margin) / hx1;
        float fitY = (gridH * 0.5f - Margin) / hy1;
        float size = Mathf.Min(DesiredOrthographicSize, fitX, fitY);
        if (size <= Epsilon) return;

        var lens = state.Lens;
        lens.OrthographicSize = size;
        state.Lens = lens;

        float halfX = size * hx1 + Margin;
        float halfY = size * hy1 + Margin;

        // Where the camera currently looks on the gameplay plane (grid's z), projected along the view dir.
        Vector3 origin = Grid.transform.position;
        Vector3 camPos = state.GetCorrectedPosition();
        float k = (origin.z - camPos.z) / fwd.z;
        Vector3 lookOnPlane = camPos + k * fwd;

        // Clamp that look point within the grid rect; if an axis is fully constrained, center on it.
        float minX = origin.x + halfX, maxX = origin.x + gridW - halfX;
        float minY = origin.y + halfY, maxY = origin.y + gridH - halfY;
        float targetX = minX <= maxX ? Mathf.Clamp(lookOnPlane.x, minX, maxX) : origin.x + gridW * 0.5f;
        float targetY = minY <= maxY ? Mathf.Clamp(lookOnPlane.y, minY, maxY) : origin.y + gridH * 0.5f;

        // Moving the camera in XY shifts the look point 1:1 on the plane, so apply the same delta.
        state.PositionCorrection += new Vector3(targetX - lookOnPlane.x, targetY - lookOnPlane.y, 0f);
    }
}
