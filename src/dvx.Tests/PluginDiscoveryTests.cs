using dvx.PluginAttributes;
using dvx.Models;
using dvx.Services;
using dvx.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Shouldly;
using Xunit;

// ── Test fixture plugin classes ────────────────────────────────────────────────
// These live in the test assembly. PluginDiscovery.Discover() is called with
// typeof(PluginDiscoveryTests).Assembly.Location so MetadataLoadContext loads
// this DLL from disk and finds all the fixtures below.
// All required assemblies (dvx.PluginAttributes, Microsoft.Xrm.Sdk) are
// co-located in the test bin output folder.

namespace dvx.Tests.Fixtures
{
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation)]
    public class TestPluginSingle : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation)]
    [PluginStep(Entity = "contact", Message = "Update", Stage = Stage.PostOperation)]
    public class TestPluginMulti : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    [PluginStep(Entity = "account", Message = "Update", Stage = Stage.PreOperation,
        FilteringAttributes = new[] { "name", "statuscode" })]
    public class TestPluginFiltering : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation, UsePreImage = true,
        PreImageAttributes = new[] { "name", "telephone1" })]
    public class TestPluginPreImage : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PostOperation, UsePostImage = true,
        PostImageAttributes = new[] { "name" })]
    public class TestPluginPostImage : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Unsecure configuration string
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation,
        Configuration = "cfg-value")]
    public class TestPluginConfig : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Custom pre-image alias
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation,
        UsePreImage = true, PreImageAlias = "Target", PreImageAttributes = new[] { "name" })]
    public class TestPluginCustomAlias : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Specific user impersonation via RunAsUser GUID string
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation,
        RunAsUser = "a1b2c3d4-0000-0000-0000-000000000001")]
    public class TestPluginRunAsUser : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Run as SYSTEM user via RunAsSystem flag
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation,
        RunAsSystem = true)]
    public class TestPluginRunAsSystem : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // IPlugin with no [PluginStep] attribute — should produce a warning and be excluded
    public class TestPluginNoStep : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Abstract class — should be silently skipped
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation)]
    public abstract class TestPluginAbstract : IPlugin
    {
        public abstract void Execute(IServiceProvider serviceProvider);
    }

    // Not an IPlugin at all — should be silently skipped
    [PluginStep(Entity = "account", Message = "Create", Stage = Stage.PreOperation)]
    public class NotAPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // ── Positional constructor syntax (matches the README + example plugins) ────
    // [PluginStep("entity", "Message", Stage.X)] — entity/message/stage are constructor
    // arguments, not named arguments.

    [PluginStep("account", "Create", Stage.PreOperation, Description = "positional")]
    public class TestPluginPositional : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Entity-less (global) message via positional syntax — empty entity string.
    [PluginStep("", "Associate", Stage.PostOperation)]
    public class TestPluginPositionalGlobal : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Positional entity/message/stage mixed with named arguments.
    [PluginStep("account", "Update", Stage.PostOperation, Async = true,
        FilteringAttributes = new[] { "name" })]
    public class TestPluginPositionalMixed : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Unsecure configuration via named arguments.
    [PluginStep("phonecall", "Create", Stage.PostOperation,
        Configuration = "<config>unsecure</config>")]
    public class TestPluginConfiguration : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // Custom API implementation — marked [CustomApi], no [PluginStep]. Must be skipped silently.
    [CustomApi]
    public class TestPluginCustomApi : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }

    // [CustomApi] takes precedence over [PluginStep] on the same class — must still be excluded.
    [CustomApi]
    [PluginStep("account", "Create", Stage.PostOperation)]
    public class TestPluginCustomApiWithStep : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider) { }
    }
}

namespace dvx.Tests
{
    public class PluginDiscoveryTests
    {
        private static readonly string TestAssemblyPath =
            typeof(PluginDiscoveryTests).Assembly.Location;

        private static List<PluginStepDefinition> Discover() =>
            new PluginDiscovery(NullLogger<PluginDiscovery>.Instance).Discover(TestAssemblyPath);

        // ── Single step ────────────────────────────────────────────────────────

        [Fact]
        public void SingleStep_FieldsPopulatedCorrectly()
        {
            var defs = Discover();
            var def  = defs.Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginSingle)));

            def.Entity.ShouldBe("account");
            def.Message.ShouldBe("Create");
            def.Stage.ShouldBe(20); // PreOperation = 20
        }

        // ── Positional constructor syntax ──────────────────────────────────────

        [Fact]
        public void PositionalSyntax_EntityMessageStagePopulated()
        {
            // Regression: positional [PluginStep("account", "Create", Stage.PreOperation)] must
            // populate Entity/Message/Stage from constructor arguments, not be left empty.
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPositional)));

            def.Entity.ShouldBe("account");
            def.Message.ShouldBe("Create");
            def.Stage.ShouldBe(20); // PreOperation = 20
        }

        [Fact]
        public void PositionalSyntax_NamedArgsStillApplied()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPositional)));

            def.Description.ShouldBe("positional");
        }

        [Fact]
        public void PositionalSyntax_EntitylessGlobalMessage_MessagePopulated_EntityEmpty()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPositionalGlobal)));

            def.Entity.ShouldBe("");
            def.Message.ShouldBe("Associate");
            def.Stage.ShouldBe(40); // PostOperation = 40
        }

        [Fact]
        public void PositionalSyntax_MixedWithNamedArgs()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPositionalMixed)));

            def.Entity.ShouldBe("account");
            def.Message.ShouldBe("Update");
            def.Mode.ShouldBe(1); // Async = true → mode 1
            def.FilteringAttributes.ShouldBe(new[] { "name" });
        }

        // ── Configuration ──────────────────────────────────────────────────────

        [Fact]
        public void Configuration_PopulatedFromNamedArgs()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginConfiguration)));

            def.Configuration.ShouldBe("<config>unsecure</config>");
        }

        [Fact]
        public void Configuration_OmittedByDefault_IsNull()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginSingle)));

            def.Configuration.ShouldBeNull();
        }

        // ── Multiple attributes on one class ───────────────────────────────────

        [Fact]
        public void MultiStep_ProducesTwoDefinitions()
        {
            var defs = Discover()
                .Where(d => d.TypeFullName!.EndsWith(nameof(TestPluginMulti)))
                .ToList();

            defs.Count.ShouldBe(2);
        }

        [Fact]
        public void MultiStep_BothEntitiesPresent()
        {
            var defs = Discover()
                .Where(d => d.TypeFullName!.EndsWith(nameof(TestPluginMulti)))
                .ToList();

            defs.ShouldContain(d => d.Entity == "account" && d.Message == "Create");
            defs.ShouldContain(d => d.Entity == "contact" && d.Message == "Update");
        }

        // ── Filtering attributes ───────────────────────────────────────────────

        [Fact]
        public void FilteringAttributes_PopulatedCorrectly()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginFiltering)));

            def.FilteringAttributes.ShouldBe(new[] { "name", "statuscode" });
        }

        // ── Pre-image ──────────────────────────────────────────────────────────

        [Fact]
        public void PreImage_AliasIsPreImage()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPreImage)));

            var img = def.Images.ShouldHaveSingleItem();
            img.ImageType.ShouldBe(ImageType.Pre);
            img.Alias.ShouldBe("PreImage");
        }

        [Fact]
        public void PreImage_AttributesPopulated()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPreImage)));

            def.Images[0].Attributes.ShouldBe(new[] { "name", "telephone1" });
        }

        // ── Post-image ─────────────────────────────────────────────────────────

        [Fact]
        public void PostImage_AliasIsPostImage()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPostImage)));

            var img = def.Images.ShouldHaveSingleItem();
            img.ImageType.ShouldBe(ImageType.Post);
            img.Alias.ShouldBe("PostImage");
        }

        [Fact]
        public void PostImage_AttributesPopulated()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginPostImage)));

            def.Images[0].Attributes.ShouldBe(new[] { "name" });
        }

        // ── Config / custom alias ──────────────────────────────────────────────

        [Fact]
        public void Configuration_Populated()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginConfig)));

            def.Configuration.ShouldBe("cfg-value");
        }

        [Fact]
        public void CustomPreImageAlias_Populated()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginCustomAlias)));

            var img = def.Images.ShouldHaveSingleItem();
            img.Alias.ShouldBe("Target");
            img.Attributes.ShouldBe(new[] { "name" });
        }

        // ── RunAsUser / RunAsSystem (MetadataLoadContext) ──────────────────────

        [Fact]
        public void RunAsUser_GuidStringParsedCorrectly()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginRunAsUser)));

            def.RunAsUser.ShouldBe(new Guid("a1b2c3d4-0000-0000-0000-000000000001"));
        }

        [Fact]
        public void RunAsSystem_SetsRunAsUserToGuidEmpty()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginRunAsSystem)));

            def.RunAsUser.ShouldBe(Guid.Empty);
        }

        [Fact]
        public void RunAsUser_OmittedByDefault_IsNull()
        {
            var def = Discover()
                .Single(d => d.TypeFullName!.EndsWith(nameof(TestPluginSingle)));

            def.RunAsUser.ShouldBeNull();
        }

        // ── ResolveImpersonatingUser unit tests ────────────────────────────────

        [Fact]
        public void Resolve_NeitherSet_ReturnsNull()
        {
            var result = PluginDiscovery.ResolveImpersonatingUser(false, null, "T");
            result.ShouldBeNull();
        }

        [Fact]
        public void Resolve_RunAsSystem_ReturnsGuidEmpty()
        {
            var result = PluginDiscovery.ResolveImpersonatingUser(true, null, "T");
            result.ShouldBe(Guid.Empty);
        }

        [Fact]
        public void Resolve_ValidRunAsUser_ReturnsParsedGuid()
        {
            var id     = Guid.NewGuid();
            var result = PluginDiscovery.ResolveImpersonatingUser(false, id.ToString(), "T");
            result.ShouldBe(id);
        }

        [Fact]
        public void Resolve_BothSet_Throws()
        {
            Should.Throw<InvalidOperationException>(() =>
                PluginDiscovery.ResolveImpersonatingUser(true, Guid.NewGuid().ToString(), "T"))
                .Message.ShouldContain("cannot both be set");
        }

        [Fact]
        public void Resolve_InvalidGuidString_Throws()
        {
            Should.Throw<InvalidOperationException>(() =>
                PluginDiscovery.ResolveImpersonatingUser(false, "not-a-guid", "T"))
                .Message.ShouldContain("not a valid GUID");
        }

        [Fact]
        public void Resolve_EmptyString_ReturnsNull()
        {
            // Empty string = attribute not meaningfully set → treat as omitted → calling user
            var result = PluginDiscovery.ResolveImpersonatingUser(false, "", "T");
            result.ShouldBeNull();
        }

        // ── Exclusion rules ────────────────────────────────────────────────────

        [Fact]
        public void UnattributedIPlugin_ExcludedFromResults()
        {
            var defs = Discover();
            defs.ShouldNotContain(d => d.TypeFullName!.EndsWith(nameof(TestPluginNoStep)));
        }

        [Fact]
        public void AbstractClass_ExcludedFromResults()
        {
            var defs = Discover();
            defs.ShouldNotContain(d => d.TypeFullName!.EndsWith(nameof(TestPluginAbstract)));
        }

        [Fact]
        public void NonIPluginClass_ExcludedFromResults()
        {
            var defs = Discover();
            defs.ShouldNotContain(d => d.TypeFullName!.EndsWith(nameof(NotAPlugin)));
        }

        [Fact]
        public void CustomApiClass_ExcludedFromResults()
        {
            var defs = Discover();
            defs.ShouldNotContain(d => d.TypeFullName!.EndsWith(nameof(TestPluginCustomApi)));
        }

        [Fact]
        public void CustomApiClass_WithPluginStep_ExcludedFromResults()
        {
            // [CustomApi] marks the class as a Custom API implementation, so it must be skipped
            // even when a [PluginStep] attribute is also present.
            var defs = Discover();
            defs.ShouldNotContain(d => d.TypeFullName!.EndsWith(nameof(TestPluginCustomApiWithStep)));
        }
    }
}
