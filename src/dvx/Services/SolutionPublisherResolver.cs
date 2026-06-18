using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Resolves the customization prefix of the publisher that owns a given solution.
    /// In Dataverse the prefix is a property of the <b>publisher</b>
    /// (<c>publisher.customizationprefix</c>) — every customization that publisher owns
    /// (tables, columns, web resources, plugin packages) shares it. There is no separate
    /// per-component prefix, so deploys that target a solution derive their prefix from it.
    /// </summary>
    public class SolutionPublisherResolver
    {
        private readonly IOrganizationService _svc;

        public SolutionPublisherResolver(IOrganizationService svc) => _svc = svc;

        /// <summary>
        /// Looks up <paramref name="solutionUniqueName"/> and returns its publisher's
        /// <c>customizationprefix</c> in a single query (solution joined to publisher).
        /// Throws when the solution is not found or its publisher has no prefix.
        /// </summary>
        public string GetCustomizationPrefix(string solutionUniqueName)
        {
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet(false),
                Criteria  = new FilterExpression(),
                TopCount  = 1,
            };
            query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);
            query.LinkEntities.Add(new LinkEntity(
                "solution", "publisher", "publisherid", "publisherid", JoinOperator.Inner)
            {
                EntityAlias = "publisher",
                Columns     = new ColumnSet("customizationprefix"),
            });

            var result = _svc.RetrieveMultiple(query);
            if (result.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"Solution '{solutionUniqueName}' was not found in the target environment.");

            var prefix = result.Entities[0]
                .GetAttributeValue<AliasedValue>("publisher.customizationprefix")?.Value as string;

            if (string.IsNullOrWhiteSpace(prefix))
                throw new InvalidOperationException(
                    $"The publisher for solution '{solutionUniqueName}' has no customization prefix.");

            return prefix!;
        }
    }
}
