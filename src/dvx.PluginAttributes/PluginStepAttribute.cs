using System;

namespace dvx.PluginAttributes
{
    /// <summary>
    /// Declares a Dataverse SDK message processing step on an IPlugin class.
    /// Apply multiple times on the same class to register multiple steps.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PluginStepAttribute : Attribute
    {
        /// <summary>
        /// Creates an empty step. Set <see cref="Entity"/>, <see cref="Message"/> and
        /// <see cref="Stage"/> via named arguments.
        /// </summary>
        public PluginStepAttribute() { }

        /// <summary>
        /// Creates a step for the given entity, message and stage.
        /// Pass an empty string for <paramref name="entity"/> on entity-less (global)
        /// messages such as Associate / Disassociate.
        /// </summary>
        public PluginStepAttribute(string entity, string message, Stage stage)
        {
            Entity  = entity;
            Message = message;
            Stage   = stage;
        }

        public string Entity  { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Stage  Stage   { get; set; }

        /// <summary>Execution order within the stage (maps to <c>rank</c>). Default: 1.</summary>
        public int ExecutionOrder { get; set; } = 1;

        /// <summary>Run asynchronously (mode=1). Default: false (synchronous).</summary>
        public bool Async { get; set; } = false;

        /// <summary>Optional description stored on the step record.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Run this step as the Dataverse SYSTEM user.
        /// Cannot be combined with <see cref="RunAsUser"/>.
        /// </summary>
        public bool RunAsSystem { get; set; } = false;

        /// <summary>
        /// The GUID string of a specific Dataverse system user to impersonate when this step runs.
        /// Must be a valid GUID. Cannot be combined with <see cref="RunAsSystem"/>.
        /// Note: C# attribute arguments do not support the <c>Guid</c> type directly, so a string is used here.
        /// </summary>
        public string RunAsUser { get; set; } = "";

        /// <summary>
        /// Attribute logical names that trigger this step on Update.
        /// Empty array = fire on any change (no filtering).
        /// </summary>
        public string[] FilteringAttributes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Unsecure configuration string handed to the plugin constructor at runtime.
        /// Maps to <c>sdkmessageprocessingstep.configuration</c>. Null = not set.
        /// </summary>
        public string? Configuration { get; set; }

        // ── Pre-image ──────────────────────────────────────────────────────────

        /// <summary>Register a pre-image on this step. Alias defaults to "PreImage".</summary>
        public bool UsePreImage { get; set; } = false;

        /// <summary>
        /// Attribute logical names to include in the pre-image.
        /// Empty array = include all attributes.
        /// </summary>
        public string[] PreImageAttributes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Entity alias for the pre-image. Read it in plugin code via
        /// <c>context.PreEntityImages[alias]</c>. Default: "PreImage".
        /// </summary>
        public string PreImageAlias { get; set; } = "PreImage";

        // ── Post-image ─────────────────────────────────────────────────────────

        /// <summary>
        /// Register a post-image on this step. Alias defaults to "PostImage".
        /// Only valid on Stage.PostOperation.
        /// </summary>
        public bool UsePostImage { get; set; } = false;

        /// <summary>
        /// Attribute logical names to include in the post-image.
        /// Empty array = include all attributes.
        /// </summary>
        public string[] PostImageAttributes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Entity alias for the post-image. Read it in plugin code via
        /// <c>context.PostEntityImages[alias]</c>. Default: "PostImage".
        /// </summary>
        public string PostImageAlias { get; set; } = "PostImage";
    }
}
