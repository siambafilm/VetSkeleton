using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Camera cam;

    public Vector3 currentCenter = Vector3.zero;
    public Vector3 targetCenter  = Vector3.zero;
    public float currentDistance = 10f;
    public float targetDistance  = 10f;
    public float yaw   = 0f;
    public float pitch = 20f;

    public float minDistance      = 0.2f;
    public float maxDistance      = 100f;
    public float orbitSensitivity = 0.25f;   // градусов на пиксель
    public float panSensitivity   = 0.002f;
    public float smoothTime       = 0.2f;

    void Awake()
    {
        if (cam == null) cam = GetComponent<Camera>();
    }

    void Update()
    {
        HandleInput();
        SmoothAndApply();
    }

    void HandleInput()
    {
        // --- Touch ---
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved)
            {
                yaw   += t.deltaPosition.x * orbitSensitivity;
                pitch -= t.deltaPosition.y * orbitSensitivity;
                pitch  = Mathf.Clamp(pitch, -89f, 89f);
            }
        }
        else if (Input.touchCount == 2)
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);

            Vector2 p0 = t0.position - t0.deltaPosition;
            Vector2 p1 = t1.position - t1.deltaPosition;

            float prevDist = (p0 - p1).magnitude;
            float currDist = (t0.position - t1.position).magnitude;
            if (prevDist > 1f && currDist > 1f)
                targetDistance = Mathf.Clamp(targetDistance * (prevDist / currDist), minDistance, maxDistance);

            Vector2 prevMid = (p0 + p1) * 0.5f;
            Vector2 currMid = (t0.position + t1.position) * 0.5f;
            PanScreen(currMid - prevMid);
        }

        // --- Mouse (для редактора) ---
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButton(1))
        {
            yaw   += Input.GetAxis("Mouse X") * orbitSensitivity * 15f;
            pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity * 15f;
            pitch  = Mathf.Clamp(pitch, -89f, 89f);
        }
        if (Input.GetMouseButton(2))
            PanScreen(new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 15f);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            targetDistance = Mathf.Clamp(targetDistance * (1f - scroll * 1.5f), minDistance, maxDistance);
#endif
    }

    void PanScreen(Vector2 delta)
    {
        if (delta.sqrMagnitude < 0.25f) return;
        float factor = currentDistance * panSensitivity;
        Vector3 move = -transform.right * delta.x * factor
                     -  transform.up   * delta.y * factor;
        currentCenter += move;
        targetCenter  += move;
    }

    void SmoothAndApply()
    {
        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, smoothTime));
        currentCenter   = Vector3.Lerp(currentCenter, targetCenter, t);
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, t);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 dir    = rot * Vector3.forward;
        transform.position = currentCenter - dir * currentDistance;
        transform.rotation = rot;
    }

    /// <summary>Плавно перемещает камеру к нужной точке.</summary>
    public void FocusOn(Vector3 center, float distance, bool instant = false)
    {
        targetCenter   = center;
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        if (instant)
        {
            currentCenter   = targetCenter;
            currentDistance = targetDistance;
        }
    }
}