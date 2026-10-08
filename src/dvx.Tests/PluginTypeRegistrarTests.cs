using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PluginTypeRegistrarTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        // A service whose every query returns empty unless a test configures a specific entity.
        private static IOrganizationService Svc()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());
            return svc;
        }

        private static Entity Type(string name, Guid id) =>
            new("plugintype", id) { ["typename"] = name };

        private static void HasTypes(IOrganizationService svc, params Entity[] types) =>
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(types.ToList()));

        private static void HasCustomApi(IOrganizationService svc, Guid plugintypeId) =>
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "customapi"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new("customapi", Guid.NewGuid())
                       { ["plugintypeid"] = new EntityReference("plugintype", plugintypeId) }
               }));

        private static void HasCustomAction(IOrganizationService svc, Guid plugintypeId) =>
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "workflow"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new("workflow", Guid.NewGuid())
                       { ["plugintypeid"] = new EntityReference("plugintype", plugintypeId) }
               }));

        private static void HasSteps(IOrganizationService svc, params Guid[] stepIds) =>
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstep"))
               .Returns(new EntityCollection(
                   stepIds.Select(id => new Entity("sdkmessageprocessingstep", id)).ToList()));

        // ── EnsureRegistered ───────────────────────────────────────────────────

        [Fact]
        public void EnsureRegistered_CreatesMissingTypes()
        {
            var svc        = Svc();
            var assemblyId = Guid.NewGuid();

            new PluginTypeRegistrar(svc).EnsureRegistered(assemblyId, new[] { "NS.A", "NS.B" });

            svc.Received(1).Create(Arg.Is<Entity>(e =>
                e.LogicalName == "plugintype" &&
                (string)e["typename"] == "NS.A" &&
                (string)e["name"] == "NS.A" &&
                (string)e["friendlyname"] == "NS.A" &&
                ((EntityReference)e["pluginassemblyid"]).Id == assemblyId));
            svc.Received(1).Create(Arg.Is<Entity>(e => (string)e["typename"] == "NS.B"));
            svc.Received(2).Create(Arg.Any<Entity>());
        }

        [Fact]
        public void EnsureRegistered_SkipsExistingTypes()
        {
            var svc        = Svc();
            var assemblyId = Guid.NewGuid();
            HasTypes(svc, Type("NS.A", Guid.NewGuid()));

            new PluginTypeRegistrar(svc).EnsureRegistered(assemblyId, new[] { "NS.A", "NS.B" });

            svc.Received(1).Create(Arg.Is<Entity>(e => (string)e["typename"] == "NS.B"));
            svc.DidNotReceive().Create(Arg.Is<Entity>(e => (string)e["typename"] == "NS.A"));
        }

        [Fact]
        public void EnsureRegistered_DeduplicatesTypeNames()
        {
            var svc = Svc();

            new PluginTypeRegistrar(svc).EnsureRegistered(Guid.NewGuid(), new[] { "NS.A", "NS.A" });

            svc.Received(1).Create(Arg.Any<Entity>());
        }

        [Fact]
        public void EnsureRegistered_EmptyList_DoesNothing()
        {
            var svc = Svc();

            new PluginTypeRegistrar(svc).EnsureRegistered(Guid.NewGuid(), Array.Empty<string>());

            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void EnsureRegistered_QueriesTypesForTheGivenAssembly()
        {
            var svc        = Svc();
            var assemblyId = Guid.NewGuid();

            new PluginTypeRegistrar(svc).EnsureRegistered(assemblyId, new[] { "NS.A" });

            svc.Received(1).RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "plugintype" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "pluginassemblyid" && c.Values.Contains(assemblyId))));
        }

        // ── DeleteOrphans ──────────────────────────────────────────────────────

        [Fact]
        public void DeleteOrphans_DeletesTypeNotInDesiredSet()
        {
            var svc      = Svc();
            var orphanId = Guid.NewGuid();
            HasTypes(svc, Type("NS.Old", orphanId));

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.New" });

            svc.Received(1).Delete("plugintype", orphanId);
        }

        [Fact]
        public void DeleteOrphans_KeepsTypesInDesiredSet()
        {
            var svc = Svc();
            HasTypes(svc, Type("NS.Keep", Guid.NewGuid()));

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.Keep" });

            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void DeleteOrphans_DeletesOrphanStepsBeforeTheType()
        {
            var svc      = Svc();
            var orphanId = Guid.NewGuid();
            var stepId   = Guid.NewGuid();
            HasTypes(svc, Type("NS.Old", orphanId));
            HasSteps(svc, stepId);

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.New" });

            Received.InOrder(() =>
            {
                svc.Delete("sdkmessageprocessingstep", stepId);
                svc.Delete("plugintype", orphanId);
            });
        }

        [Fact]
        public void DeleteOrphans_NeverDeletesCustomApiBackedType()
        {
            var svc    = Svc();
            var typeId = Guid.NewGuid();
            HasTypes(svc, Type("NS.Api", typeId));
            HasCustomApi(svc, typeId);

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.New" });

            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void DeleteOrphans_NeverDeletesCustomActionBackedType()
        {
            var svc    = Svc();
            var typeId = Guid.NewGuid();
            HasTypes(svc, Type("NS.Action", typeId));
            HasCustomAction(svc, typeId);

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.New" });

            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void DeleteOrphans_EmptyDesiredSet_DeletesNothing()
        {
            var svc = Svc();
            HasTypes(svc, Type("NS.A", Guid.NewGuid()), Type("NS.B", Guid.NewGuid()));

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), Array.Empty<string>());

            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void DeleteOrphans_DryRun_DeletesNothing()
        {
            var svc = Svc();
            HasTypes(svc, Type("NS.Old", Guid.NewGuid()));

            new PluginTypeRegistrar(svc).DeleteOrphans(Guid.NewGuid(), new[] { "NS.New" }, dryRun: true);

            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void DeleteOrphans_QueriesTypesForTheGivenAssembly()
        {
            var svc        = Svc();
            var assemblyId = Guid.NewGuid();
            HasTypes(svc, Type("NS.Old", Guid.NewGuid()));

            new PluginTypeRegistrar(svc).DeleteOrphans(assemblyId, new[] { "NS.New" });

            svc.Received(1).RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "plugintype" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "pluginassemblyid" && c.Values.Contains(assemblyId))));
        }
    }
}
