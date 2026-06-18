namespace dvx.Models
{
    public class PluginStepDefinition
    {
        public string   TypeFullName         { get; set; } = string.Empty;
        public string   Entity               { get; set; } = string.Empty;
        public string   Message              { get; set; } = string.Empty;
        public int      Stage                { get; set; }  // 10 / 20 / 40
        public int      ExecutionOrder       { get; set; } = 1;
        public int      Mode                 { get; set; }  // 0 = sync, 1 = async
        public string?  Description          { get; set; }
        /// <summary>
        /// User to impersonate, resolved from [PluginStep] RunAsSystem / RunAsUser.
        /// <c>null</c> = run as calling user (field not written to Dataverse).
        /// <c>Guid.Empty</c> = run as SYSTEM user.
        /// Any other Guid = impersonate that specific user.
        /// </summary>
        public Guid?    RunAsUser            { get; set; } = null;
        public string[] FilteringAttributes  { get; set; } = Array.Empty<string>();

        /// <summary>Unsecure config string (sdkmessageprocessingstep.configuration). null = not set.</summary>
        public string?  Configuration        { get; set; }

        public List<ImageDefinition> Images  { get; set; } = new();

        /// <summary>
        /// Stable name written to the Dataverse step record.
        /// Format: <c>TypeFullName | entity | message | StageName</c>
        /// </summary>
        public string StepName =>
            $"{TypeFullName} | {Entity.ToLowerInvariant()} | {Message.ToLowerInvariant()} | {StageName(Stage)} | {(Mode == 1 ? "async" : "sync")}";

        /// <summary>Returns the human-readable stage name for a Dataverse stage integer.</summary>
        public static string StageName(int stage) => stage switch
        {
            10 => "PreValidation",
            20 => "PreOperation",
            40 => "PostOperation",
            _  => stage.ToString()
        };
    }
}
