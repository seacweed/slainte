using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailComposition
    {
        private readonly Dictionary<ItemDef, float> volumes = new();
        private float thermalVolumeMl;
        private float weightedTemperature;

        public IReadOnlyDictionary<ItemDef, float> Volumes => volumes;
        public float TotalVolumeMl { get; private set; }
        public float AverageTemperatureC => thermalVolumeMl > 0f
            ? weightedTemperature / thermalVolumeMl
            : 20f;
        public string GlassId { get; private set; } = string.Empty;
        public bool HasIce { get; private set; }
        public CocktailTechnique Techniques { get; private set; } = CocktailTechnique.None;

        public void Add(ItemDef item, float volumeMl)
        {
            if (item == null || volumeMl <= 0f)
                return;

            if (!volumes.ContainsKey(item))
                volumes.Add(item, 0f);

            volumes[item] += volumeMl;
            TotalVolumeMl += volumeMl;
        }

        public float GetVolume(ItemDef item)
        {
            return item != null && volumes.TryGetValue(item, out float volumeMl)
                ? volumeMl
                : 0f;
        }

        public void AddThermalSample(float volumeMl, float temperatureC)
        {
            if (volumeMl <= 0f)
                return;

            thermalVolumeMl += volumeMl;
            weightedTemperature += temperatureC * volumeMl;
        }

        public void RecordTechnique(CocktailTechnique technique)
        {
            Techniques |= technique;
        }

        public void SetServingStyle(string glassId, bool hasIce)
        {
            GlassId = glassId?.Trim() ?? string.Empty;
            HasIce = hasIce;
        }

        public CocktailTechnique GetEffectiveTechniques()
        {
            return Techniques == CocktailTechnique.None ? CocktailTechnique.Build : Techniques;
        }
    }

    [RequireComponent(typeof(Collider2D))]
    public sealed class VesselLiquidTracker : MonoBehaviour
    {
        private readonly HashSet<LiquidParticleData> particles = new();
        private readonly List<Collider2D> overlapResults = new();
        private readonly StringBuilder debugTextBuilder = new();
        private Collider2D[] colliders;
        private ContactFilter2D scanFilter;
        private GUIStyle debugBoxStyle;
        private string servingGlassId = string.Empty;
        private bool hasIce;

        [Header("Debug View")]
        [SerializeField] private bool drawDebugGizmos = true;
        [SerializeField] private bool drawOnlyWhenSelected = false;
        [SerializeField] private bool drawRuntimeLabel = true;
        [SerializeField] private Color triggerDebugColor = new Color(0.15f, 0.85f, 1f, 0.8f);
        [SerializeField] private Color particleDebugColor = new Color(1f, 0.92f, 0.25f, 0.9f);
        [SerializeField] private Color connectionDebugColor = new Color(0.5f, 1f, 0.65f, 0.45f);
        [SerializeField, Min(0.01f)] private float debugParticleRadius = 0.06f;

        public int ParticleCount
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles.Count;
            }
        }

        public IReadOnlyCollection<LiquidParticleData> Particles
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles;
            }
        }

        private void Awake()
        {
            CacheColliders();
            scanFilter = ContactFilter2D.noFilter;
            scanFilter.useTriggers = true;
        }

        private void OnDisable()
        {
            particles.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Track(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            Track(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.TryGetComponent(out LiquidParticleData particle))
                particles.Remove(particle);
        }

        public CocktailComposition BuildComposition()
        {
            Cleanup();
            RefreshTrackedParticles();

            CocktailComposition composition = new CocktailComposition();
            composition.SetServingStyle(servingGlassId, hasIce);
            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null || particle.payload == null)
                    continue;

                for (int i = 0; i < particle.payload.portions.Count; i++)
                {
                    LiquidPortion portion = particle.payload.portions[i];
                    composition.Add(portion.sourceItem, portion.volumeMl);
                }

                composition.AddThermalSample(
                    particle.payload.TotalVolumeMl,
                    particle.payload.temperatureC);
                composition.RecordTechnique(particle.payload.techniques);
            }

            return composition;
        }

        public void ConfigureServingStyle(string glassId, bool containsIce = false)
        {
            servingGlassId = glassId?.Trim() ?? string.Empty;
            hasIce = containsIce;
        }

        public void SetHasIce(bool value)
        {
            hasIce = value;
        }

        public void TranslateTrackedParticles(Vector2 delta)
        {
            if (delta.sqrMagnitude <= 0.000001f)
                return;

            Cleanup();
            RefreshTrackedParticles();

            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null)
                    continue;

                Rigidbody2D particleBody = particle.GetComponent<Rigidbody2D>();
                if (particleBody != null)
                {
                    particleBody.position += delta;
                    particleBody.WakeUp();
                }
                else
                {
                    particle.transform.position += (Vector3)delta;
                }
            }
        }

        private void Track(Collider2D other)
        {
            if (other == null)
                return;

            if (!other.TryGetComponent(out LiquidParticleData particle))
                return;

            if (particle.hasBeenCollected)
                return;

            particles.Add(particle);
        }

        private void Cleanup()
        {
            particles.RemoveWhere(IsInvalidParticle);
        }

        private void RefreshTrackedParticles()
        {
            particles.Clear();
            CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D trigger = colliders[i];
                if (trigger == null || !trigger.enabled || !trigger.isTrigger)
                    continue;

                overlapResults.Clear();
                trigger.Overlap(scanFilter, overlapResults);

                for (int j = 0; j < overlapResults.Count; j++)
                    Track(overlapResults[j]);
            }

            overlapResults.Clear();
        }

        private void CacheColliders()
        {
            colliders = GetComponentsInChildren<Collider2D>();
        }

        private static bool IsInvalidParticle(LiquidParticleData particle)
        {
            return particle == null
                || particle.hasBeenCollected
                || !particle.gameObject.activeInHierarchy;
        }

        public bool HasTriggerCollider()
        {
            if (colliders == null || colliders.Length == 0)
                CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger)
                    return true;
            }

            return false;
        }

        public void SetDebugViewEnabled(bool enabled)
        {
            drawDebugGizmos = enabled;
            drawRuntimeLabel = enabled;
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos || !drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !drawDebugGizmos || !drawRuntimeLabel)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            CocktailComposition composition = BuildComposition();
            Vector3 screenPosition = camera.WorldToScreenPoint(GetDebugLabelWorldPosition());
            if (screenPosition.z < 0f)
                return;

            EnsureDebugBoxStyle();

            const float width = 220f;
            string text = BuildDebugText(composition);
            GUIContent content = new GUIContent(text);
            float height = debugBoxStyle.CalcHeight(content, width);
            Rect rect = new Rect(
                screenPosition.x + 8f,
                Screen.height - screenPosition.y - height - 8f,
                width,
                height);

            GUI.Box(rect, content, debugBoxStyle);
        }

        private void DrawDebugGizmos()
        {
            CacheColliders();

            Color previousColor = Gizmos.color;

            Gizmos.color = triggerDebugColor;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                DrawColliderGizmo(collider);
            }

            if (Application.isPlaying)
            {
                RefreshTrackedParticles();
                Vector3 origin = GetDebugLabelWorldPosition();

                foreach (LiquidParticleData particle in particles)
                {
                    if (particle == null)
                        continue;

                    Vector3 position = particle.transform.position;
                    Gizmos.color = connectionDebugColor;
                    Gizmos.DrawLine(origin, position);
                    Gizmos.color = particleDebugColor;
                    Gizmos.DrawWireSphere(position, debugParticleRadius);
                }

#if UNITY_EDITOR
                UnityEditor.Handles.Label(origin, BuildDebugText(BuildComposition()));
#endif
            }

            Gizmos.color = previousColor;
        }

        private void DrawColliderGizmo(Collider2D collider)
        {
            if (collider is BoxCollider2D boxCollider)
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = boxCollider.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(boxCollider.offset, boxCollider.size);
                Gizmos.matrix = previousMatrix;
                return;
            }

            if (collider is CircleCollider2D circleCollider)
            {
                Vector3 center = circleCollider.transform.TransformPoint(circleCollider.offset);
                Vector3 scale = circleCollider.transform.lossyScale;
                float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                Gizmos.DrawWireSphere(center, circleCollider.radius * radiusScale);
                return;
            }

            Bounds bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private Vector3 GetDebugLabelWorldPosition()
        {
            CacheColliders();

            bool hasBounds = false;
            Bounds combinedBounds = default;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
                return transform.position + Vector3.up * 0.5f;

            return new Vector3(combinedBounds.center.x, combinedBounds.max.y + 0.25f, transform.position.z);
        }

        private string BuildDebugText(CocktailComposition composition)
        {
            debugTextBuilder.Clear();
            debugTextBuilder.Append(name);
            debugTextBuilder.AppendLine(" 액체 추적기");
            debugTextBuilder.Append("입자: ");
            debugTextBuilder.Append(particles.Count);
            debugTextBuilder.Append("  총량: ");
            debugTextBuilder.Append(composition.TotalVolumeMl.ToString("0.##"));
            debugTextBuilder.AppendLine(" ml");
            debugTextBuilder.Append("온도: ");
            debugTextBuilder.Append(composition.AverageTemperatureC.ToString("0.#"));
            debugTextBuilder.Append(" C  잔: ");
            debugTextBuilder.Append(GetGlassLabel(composition.GlassId));
            debugTextBuilder.Append("  얼음: ");
            debugTextBuilder.AppendLine(composition.HasIce ? "있음" : "없음");

            int lineCount = 0;
            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                if (lineCount >= 4)
                {
                    debugTextBuilder.AppendLine("...");
                    break;
                }

                float ratio = composition.TotalVolumeMl > 0f
                    ? pair.Value / composition.TotalVolumeMl * 100f
                    : 0f;

                debugTextBuilder.Append(GetItemLabel(pair.Key));
                debugTextBuilder.Append(": ");
                debugTextBuilder.Append(pair.Value.ToString("0.##"));
                debugTextBuilder.Append(" ml / ");
                debugTextBuilder.Append(ratio.ToString("0.#"));
                debugTextBuilder.AppendLine("%");
                lineCount++;
            }

            if (lineCount == 0)
                debugTextBuilder.AppendLine("비어 있음");

            return debugTextBuilder.ToString();
        }

        private static string GetGlassLabel(string glassId)
        {
            if (string.IsNullOrWhiteSpace(glassId))
                return "미지정";

            return glassId.Trim().ToLowerInvariant() switch
            {
                "rock" => "락 글라스",
                "highball" => "하이볼 글라스",
                "hurricane" => "허리케인 글라스",
                "martini" => "마티니 글라스",
                _ => glassId
            };
        }

        private static string GetItemLabel(ItemDef item)
        {
            if (item == null)
                return "알 수 없음";

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id;

            return item.name;
        }

        private void EnsureDebugBoxStyle()
        {
            if (debugBoxStyle != null)
                return;

            debugBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(6, 6, 5, 5)
            };
            debugBoxStyle.normal.textColor = Color.white;
        }
    }
}
