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
    public class StepRegistrarTests
    {
        // ── Shared fixture data ────────────────────────────────────────────────

        private static readonly Guid AssemblyId   = Guid.NewGuid();
        private static readonly Guid MsgId          = Guid.NewGuid();
        private static readonly Guid FilterId       = Guid.NewGuid();
        private static readonly Guid PluginTypeId   = Guid.NewGuid();
        private static readonly Guid SystemUserGuid = Guid.NewGuid();

        private const string MessageName  = "Create";
        private const string EntityName   = "account";

        // ── Helper: build a PluginStepDefinition ──────────────────────────────

        private static PluginStepDefinition MakeDef(
            string typeName  = "Ns.AccountCreate",
            string entity    = EntityName,
            string message   = MessageName,
            int    stage     = 20,
            bool   postImage = false)
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = typeName,
                Entity       = entity,
                Message      = message,
                Stage        = stage,
            };
            if (postImage)
                def.Images.Add(new ImageDefinition
                {
                    ImageType  = ImageType.Post,
                    Alias      = "PostImage",
                    Attributes = Array.Empty<string>()
                });
            return def;
        }

        // ── Helper: build and wire a default IOrganizationService mock ─────────

        /// <summary>
        /// Wires an <see cref="IOrganizationService"/> mock with:
        /// <list type="bullet">
        ///   <item>One sdkmessage ("Create")</item>
        ///   <item>One sdkmessagefilter (account + Create, iscustomprocessingstepallowed = true)</item>
        ///   <item>One plugintype for <see cref="AssemblyId"/></item>
        ///   <item>Zero existing sdkmessageprocessingstep / image records</item>
        ///   <item>Create returns a new Guid each call</item>
        /// </list>
        /// </summary>
        private static IOrganizationService BuildDefaultSvc(
            EntityCollection? existingSteps  = null,
            EntityCollection? existingImages = null)
        {
            var svc = Substitute.For<IOrganizationService>();

            // sdkmessage
            var msgEntity = new Entity("sdkmessage", MsgId) { ["name"] = MessageName };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity> { msgEntity }));

            // sdkmessagefilter
            var filterEntity = new Entity("sdkmessagefilter", FilterId)
            {
                ["sdkmessageid"]                   = new EntityReference("sdkmessage", MsgId),
                ["primaryobjecttypecode"]           = EntityName,
                ["iscustomprocessingstepallowed"]   = true
            };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessagefilter"))
               .Returns(new EntityCollection(new List<Entity> { filterEntity }));

            // sdkmessagefilter Retrieve (IsCustomStepAllowed)
            svc.Retrieve("sdkmessagefilter", FilterId, Arg.Any<ColumnSet>())
               .Returns(filterEntity);

            // plugintype
            var pluginTypeEntity = new Entity("plugintype", PluginTypeId) { ["typename"] = "Ns.AccountCreate" };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(new List<Entity> { pluginTypeEntity }));

            // existing steps
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstep"))
               .Returns(existingSteps ?? new EntityCollection());

            // existing images
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstepimage"))
               .Returns(existingImages ?? new EntityCollection());

            // custom APIs (none by default)
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "customapi"))
               .Returns(new EntityCollection());

            // custom actions / workflows (none by default)
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "workflow"))
               .Returns(new EntityCollection());

            // Create returns a fresh Guid
            svc.Create(Arg.Any<Entity>()).Returns(_ => Guid.NewGuid());

            // systemuser query
            var systemUserEntity = new Entity("systemuser", SystemUserGuid) { ["fullname"] = "SYSTEM" };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "systemuser" && q.Criteria.Conditions.Any(c => c.AttributeName == "fullname" && (string)c.Values[0] == "SYSTEM")))
               .Returns(new EntityCollection(new List<Entity> { systemUserEntity }));

            return svc;
        }

        private static StepRegistrar MakeRegistrar(IOrganizationService svc) =>
            new StepRegistrar(svc, NullLogger<StepRegistrar>.Instance);

        // ── Tests ──────────────────────────────────────────────────────────────

        [Fact]
        public void NoPluginTypes_ReturnsError_CreateNotCalled()
        {
            var svc = BuildDefaultSvc();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection()); // override: empty

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef() });

            result.Errors.Count.ShouldBeGreaterThan(0);
            result.Errors[0].ShouldContain(AssemblyId.ToString());
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void NewStep_CreatesStep_ResultCreatedIsOne()
        {
            var svc    = BuildDefaultSvc();
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef() });

            result.Created.ShouldBe(1);
            result.Errors.ShouldBeEmpty();
            // Only the step is created (no secure config record).
            svc.Received(1).Create(Arg.Any<Entity>());
        }

        [Fact]
        public void Sync_ResolvesFiltersByEntityName_NotByScanningEveryFilter()
        {
            // Symmetric to the adopt fix: registration must query sdkmessagefilter for the entities
            // its definitions target, not pull every filter in the org (more than one page of them).
            var svc = BuildDefaultSvc();

            MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(entity: "account") });

            svc.Received().RetrieveMultiple(Arg.Is<QueryExpression>(q =>
                q.EntityName == "sdkmessagefilter" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "primaryobjecttypecode" &&
                    c.Operator == ConditionOperator.In &&
                    c.Values.Contains("account"))));
        }

        [Fact]
        public void ExistingStep_UpdatesCalled_ResultUpdatedIsOne()
        {
            var def = MakeDef();
            var stepId = Guid.NewGuid();

            var existingStepEntity = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName
            };
            var existingSteps = new EntityCollection(new List<Entity> { existingStepEntity });

            var svc    = BuildDefaultSvc(existingSteps: existingSteps);
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Updated.ShouldBe(1);
            result.Created.ShouldBe(0);
            // Only the step is updated (no secure config record).
            svc.Received(1).Update(Arg.Any<Entity>());
            svc.DidNotReceive().Create(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstep"));
        }

        [Fact]
        public void OrphanStep_DeletedFromDataverse_ResultDeletedIsOne()
        {
            var orphanId = Guid.NewGuid();
            var orphanEntity = new Entity("sdkmessageprocessingstep", orphanId)
            {
                ["name"] = "Ns.OldPlugin | account | create | PreOperation"
            };

            // plugintype for orphan — must match so the query returns it
            var svc = Substitute.For<IOrganizationService>();

            var msgEntity = new Entity("sdkmessage", MsgId) { ["name"] = MessageName };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity> { msgEntity }));

            var filterEntity = new Entity("sdkmessagefilter", FilterId)
            {
                ["sdkmessageid"]                 = new EntityReference("sdkmessage", MsgId),
                ["primaryobjecttypecode"]         = EntityName,
                ["iscustomprocessingstepallowed"] = true
            };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessagefilter"))
               .Returns(new EntityCollection(new List<Entity> { filterEntity }));
            svc.Retrieve("sdkmessagefilter", FilterId, Arg.Any<ColumnSet>()).Returns(filterEntity);

            var pluginTypeEntity = new Entity("plugintype", PluginTypeId) { ["typename"] = "Ns.AccountCreate" };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(new List<Entity> { pluginTypeEntity }));

            // Existing steps include an orphan (type not in definitions)
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstep"))
               .Returns(new EntityCollection(new List<Entity> { orphanEntity }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessageprocessingstepimage"))
               .Returns(new EntityCollection());

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "customapi"))
               .Returns(new EntityCollection());
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "workflow"))
               .Returns(new EntityCollection());

            svc.Create(Arg.Any<Entity>()).Returns(_ => Guid.NewGuid());

            // definitions don't include the old plugin type
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef() }, deleteOrphaned: true);

            result.Deleted.ShouldBe(1);
            svc.Received(1).Delete("sdkmessageprocessingstep", orphanId);
        }

        [Fact]
        public void OrphanStep_DeleteOrphanedFalse_Warns_NotDeleted()
        {
            // Deletion is opt-in: by default an orphan is reported as a warning, not removed.
            var orphanId = Guid.NewGuid();
            var orphan   = new Entity("sdkmessageprocessingstep", orphanId)
            {
                ["name"]         = "Ns.OldPlugin | account | create | PreOperation",
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", MsgId),
            };

            var svc    = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { orphan }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, Array.Empty<PluginStepDefinition>());

            result.Deleted.ShouldBe(0);
            result.Warnings.ShouldContain(w => w.Contains("Ns.OldPlugin | account | create | PreOperation"));
            svc.DidNotReceive().Delete("sdkmessageprocessingstep", orphanId);
        }

        [Fact]
        public void OrphanStep_OnCustomApiMessage_NotDeleted()
        {
            // A Custom API's backing step lives on this assembly's plugin types but is owned by the
            // Custom API definition — it must never be treated as an orphan, even when pruning.
            var customApiMsgId = Guid.NewGuid();
            var stepId         = Guid.NewGuid();

            var orphanOnCustomApi = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"]         = "MyCustomApi.Handler",
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", customApiMsgId),
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { orphanOnCustomApi }));
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "customapi"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("customapi", Guid.NewGuid())
                   {
                       ["sdkmessageid"] = new EntityReference("sdkmessage", customApiMsgId)
                   }
               }));

            var result = MakeRegistrar(svc).Sync(AssemblyId, Array.Empty<PluginStepDefinition>(), deleteOrphaned: true);

            result.Deleted.ShouldBe(0);
            svc.DidNotReceive().Delete("sdkmessageprocessingstep", stepId);
        }

        [Fact]
        public void OrphanStep_OnCustomActionMessage_NotDeleted()
        {
            // A Custom Action (workflow, category = Action) owns an SDK message named after its
            // uniquename. A step on that message backs the action and must never be orphan-deleted.
            var actionMsgId           = Guid.NewGuid();
            const string actionUnique = "new_MyAction";
            var stepId                = Guid.NewGuid();

            var orphanOnAction = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"]         = "MyAction.Handler",
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", actionMsgId),
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { orphanOnAction }));

            // The action's message must be resolvable by name (workflow.uniquename → sdkmessage.name).
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("sdkmessage", MsgId)       { ["name"] = MessageName },
                   new Entity("sdkmessage", actionMsgId) { ["name"] = actionUnique },
               }));
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "workflow"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("workflow", Guid.NewGuid()) { ["uniquename"] = actionUnique }
               }));

            var result = MakeRegistrar(svc).Sync(AssemblyId, Array.Empty<PluginStepDefinition>(), deleteOrphaned: true);

            result.Deleted.ShouldBe(0);
            svc.DidNotReceive().Delete("sdkmessageprocessingstep", stepId);
        }

        [Fact]
        public void ConsumedStep_DeleteOrphanedTrue_NotDeleted()
        {
            // Pruning must only remove genuine orphans — a step matching a definition is never deleted.
            var def      = MakeDef();
            var stepId   = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId) { ["name"] = def.StepName };

            var svc    = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def }, deleteOrphaned: true);

            result.Deleted.ShouldBe(0);
            svc.DidNotReceive().Delete("sdkmessageprocessingstep", stepId);
        }

        [Fact]
        public void UnknownMessage_AddsWarning_CreateNotCalled()
        {
            var svc = BuildDefaultSvc();
            // Override: no messages
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection());

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(message: "CustomMessage") });

            result.Warnings.ShouldContain(w => w.Contains("CustomMessage"));
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void NoSdkMessageFilter_AddsWarning_CreateNotCalled()
        {
            var svc = BuildDefaultSvc();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessagefilter"))
               .Returns(new EntityCollection()); // no filters

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef() });

            result.Warnings.Count.ShouldBeGreaterThan(0);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        // ── Dataverse faults during create/update are attributed to the plugin ──

        [Fact]
        public void StepCreateThrows_ErrorNamesPluginClass_AndKeepsDataverseMessage()
        {
            // Mirrors a real Dataverse fault (e.g. a bad filtering attribute). The failure must be
            // attributed to the plugin class instead of bubbling up as a bare, unattributed error.
            const string dataverseError =
                "'Account' entity doesn't contain attribute with Name = 'subject'.";

            var svc = BuildDefaultSvc();
            svc.Create(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstep"))
               .Returns(_ => throw new InvalidOperationException(dataverseError));

            // Must not throw — the fault is captured as a result error, not propagated.
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(typeName: "Ns.AccountCreate") });

            result.Created.ShouldBe(0);
            result.Errors.ShouldContain(e =>
                e.Contains("Ns.AccountCreate") && e.Contains(dataverseError));
        }

        [Fact]
        public void OneStepFails_RemainingStepsStillRegister()
        {
            // A single bad registration must not abort the whole run: later steps still process.
            var svc = BuildDefaultSvc();
            // Fail only the PreOperation (stage 20) step; the PostOperation (stage 40) step succeeds.
            svc.Create(Arg.Is<Entity>(e =>
                    e.LogicalName == "sdkmessageprocessingstep" &&
                    e.Contains("stage") &&
                    e.GetAttributeValue<OptionSetValue>("stage").Value == 20))
               .Returns(_ => throw new InvalidOperationException("boom"));

            var result = MakeRegistrar(svc).Sync(
                AssemblyId, new[] { MakeDef(stage: 20), MakeDef(stage: 40) });

            result.Errors.Count.ShouldBe(1);
            result.Created.ShouldBe(1); // the PostOperation step still registered
        }

        // ── Entity-less (global) messages: Associate / Disassociate ────────────

        /// <summary>Builds a service whose only SDK message is an entity-less <paramref name="message"/>.</summary>
        private static IOrganizationService BuildEntitylessSvc(string message)
        {
            var svc = BuildDefaultSvc();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = message }
               }));
            return svc;
        }

        [Fact]
        public void EntitylessMessage_NoFilterRequired_StepCreatedWithNullFilter()
        {
            // Global messages (Associate/Disassociate) have no entity and no sdkmessagefilter,
            // so the step must register with a null sdkmessagefilterid rather than be skipped.
            var svc    = BuildEntitylessSvc("Associate");
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(entity: "", message: "Associate") });

            result.Created.ShouldBe(1);
            result.Warnings.ShouldBeEmpty();
            result.Errors.ShouldBeEmpty();

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<EntityReference>("sdkmessagefilterid") == null));
        }

        [Fact]
        public void EntitylessMessage_DoesNotValidateFilter()
        {
            // With no entity there is no filter to validate, so IsCustomStepAllowed must not run.
            var svc = BuildEntitylessSvc("Disassociate");

            MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(entity: "", message: "Disassociate") });

            svc.DidNotReceive().Retrieve("sdkmessagefilter", Arg.Any<Guid>(), Arg.Any<ColumnSet>());
        }

        [Fact]
        public void EntitySpecificStep_StillRequiresFilter_SkippedWhenMissing()
        {
            // Regression guard: entity-specific steps must still be skipped when no filter exists.
            var svc = BuildDefaultSvc();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessagefilter"))
               .Returns(new EntityCollection()); // no filters

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef(entity: "account") });

            result.Created.ShouldBe(0);
            result.Warnings.ShouldContain(w => w.Contains("No sdkmessagefilter"));
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void DryRun_CountsCorrect_ServiceNotMutated()
        {
            var svc    = BuildDefaultSvc();
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { MakeDef() }, dryRun: true);

            result.Created.ShouldBe(1);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
            svc.DidNotReceive().Update(Arg.Any<Entity>());
            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }

        [Fact]
        public void PostImageOnNonPostOperation_AddsWarning_ImageNotCreated()
        {
            // Stage 20 = PreOperation — PostImage is not valid here
            var def = MakeDef(stage: 20, postImage: true);
            var svc = BuildDefaultSvc();

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Warnings.ShouldContain(w => w.Contains("PostImage") || w.Contains("PostOperation"));
            // image Create should NOT be called (only the step is created)
            svc.Received(1).Create(Arg.Any<Entity>());
            svc.DidNotReceive().Create(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstepimage"));
        }

        // ── RunAsUser / impersonation ──────────────────────────────────────────

        [Fact]
        public void RunAsUser_NonEmpty_SetsImpersonatingUserId()
        {
            var userId = Guid.NewGuid();
            var def    = MakeDef();
            def.RunAsUser = userId;

            var svc = BuildDefaultSvc();
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid") != null &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid")!.Id == userId));
        }

        [Fact]
        public void RunAsUser_GuidEmpty_SetsImpersonatingUserIdToSystemUser()
        {
            // Guid.Empty = RunAsSystem → impersonatinguserid written as SystemUserGuid
            var def = MakeDef();
            def.RunAsUser = Guid.Empty;

            var svc = BuildDefaultSvc();
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid") != null &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid")!.Id == SystemUserGuid));
        }

        [Fact]
        public void RunAsUser_Null_SetsImpersonatingUserIdToNull()
        {
            // null = calling user → impersonatinguserid set to null explicitly to clear it
            var def = MakeDef();
            def.RunAsUser = null;

            var svc = BuildDefaultSvc();
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("impersonatinguserid") &&
                e["impersonatinguserid"] == null));
        }

        [Fact]
        public void AddRunAsSystem_ToExistingCallingUserStep_ShouldUpdate()
        {
            var def = MakeDef();
            def.RunAsUser = Guid.Empty; // RunAsSystem = true

            var stepId = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", MsgId),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", FilterId),
                ["stage"] = new OptionSetValue(20),
                ["mode"] = new OptionSetValue(0),
                ["rank"] = 1,
                ["impersonatinguserid"] = null // Calling User
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Updated.ShouldBe(1);
            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid") != null &&
                e.GetAttributeValue<EntityReference>("impersonatinguserid").Id == SystemUserGuid));
        }

        [Fact]
        public void RemoveRunAsSystem_FromExistingSystemStep_ShouldUpdate()
        {
            var def = MakeDef();
            def.RunAsUser = null; // Calling User

            var stepId = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", MsgId),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", FilterId),
                ["stage"] = new OptionSetValue(20),
                ["mode"] = new OptionSetValue(0),
                ["rank"] = 1,
                ["impersonatinguserid"] = new EntityReference("systemuser", SystemUserGuid) // SYSTEM
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Updated.ShouldBe(1);
            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("impersonatinguserid") &&
                e["impersonatinguserid"] == null));
        }

        [Fact]
        public void RemoveRunAsUser_FromExistingUserStep_ShouldUpdate()
        {
            var def = MakeDef();
            def.RunAsUser = null; // Calling User

            var stepId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["plugintypeid"] = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"] = new EntityReference("sdkmessage", MsgId),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", FilterId),
                ["stage"] = new OptionSetValue(20),
                ["mode"] = new OptionSetValue(0),
                ["rank"] = 1,
                ["impersonatinguserid"] = new EntityReference("systemuser", userId)
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Updated.ShouldBe(1);
            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("impersonatinguserid") &&
                e["impersonatinguserid"] == null));
        }

        // ── Configuration ──────────────────────────────────────────────────────

        [Fact]
        public void Configuration_WrittenToStep()
        {
            var def = MakeDef();
            def.Configuration = "cfg";

            var svc = BuildDefaultSvc();
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<string>("configuration") == "cfg"));
        }

        // ── Filtering attributes ───────────────────────────────────────────────

        [Fact]
        public void FilteringAttributes_Provided_WrittenToStep()
        {
            var def = MakeDef();
            def.FilteringAttributes = new[] { "name", "telephone1" };

            var svc = BuildDefaultSvc();
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.GetAttributeValue<string>("filteringattributes") == "name,telephone1"));
        }

        [Fact]
        public void FilteringAttributes_Removed_ClearedOnUpdate()
        {
            // Removing all filtering attributes must clear the column on update, not leave a stale
            // value behind. The step entity must explicitly carry filteringattributes = null.
            var def      = MakeDef(); // no filtering attributes
            var stepId   = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("filteringattributes") &&
                e.GetAttributeValue<string>("filteringattributes") == null));
        }

        [Fact]
        public void Description_Removed_ClearedOnUpdate()
        {
            var def = MakeDef();
            def.Description = null;

            var stepId = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["description"] = "old-description"
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("description") &&
                e["description"] == null));
        }

        [Fact]
        public void Configuration_Removed_ClearedOnUpdate()
        {
            var def = MakeDef();
            def.Configuration = null;

            var stepId = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["configuration"] = "old-config"
            };

            var svc = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" &&
                e.Contains("configuration") &&
                e["configuration"] == null));
        }

        // ── Image attributes ───────────────────────────────────────────────────

        [Fact]
        public void ImageAttributes_Removed_ClearedOnUpdate()
        {
            // An image that drops to capture-all (no attributes) must clear the column on update,
            // not leave a stale list behind.
            var def = MakeDef(stage: 40);
            def.Images.Add(new ImageDefinition
            {
                ImageType  = ImageType.Post,
                Alias      = "PostImage",
                Attributes = Array.Empty<string>()
            });

            var stepId   = Guid.NewGuid();
            var imageId  = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["rank"] = def.ExecutionOrder  // step fields match; only the image differs
            };
            var existingImage = new Entity("sdkmessageprocessingstepimage", imageId)
            {
                ["imagetype"]   = new OptionSetValue(1),
                ["entityalias"] = "PostImage",
                ["attributes"]  = "name,telephone1"
            };

            var svc = BuildDefaultSvc(
                existingSteps:  new EntityCollection(new List<Entity> { existing }),
                existingImages: new EntityCollection(new List<Entity> { existingImage }));

            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstepimage" &&
                e.Contains("attributes") &&
                e.GetAttributeValue<string>("attributes") == null));
        }

        // ── Image message property name ────────────────────────────────────────

        [Fact]
        public void CreateStep_PostImage_UsesIdMessagePropertyName()
        {
            // The Create message binds post-images via 'Id'. dvx previously hardcoded 'Target',
            // which Dataverse rejects ("Message property name 'Target' is not valid on message Create").
            var def = MakeDef(stage: 40, postImage: true); // message defaults to "Create"
            var svc = BuildDefaultSvc();

            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstepimage" &&
                e.GetAttributeValue<string>("messagepropertyname") == "Id"));
        }

        [Fact]
        public void UpdateStep_PostImage_UsesTargetMessagePropertyName()
        {
            var def = MakeDef(message: "Update", stage: 40, postImage: true);
            var svc = BuildDefaultSvc();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "sdkmessage"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("sdkmessage", MsgId) { ["name"] = "Update" }
               }));

            MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.Received().Create(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstepimage" &&
                e.GetAttributeValue<string>("messagepropertyname") == "Target"));
        }

        [Fact]
        public void MessageWithoutImageSupport_SkipsImage_AddsWarning()
        {
            // Associate has no valid image property name. The image must be skipped (not registered
            // with an invalid property name), and the user warned.
            var def = MakeDef(entity: "", message: "Associate", stage: 40, postImage: true);
            var svc = BuildEntitylessSvc("Associate");

            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            svc.DidNotReceive().Create(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstepimage"));
            result.Warnings.ShouldContain(w => w.Contains("does not support entity images"));
        }

        // ── Change detection (skip unchanged) ──────────────────────────────────

        [Fact]
        public void ExistingStep_Unchanged_Skipped_NotUpdated()
        {
            // A re-sync with no changes must skip the step (counted Skipped), not call Update.
            var def      = MakeDef(); // rank 1, no filtering / description / config / images
            var stepId   = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName,
                ["rank"] = def.ExecutionOrder  // matches → unchanged
            };

            var svc    = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Skipped.ShouldBe(1);
            result.Updated.ShouldBe(0);
            svc.DidNotReceive().Update(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstep"));
        }

        // ── Identity-based adoption ────────────────────────────────────────────

        [Fact]
        public void IdentityMatch_AdoptsHandRegisteredStepInPlace()
        {
            // A pre-existing step with a non-dvx name but matching identity must be UPDATED
            // (renamed) in place, not deleted and recreated.
            var def     = MakeDef(); // stage 20, mode 0, type Ns.AccountCreate
            var stepId  = Guid.NewGuid();
            var existing = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"]               = "Hand-registered: Create of account",
                ["plugintypeid"]       = new EntityReference("plugintype", PluginTypeId),
                ["sdkmessageid"]       = new EntityReference("sdkmessage", MsgId),
                ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", FilterId),
                ["stage"]              = new OptionSetValue(20),
                ["mode"]               = new OptionSetValue(0),
            };

            var svc    = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existing }));
            var result = MakeRegistrar(svc).Sync(AssemblyId, new[] { def });

            result.Updated.ShouldBe(1);
            result.Created.ShouldBe(0);
            result.Deleted.ShouldBe(0);
            svc.Received(1).Update(Arg.Is<Entity>(e =>
                e.LogicalName == "sdkmessageprocessingstep" && e.Id == stepId));
            svc.DidNotReceive().Create(Arg.Is<Entity>(e => e.LogicalName == "sdkmessageprocessingstep"));
            svc.DidNotReceive().Delete("sdkmessageprocessingstep", stepId);
        }

        // ── solutionUniqueName ─────────────────────────────────────────────────

        private static StepRegistrar MakeRegistrarWithSolution(
            IOrganizationService svc, SolutionService solutionService) =>
            new StepRegistrar(svc, NullLogger<StepRegistrar>.Instance, solutionService);

        [Fact]
        public void SolutionProvided_ValidateSolutionCalledBeforeSync()
        {
            var svc             = BuildDefaultSvc();
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { MakeDef() }, solutionUniqueName: "MySolution");

            solutionService.Received(1).ValidateSolutionExists("MySolution", Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_NewStep_AddStepToSolutionCalled()
        {
            var svc             = BuildDefaultSvc();
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { MakeDef() }, solutionUniqueName: "MySolution");

            solutionService.Received(1).AddStepToSolution(Arg.Any<Guid>(), "MySolution", Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_ExistingStep_AddStepToSolutionCalled()
        {
            var def    = MakeDef();
            var stepId = Guid.NewGuid();
            var existingStepEntity = new Entity("sdkmessageprocessingstep", stepId)
            {
                ["name"] = def.StepName
            };
            var svc             = BuildDefaultSvc(existingSteps: new EntityCollection(new List<Entity> { existingStepEntity }));
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { def }, solutionUniqueName: "MySolution");

            solutionService.Received(1).AddStepToSolution(stepId, "MySolution", Arg.Any<bool>());
        }

        [Fact]
        public void SolutionNotProvided_AddStepToSolutionNotCalled()
        {
            var svc             = BuildDefaultSvc();
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { MakeDef() });

            solutionService.DidNotReceive().AddStepToSolution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_DryRun_AddStepToSolutionNotCalled()
        {
            var svc             = BuildDefaultSvc();
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { MakeDef() }, dryRun: true, solutionUniqueName: "MySolution");

            solutionService.DidNotReceive().AddStepToSolution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_VerboseTrue_PassedThroughToSolutionService()
        {
            var svc             = BuildDefaultSvc();
            var solutionService = Substitute.For<SolutionService>(svc);
            var registrar       = MakeRegistrarWithSolution(svc, solutionService);

            registrar.Sync(AssemblyId, new[] { MakeDef() }, solutionUniqueName: "MySolution", verbose: true);

            solutionService.Received(1).ValidateSolutionExists("MySolution", true);
            solutionService.Received(1).AddStepToSolution(Arg.Any<Guid>(), "MySolution", true);
        }
    }
}
