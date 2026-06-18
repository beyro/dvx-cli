using dvx.Models;
using dvx.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class StepImporterTests
    {
        private static readonly Guid AssemblyId   = Guid.NewGuid();
        private static readonly Guid MsgId        = Guid.NewGuid();
        private static readonly Guid FilterId     = Guid.NewGuid();
        private static readonly Guid PluginTypeId = Guid.NewGuid();
        private static readonly Guid StepId       = Guid.NewGuid();
        private static readonly Guid SystemUserGuid = Guid.NewGuid();

        private const string TypeName = "Ns.AccountCreate";

        /// <summary>
        /// Wires a service with one plugin type, message (Create), filter (account), and one
        /// fully-populated step. Pass <paramref name="step"/>/<paramref name="images"/> to override.
        /// </summary>
        private static IOrganizationService BuildSvc(
            Entity? step = null,
            EntityCollection? images = null,
            string entity = "account",
            string message = "Create")
        {
            var svc = Substitute.For<IOrganizationService>();

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new("plugintype", PluginTypeId) { ["typename"] = TypeName }
               }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new("sdkmessage", MsgId) { ["name"] = message }
               }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessagefilter"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new("sdkmessagefilter", FilterId)
                   {
                       ["sdkmessageid"]          = new EntityReference("sdkmessage", MsgId),
                       ["primaryobjecttypecode"] = entity
                   }
               }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstep"))
               .Returns(new EntityCollection(step is null ? new() : new List<Entity> { step }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstepimage"))
               .Returns(images ?? new EntityCollection());

            // systemuser query
            var systemUserEntity = new Entity("systemuser", SystemUserGuid) { ["fullname"] = "SYSTEM" };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "systemuser" && q.Criteria.Conditions.Any(c => c.AttributeName == "fullname" && (string)c.Values[0] == "SYSTEM")))
               .Returns(new EntityCollection(new List<Entity> { systemUserEntity }));

            return svc;
        }

        private static Entity FullStep() => new("sdkmessageprocessingstep", StepId)
        {
            ["name"]                = "Old hand-registered name",
            ["plugintypeid"]        = new EntityReference("plugintype", PluginTypeId),
            ["sdkmessageid"]        = new EntityReference("sdkmessage", MsgId),
            ["sdkmessagefilterid"]  = new EntityReference("sdkmessagefilter", FilterId),
            ["stage"]               = new OptionSetValue(40),
            ["mode"]                = new OptionSetValue(0),
            ["rank"]                = 1,
            ["filteringattributes"] = "name,statuscode",
            ["description"]         = "desc",
            ["configuration"]       = "unsecure-cfg",
        };

        private static StepImporter Importer(IOrganizationService svc) =>
            new(svc, NullLogger<StepImporter>.Instance);

        // ── Tests ────────────────────────────────────────────────────────────────

        [Fact]
        public void Import_MapsScalarFields()
        {
            var svc = BuildSvc(step: FullStep());
            var def = Importer(svc).Import(AssemblyId).Definitions.ShouldHaveSingleItem();

            def.TypeFullName.ShouldBe(TypeName);
            def.Entity.ShouldBe("account");
            def.Message.ShouldBe("Create");
            def.Stage.ShouldBe(40);
            def.Mode.ShouldBe(0);
            def.ExecutionOrder.ShouldBe(1);
            def.Description.ShouldBe("desc");
            def.Configuration.ShouldBe("unsecure-cfg");
            def.FilteringAttributes.ShouldBe(new[] { "name", "statuscode" });
        }

        [Fact]
        public void Import_PreservesCustomImageAlias()
        {
            var images = new EntityCollection(new List<Entity>
            {
                new("sdkmessageprocessingstepimage")
                {
                    ["imagetype"]   = new OptionSetValue(0), // Pre
                    ["entityalias"] = "Target",
                    ["attributes"]  = "name,telephone1"
                }
            });

            var svc = BuildSvc(step: FullStep(), images: images);
            var def = Importer(svc).Import(AssemblyId).Definitions.ShouldHaveSingleItem();

            var img = def.Images.ShouldHaveSingleItem();
            img.ImageType.ShouldBe(ImageType.Pre);
            img.Alias.ShouldBe("Target");
            img.Attributes.ShouldBe(new[] { "name", "telephone1" });
        }

        [Fact]
        public void Import_NoPluginTypes_WarnsAndReturnsEmpty()
        {
            var svc = BuildSvc(step: FullStep());
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection());

            var result = Importer(svc).Import(AssemblyId);

            result.Definitions.ShouldBeEmpty();
            result.Warnings.ShouldNotBeEmpty();
        }

        [Fact]
        public void Import_GlobalMessageStep_AdoptsWithEmptyEntity()
        {
            // Global messages such as Associate / Disassociate register with no sdkmessagefilterid.
            // They are representable as [PluginStep("", ...)] and must be adopted, not skipped.
            var step = FullStep();
            step.Attributes.Remove("sdkmessagefilterid");

            var result = Importer(BuildSvc(step: step, message: "Associate")).Import(AssemblyId);

            var def = result.Definitions.ShouldHaveSingleItem();
            def.Entity.ShouldBe(string.Empty);
            def.Message.ShouldBe("Associate");
            result.Warnings.ShouldBeEmpty();
        }

        [Fact]
        public void Import_SystemImpersonation_MapsToGuidEmpty()
        {
            var step = FullStep();
            step["impersonatinguserid"] = new EntityReference("systemuser", SystemUserGuid);

            var svc = BuildSvc(step: step);
            var def = Importer(svc).Import(AssemblyId).Definitions.ShouldHaveSingleItem();

            def.RunAsUser.ShouldBe(Guid.Empty);
        }

        [Fact]
        public void Import_SpecificUserImpersonation_MapsToUserId()
        {
            var userId = Guid.NewGuid();
            var step = FullStep();
            step["impersonatinguserid"] = new EntityReference("systemuser", userId);

            var svc = BuildSvc(step: step);
            var def = Importer(svc).Import(AssemblyId).Definitions.ShouldHaveSingleItem();

            def.RunAsUser.ShouldBe(userId);
        }
    }
}
