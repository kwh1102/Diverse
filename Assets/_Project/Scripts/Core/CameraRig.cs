using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diverse
{
    /// <summary>
    /// 픽셀 퍼펙트에 가까운 직교 카메라 + 화면 흔들림(트라우마 방식) + 방향성 킥.
    /// 무기마다 Shake/Kick 값을 다르게 넣어 타격감을 차별화한다.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }

        public Transform target;
        public int zoom = 4;            // 도트 1px = 화면 zoom px
        public float followSharpness = 10f;
        public Vector2 lookAhead;       // 마우스 방향으로 살짝 시선 이동

        Vector2 focus;
        float trauma;                   // 0~1
        Vector2 kick;                   // 방향성 밀림 (빠르게 복귀)
        float zoomPunch;                // 강타 시 살짝 줌인

        public static CameraRig Create()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.07f, 0.12f);
            cam.nearClipPlane = -50; cam.farClipPlane = 50;
            cam.allowMSAA = false;
            cam.allowHDR = true;
            go.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = cam;
            I = rig;
            rig.CreatePostFx();
            return rig;
        }

        void CreatePostFx()
        {
            var go = new GameObject("Global Volume");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.75f);
            bloom.threshold.Override(0.82f);
            bloom.scatter.Override(0.55f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(8f);
            ca.contrast.Override(6f);
            Vignette = vig;
            vol.profile = profile;
        }

        public Vignette Vignette { get; private set; }

        public void Snap(Vector2 pos) { focus = pos; ApplyTransform(); }

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
                Vector2 goal = (Vector2)target.position + lookAhead;
                focus = Vector2.Lerp(focus, goal, 1 - Mathf.Exp(-followSharpness * dt));
            }
            trauma = Mathf.Max(0, trauma - dt * 1.6f);
            kick = Vector2.Lerp(kick, Vector2.zero, 1 - Mathf.Exp(-18f * dt));
            zoomPunch = Mathf.Lerp(zoomPunch, 0, 1 - Mathf.Exp(-12f * dt));
            ApplyTransform();
        }

        void ApplyTransform()
        {
            if (Cam == null) return;
            zoom = Mathf.Max(2, Mathf.RoundToInt(Screen.height / 270f));
            float baseSize = Screen.height / (2f * Art.PPU * zoom);
            Cam.orthographicSize = baseSize * (1f - zoomPunch * 0.06f);

            float t = Time.unscaledTime * 38f;
            float s = trauma * trauma;
            Vector2 shake = new Vector2(Mathf.PerlinNoise(t, 3.1f) - 0.5f, Mathf.PerlinNoise(7.7f, t) - 0.5f) * (s * 0.9f);

            Vector2 p = focus + shake + kick;
            // 도트 격자에 맞춰 반올림 (흔들림 중에는 해제해 부드럽게)
            float unit = 1f / (Art.PPU * zoom);
            if (s < 0.01f && kick.sqrMagnitude < 0.0001f)
            {
                p.x = Mathf.Round(p.x / unit) * unit;
                p.y = Mathf.Round(p.y / unit) * unit;
            }
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
