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
        private static IOrganizationService Svc(params string[] existingTypeNames)
        {
            var svc = Substitute.For<IOrganizationService>();
            var entities = existingTypeNames
                .Select(t => new Entity("plugintype", Guid.NewGuid()) { ["typename"] = t })
                .ToList();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(entities));
            return svc;
        }

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
            var svc        = Svc("NS.A");
            var assemblyId = Guid.NewGuid();

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
            svc.DidNotReceive().RetrieveMultiple(Arg.Any<QueryExpression>());
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
    }
}
