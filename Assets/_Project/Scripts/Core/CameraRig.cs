using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diverse
{
    /// <summary>
    /// 픽셀 퍼펙트에 가까운 직교 카메라 + 화면 흔들림(트라우마 방식) + 방향성 킥.
    /// 카메라·후처리 설정은 Game.unity의 Main Camera / Global Volume(Data/PostFx.asset)에서 조정한다.
    /// 무기마다 Shake/Kick 값을 다르게 넣어 타격감을 차별화한다.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }

        public Transform target;
        public float zoom = 4;          // 도트 1px = 화면 zoom px (현재 목표값, 정수가 아닐 수 있음)
        [Tooltip("가장 넓은 시야(기본)에서 화면 세로에 들어가는 대략의 도트 수. 클수록 넓게 보인다.")]
        public float widestViewPx = 315;
        [Tooltip("휠 한 칸에 확대되는 비율 (0.1 = 10%)")]
        public float zoomStep = 0.1f;
        [Tooltip("마우스 휠로 기본 시야에서 몇 칸까지 확대할 수 있는지")]
        public int maxZoomIn = 8;
        [Tooltip("휠 확대/축소가 따라가는 속도")]
        public float zoomSharpness = 12f;
        public float followSharpness = 10f;
        public Vector2 lookAhead;       // 마우스 방향으로 살짝 시선 이동

        Vector2 focus;
        float trauma;                   // 0~1
        Vector2 kick;                   // 방향성 밀림 (빠르게 복귀)
        float zoomPunch;                // 강타 시 살짝 줌인
        Vector2 lead, lastTarget;       // movement look-ahead (smoothed)
        int zoomIn;                     // wheel steps in from the widest view (0 = widest)
        float zoomShown = -1;           // pixel scale on screen, eases towards zoom

        /// <summary>Scene setup: the Main Camera object in Game.unity carries Camera + CameraRig, with the Global Volume as a child.</summary>
        void Awake()
        {
            I = this;
            Cam = GetComponent<Camera>();
            // Volume.profile (unlike sharedProfile) returns a runtime copy, so the low-HP vignette never writes into the asset
            volume = GetComponentInChildren<Volume>();
            if (volume != null && volume.sharedProfile != null)
            {
                if (volume.profile.TryGet<Vignette>(out var vig)) Vignette = vig;
                volume.profile.TryGet(out bloom);
            }
            SetPostFx(GraphicsQuality.PostFx);
        }

        Volume volume;
        Bloom bloom;

        /// <summary>Post-processing level (설정 → 그래픽): 0 off, 1 without bloom/HDR (the costly part), 2 full.</summary>
        public void SetPostFx(int level)
        {
            if (Cam == null) return;
            var data = Cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = level > 0;
            Cam.allowHDR = level >= 2;
            if (bloom != null) bloom.active = level >= 2;
        }

        void OnDestroy() { if (I == this) I = null; }

        public Vignette Vignette { get; private set; }

        public void Snap(Vector2 pos) { focus = pos; lead = Vector2.zero; lastTarget = pos; ApplyTransform(); }

        /// <summary>amount 0~1. 흔들림은 trauma²에 비례해서 작은 타격은 은은하고 큰 타격은 확실하게.</summary>
        public void Shake(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, amount) + amount * 0.25f);

        /// <summary>타격 방향으로 화면을 툭 민다 (픽셀 단위 거리).</summary>
        public void Kick(Vector2 dir, float pixels) => kick += dir.normalized * pixels / Art.PPU;

        public void Punch(float amount) => zoomPunch = Mathf.Max(zoomPunch, amount);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (target != null)
            {
                // Lead a little in the direction of travel so the view glides ahead of the hero instead of trailing it
                Vector2 tp = target.position;
                if (Time.deltaTime > 0) lead = Vector2.Lerp(lead, Vector2.ClampMagnitude((tp - lastTarget) / Time.deltaTime * 0.16f, 1.1f), 1 - Mathf.Exp(-4f * dt));
                lastTarget = tp;
                Vector2 goal = tp + lookAhead + lead;
                focus = Vector2.Lerp(focus, goal, 1 - Mathf.Exp(-followSharpness * dt));
            }
            trauma = Mathf.Max(0, trauma - dt * 1.6f);
            kick = Vector2.Lerp(kick, Vector2.zero, 1 - Mathf.Exp(-18f * dt));
            zoomPunch = Mathf.Lerp(zoomPunch, 0, 1 - Mathf.Exp(-12f * dt));
            HandleWheel();
            zoomShown = Mathf.Lerp(zoomShown, zoom, 1 - Mathf.Exp(-zoomSharpness * dt));
            if (Mathf.Abs(zoomShown - zoom) < 0.002f) zoomShown = zoom;
            ApplyTransform();
        }

        /// <summary>Mouse wheel while playing (not over UI): up zooms in, down back out to the widest view.</summary>
        void HandleWheel()
        {
            var g = Game.I;
            if (g == null || g.State != GameState.Playing || g.UiCapturingMouse) return;
            float wheel = Controls.Scroll;
            if (Mathf.Abs(wheel) < 0.01f) return;
            zoomIn = Mathf.Clamp(zoomIn + (wheel > 0 ? 1 : -1), 0, Mathf.Max(0, maxZoomIn));
        }

        /// <summary>Widest pixel scale for this screen (screen pixels per art pixel, at least 1). Not a whole number in general:
        /// the sprite shader (DiversePixel.hlsl) keeps the art pixels even at in-between scales.</summary>
        float WidestZoom => Mathf.Max(1f, Screen.height / Mathf.Max(60f, widestViewPx));

        void ApplyTransform()
        {
            if (Cam == null) return;
            zoom = WidestZoom * Mathf.Pow(1f + zoomStep, zoomIn);
            if (zoomShown <= 0) zoomShown = zoom;
            float baseSize = Screen.height / (2f * Art.PPU * zoomShown);
            Cam.orthographicSize = baseSize * (1f - zoomPunch * 0.06f);

            float t = Time.unscaledTime * 38f;
            float s = trauma * trauma;
            Vector2 shake = new Vector2(Mathf.PerlinNoise(t, 3.1f) - 0.5f, Mathf.PerlinNoise(7.7f, t) - 0.5f) * (s * 0.9f);

            Vector2 p = focus + shake + kick;
            // No rounding to the screen-pixel grid: at walking speed that scrolled the world in alternating 4/5-pixel steps
            // (visible judder). The sprite shader keeps art pixels clean at any sub-pixel camera position.
            transform.position = new Vector3(p.x, p.y, -10);
        }

        public Vector2 MouseWorld()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null || Cam == null) return focus;
            Vector2 sp = m.position.ReadValue();
            return Cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10));
        }

        public Rect ViewRect()
        {
            float h = Cam.orthographicSize * 2, w = h * Cam.aspect;
            return new Rect(transform.position.x - w / 2, transform.position.y - h / 2, w, h);
        }
    }
}
