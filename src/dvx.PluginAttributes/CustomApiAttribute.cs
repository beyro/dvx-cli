using System;

namespace dvx.PluginAttributes
{
    /// <summary>
    /// Marks an IPlugin class as the implementation of a Custom API rather than a standard event
    /// plugin. dvx's reflection discovery (used by <c>sync</c>/<c>register</c>) skips classes marked
    /// with this attribute, and <c>adopt</c> writes it instead of <see cref="PluginStepAttribute"/>
    /// for Custom API implementations it finds in Dataverse.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CustomApiAttribute : Attribute
    {
    }
}
