using dvx.Models;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PluginStepDefinitionTests
    {
        [Fact]
        public void StepName_FormatsCorrectly()
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = "MyPlugin.AccountCreateHandler",
                Entity       = "account",
                Message      = "Create",
                Stage        = 20
            };

            def.StepName.ShouldBe("MyPlugin.AccountCreateHandler | account | create | PreOperation | sync");
        }

        [Fact]
        public void StepName_EntityAndMessageAreLowercased()
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = "MyPlugin.Handler",
                Entity       = "Account",    // mixed case
                Message      = "CREATE",     // all caps
                Stage        = 40
            };

            def.StepName.ShouldContain("| account |");
            def.StepName.ShouldContain("| create |");
        }

        [Theory]
        [InlineData(10, "| PreValidation |")]
        [InlineData(20, "| PreOperation |")]
        [InlineData(40, "| PostOperation |")]
        public void StepName_IncludesStageAsText(int stage, string expected)
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = "Ns.Plugin",
                Entity       = "contact",
                Message      = "Update",
                Stage        = stage
            };

            def.StepName.ShouldContain(expected);
        }

        [Theory]
        [InlineData(0, "| sync")]
        [InlineData(1, "| async")]
        public void StepName_IncludesSyncOrAsync(int mode, string expected)
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = "Ns.Plugin",
                Entity       = "account",
                Message      = "Create",
                Stage        = 40,
                Mode         = mode
            };

            def.StepName.ShouldEndWith(expected);
        }

        [Fact]
        public void StepName_StartsWithTypeFullName()
        {
            var def = new PluginStepDefinition
            {
                TypeFullName = "Ns.Plugin",
                Entity       = "account",
                Message      = "Delete",
                Stage        = 10
            };

            def.StepName.ShouldStartWith("Ns.Plugin |");
        }

        [Fact]
        public void StepName_DifferentStages_ProduceDifferentNames()
        {
            var pre = new PluginStepDefinition { TypeFullName = "P", Entity = "a", Message = "m", Stage = 20 };
            var post = new PluginStepDefinition { TypeFullName = "P", Entity = "a", Message = "m", Stage = 40 };

            pre.StepName.ShouldNotBe(post.StepName);
        }
    }
}
