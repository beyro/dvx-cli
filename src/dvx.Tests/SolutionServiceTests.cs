using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class SolutionServiceTests
    {
        // ── ValidateSolutionExists ─────────────────────────────────────────────

        [Fact]
        public void ValidateSolutionExists_SolutionFound_DoesNotThrow()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "solution"))
               .Returns(new EntityCollection(new List<Entity> { new Entity("solution", Guid.NewGuid()) }));

            var service = new SolutionService(svc);
            Should.NotThrow(() => service.ValidateSolutionExists("MySolution"));
        }

        [Fact]
        public void ValidateSolutionExists_SolutionNotFound_ThrowsWithSolutionName()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "solution"))
               .Returns(new EntityCollection());

            var service = new SolutionService(svc);
            var ex = Should.Throw<InvalidOperationException>(() => service.ValidateSolutionExists("DoesNotExist"));
            ex.Message.ShouldContain("DoesNotExist");
        }

        [Fact]
        public void ValidateSolutionExists_QueriesByUniqueName()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());

            var service = new SolutionService(svc);
            Should.Throw<InvalidOperationException>(() => service.ValidateSolutionExists("MySolution"));

            svc.Received(1).RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "solution" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "uniquename" &&
                    c.Values.Contains("MySolution"))));
        }

        [Fact]
        public void ValidateSolutionExists_Verbose_DoesNotThrowAndStillValidates()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "solution"))
               .Returns(new EntityCollection(new List<Entity> { new Entity("solution", Guid.NewGuid()) }));

            var service = new SolutionService(svc);
            Should.NotThrow(() => service.ValidateSolutionExists("MySolution", verbose: true));
        }

        // ── AddStepToSolution ──────────────────────────────────────────────────

        [Fact]
        public void AddStepToSolution_ExecutesCorrectRequest()
        {
            var svc = Substitute.For<IOrganizationService>();
            var stepId = Guid.NewGuid();
            OrganizationRequest? captured = null;
            svc.Execute(Arg.Do<OrganizationRequest>(r => captured = r));

            var service = new SolutionService(svc);
            service.AddStepToSolution(stepId, "MySolution");

            captured.ShouldNotBeNull();
            captured!.RequestName.ShouldBe("AddSolutionComponent");
            captured["ComponentId"].ShouldBe(stepId);
            captured["ComponentType"].ShouldBe(92);
            captured["SolutionUniqueName"].ShouldBe("MySolution");
            captured["AddRequiredComponents"].ShouldBe(false);
        }

        [Fact]
        public void AddStepToSolution_Verbose_DoesNotThrowAndStillExecutes()
        {
            var svc = Substitute.For<IOrganizationService>();
            var stepId = Guid.NewGuid();

            var service = new SolutionService(svc);
            Should.NotThrow(() => service.AddStepToSolution(stepId, "MySolution", verbose: true));

            svc.Received(1).Execute(Arg.Any<OrganizationRequest>());
        }

        // ── AddWebResourceToSolution ───────────────────────────────────────────

        [Fact]
        public void AddWebResourceToSolution_ExecutesRequestWithComponentType61()
        {
            var svc = Substitute.For<IOrganizationService>();
            var id  = Guid.NewGuid();
            OrganizationRequest? captured = null;
            svc.Execute(Arg.Do<OrganizationRequest>(r => captured = r));

            new SolutionService(svc).AddWebResourceToSolution(id, "MySolution");

            captured.ShouldNotBeNull();
            captured!.RequestName.ShouldBe("AddSolutionComponent");
            captured["ComponentId"].ShouldBe(id);
            captured["ComponentType"].ShouldBe(61);
            captured["SolutionUniqueName"].ShouldBe("MySolution");
            captured["AddRequiredComponents"].ShouldBe(false);
        }

        // ── GetSolutionWebResources ────────────────────────────────────────────

        [Fact]
        public void GetSolutionWebResources_ReturnsIdAndName()
        {
            var svc = Substitute.For<IOrganizationService>();
            var id  = Guid.NewGuid();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "webresource"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("webresource", id) { ["name"] = "pub_/a.js" }
               }));

            var result = new SolutionService(svc).GetSolutionWebResources("MySolution");

            result.Count.ShouldBe(1);
            result[0].Id.ShouldBe(id);
            result[0].Name.ShouldBe("pub_/a.js");
        }

        [Fact]
        public void GetSolutionWebResources_FiltersByComponentType61AndSolutionUniqueName()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());

            new SolutionService(svc).GetSolutionWebResources("MySolution");

            svc.Received(1).RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "webresource" &&
                q.LinkEntities.Any(le =>
                    le.LinkToEntityName == "solutioncomponent" &&
                    le.LinkCriteria.Conditions.Any(c => c.AttributeName == "componenttype" && c.Values.Contains(61)) &&
                    le.LinkEntities.Any(se =>
                        se.LinkToEntityName == "solution" &&
                        se.LinkCriteria.Conditions.Any(c => c.AttributeName == "uniquename" && c.Values.Contains("MySolution"))))));
        }

        // ── GetSolutionStepIds ─────────────────────────────────────────────────

        [Fact]
        public void GetSolutionStepIds_ReturnsObjectIds()
        {
            var svc = Substitute.For<IOrganizationService>();
            var id  = Guid.NewGuid();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "solutioncomponent"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("solutioncomponent", Guid.NewGuid()) { ["objectid"] = id }
               }));

            var result = new SolutionService(svc).GetSolutionStepIds("MySolution");

            result.Count.ShouldBe(1);
            result.ShouldContain(id);
        }

        [Fact]
        public void GetSolutionStepIds_FiltersByComponentType92AndSolutionUniqueName()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());

            new SolutionService(svc).GetSolutionStepIds("MySolution");

            svc.Received(1).RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "solutioncomponent" &&
                q.Criteria.Conditions.Any(c => c.AttributeName == "componenttype" && c.Values.Contains(92)) &&
                q.LinkEntities.Any(le =>
                    le.LinkToEntityName == "solution" &&
                    le.LinkCriteria.Conditions.Any(c => c.AttributeName == "uniquename" && c.Values.Contains("MySolution")))));
        }
    }
}
