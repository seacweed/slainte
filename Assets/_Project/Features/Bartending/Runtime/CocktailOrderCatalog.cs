using System.Collections.Generic;

namespace Slainte.Bartending
{
    public enum CocktailOrderType
    {
        RecipeOrder = 0,
        RecipeModifierOrder = 1,
        TasteOrder = 2,
        MoodOrder = 3,
        // 직렬화 호환을 위해 이전 값 4는 재사용하지 않는다.
        CustomRecipeOrder = 5,
        EpisodeOrder = 6
    }

    public static class CocktailOrderTagRules
    {
        public static bool TryGetSingleTag(
            IEnumerable<string> source,
            out string requestedTag)
        {
            requestedTag = string.Empty;
            if (source == null)
                return false;

            int validTags = 0;
            foreach (string tag in source)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                validTags++;
                requestedTag = Normalize(tag);
            }

            return validTags == 1 && !string.IsNullOrWhiteSpace(requestedTag);
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            int separator = trimmed.IndexOf('_');
            return (separator >= 0 ? trimmed.Substring(0, separator) : trimmed).Trim();
        }
    }

    public sealed class CocktailOrderTemplate
    {
        public string id;
        public CocktailOrderType orderType;
        public string lineTemplate;
        public float weight = 1f;
    }

    public sealed class GeneratedCocktailOrder
    {
        public string id;
        public CocktailOrderType orderType;
        public string line;
        public string requestedRecipeId;
        public string requestedConditionLabel;
        public CocktailRecipe requestedRecipe;
        public CocktailOrderTemplate sourceTemplate;
        public readonly HashSet<string> requiredTasteTags = new(System.StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> requiredMoodTags = new(System.StringComparer.OrdinalIgnoreCase);

        public string RequestedRecipeName
        {
            get
            {
                if (requestedRecipe == null)
                    return !string.IsNullOrWhiteSpace(requestedConditionLabel)
                        ? requestedConditionLabel
                        : requestedRecipeId;

                if (!string.IsNullOrWhiteSpace(requestedRecipe.displayName))
                    return requestedRecipe.displayName;

                return requestedRecipe.id;
            }
        }
    }

    public sealed class CocktailOrderTemplateCatalog
    {
        private readonly Dictionary<string, CocktailOrderTemplate> templatesById = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<CocktailOrderType, List<CocktailOrderTemplate>> templatesByType = new();
        private readonly List<CocktailOrderTemplate> templates = new();

        public IReadOnlyDictionary<string, CocktailOrderTemplate> TemplatesById => templatesById;
        public IReadOnlyList<CocktailOrderTemplate> Templates => templates;
        public int Count => templates.Count;

        public void Add(CocktailOrderTemplate template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.id))
                return;

            string id = template.id.Trim();
            if (templatesById.TryGetValue(id, out CocktailOrderTemplate existing))
            {
                RemoveFromTypeIndex(existing);
                int index = templates.IndexOf(existing);
                if (index >= 0)
                    templates[index] = template;
                else
                    templates.Add(template);
            }
            else
            {
                templates.Add(template);
            }

            template.id = id;
            templatesById[id] = template;

            if (!templatesByType.TryGetValue(template.orderType, out List<CocktailOrderTemplate> typedTemplates))
            {
                typedTemplates = new List<CocktailOrderTemplate>();
                templatesByType[template.orderType] = typedTemplates;
            }

            typedTemplates.Add(template);
        }

        public bool TryGet(string id, out CocktailOrderTemplate template)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                template = null;
                return false;
            }

            return templatesById.TryGetValue(id.Trim(), out template);
        }

        public IReadOnlyList<CocktailOrderTemplate> GetTemplates(CocktailOrderType orderType)
        {
            return templatesByType.TryGetValue(orderType, out List<CocktailOrderTemplate> typedTemplates)
                ? typedTemplates
                : System.Array.Empty<CocktailOrderTemplate>();
        }

        private void RemoveFromTypeIndex(CocktailOrderTemplate template)
        {
            if (template == null)
                return;

            if (templatesByType.TryGetValue(template.orderType, out List<CocktailOrderTemplate> typedTemplates))
                typedTemplates.Remove(template);
        }
    }
}
