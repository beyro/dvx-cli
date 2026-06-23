using dvx.Output;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    public class SolutionService(IOrganizationService svc)
    {
        public virtual void ValidateSolutionExists(string solutionUniqueName, bool verbose = false)
        {
            if (verbose)
                Out.Dim($"    Checking solution '{solutionUniqueName}' exists in Dataverse...");

            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("solutionid"),
                Criteria = new FilterExpression()
                {
                    Conditions =
                    {
                        new ConditionExpression("uniquename", ConditionOperator.Equal, solutionUniqueName)
                    }
                },
            };
            var result = svc.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"Solution '{solutionUniqueName}' was not found in Dataverse. " +
                    "Ensure the solution unique name is correct.");

            if (verbose)
                Out.Dim($"    Solution '{solutionUniqueName}' found.");
        }

        public virtual void AddComponentToSolution(
            Guid componentId, int componentType, string solutionUniqueName, bool verbose = false)
        {
            if (verbose)
                Out.Dim($"    Adding component {componentId} (type {componentType}) to solution '{solutionUniqueName}'...");

            var request = new OrganizationRequest("AddSolutionComponent")
            {
                ["ComponentId"] = componentId,
                ["ComponentType"] = componentType,
                ["SolutionUniqueName"] = solutionUniqueName,
                ["AddRequiredComponents"] = false
            };
            svc.Execute(request);
        }

        public virtual void AddStepToSolution(Guid stepId, string solutionUniqueName, bool verbose = false)
            => AddComponentToSolution(stepId, 92 /* sdkmessageprocessingstep */, solutionUniqueName, verbose);

        public virtual void AddWebResourceToSolution(Guid webResourceId, string solutionUniqueName, bool verbose = false)
            => AddComponentToSolution(webResourceId, 61 /* webresource */, solutionUniqueName, verbose);

        /// <summary>
        /// Returns the id and name of every web resource that is a component of the given solution.
        /// Used to detect orphans (resources in the solution but no longer in source).
        /// </summary>
        public virtual IReadOnlyList<(Guid Id, string Name)> GetSolutionWebResources(
            string solutionUniqueName, bool verbose = false)
        {
            if (verbose)
                Out.Dim($"    Querying web resources in solution '{solutionUniqueName}'...");

            var query = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("webresourceid", "name"),
            };
            var component = query.AddLink("solutioncomponent", "webresourceid", "objectid");
            component.LinkCriteria.AddCondition("componenttype", ConditionOperator.Equal, 61);
            var solution = component.AddLink("solution", "solutionid", "solutionid");
            solution.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);

            var results = new List<(Guid, string)>();
            foreach (var e in svc.RetrieveMultiple(query).Entities)
                results.Add((e.Id, e.GetAttributeValue<string>("name") ?? string.Empty));

            if (verbose)
                Out.Dim($"    Found {results.Count} web resource(s) in solution.");

            return results;
        }

        /// <summary>
        /// Returns the ids of every plugin step (sdkmessageprocessingstep) already a component of
        /// the given solution. Lets the registrar skip redundant AddSolutionComponent calls for
        /// steps the solution already contains. Mirrors <see cref="GetSolutionWebResources"/>.
        /// </summary>
        public virtual HashSet<Guid> GetSolutionStepIds(string solutionUniqueName, bool verbose = false)
        {
            if (verbose)
                Out.Dim($"    Querying plugin steps in solution '{solutionUniqueName}'...");

            var query = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("componenttype", ConditionOperator.Equal, 92) }
                }
            };
            var solution = query.AddLink("solution", "solutionid", "solutionid");
            solution.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);

            var ids = new HashSet<Guid>();
            foreach (var e in svc.RetrieveMultiple(query).Entities)
            {
                var id = e.GetAttributeValue<Guid>("objectid");
                if (id != Guid.Empty) ids.Add(id);
            }

            if (verbose)
                Out.Dim($"    Found {ids.Count} plugin step(s) in solution.");

            return ids;
        }
    }
}