using System.CommandLine;

namespace dvx.Commands.Shared
{
    public static class CommandSupport
    {
        /// <summary>Adds every option to the command in one call.</summary>
        public static void AddOptions(this Command cmd, params Option[] options)
        {
            foreach (var option in options)
                cmd.AddOption(option);
        }
    }
}
